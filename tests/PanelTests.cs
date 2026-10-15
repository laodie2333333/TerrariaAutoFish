using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TerrariaAutoFish;

internal static class PanelTests
{
    private sealed class RecordingBridge : IBotBridge
    {
        public bool FilterEnabled;
        public int[] Targets = new int[0];
        public int FilterChanges, Starts;
        public bool InventoryFullStop;
        public int InventoryFullStopChanges;
        public void UpdateSonarFilter(bool enabled, int[] ids) { FilterEnabled = enabled; Targets = (int[])ids.Clone(); FilterChanges++; }
        public void UpdateInventoryFullStop(bool enabled) { InventoryFullStop = enabled; InventoryFullStopChanges++; }
        public void Start(BotOptions options) { Starts++; }
        public void Stop() { }
        public BotSnapshot Snapshot() { return new BotSnapshot { Enabled = true, InWorld = true }; }
        public string[] DrainLog() { return new string[0]; }
        public bool IsGameForeground() { return false; }
        public CatchCatalogEntry[] CatchCatalog() { return new CatchCatalogEntry[0]; }
    }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static Type panelType;
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern int GetScrollPos(IntPtr window, int bar);
    private static object Field(object panel, string name) { return panelType.GetField(name, Private).GetValue(panel); }
    private static object Call(object panel, string method, params object[] args) { return panelType.GetMethod(method, Private).Invoke(panel, args); }
    private static void Assert(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception("FAIL: " + message);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void CheckLayout(Form panel, string scenario)
    {
        panel.PerformLayout();
        Application.DoEvents();
        var list = (Control)Field(panel, "catchList");
        var search = (Control)Field(panel, "catchSearch");
        var hint = Descendants(panel).Single(control => control.Name == "searchHint");
        var description = Descendants(panel).Single(control => control.Name == "filterHint");
        Assert(description.RectangleToScreen(description.ClientRectangle).Bottom <= hint.PointToScreen(Point.Empty).Y,
            scenario + ": explanation does not overlap search caption");
        Assert(hint.RectangleToScreen(hint.ClientRectangle).Bottom <= search.PointToScreen(Point.Empty).Y,
            scenario + ": caption is above the search input");
        Assert(list.Height > list.Font.Height * 4, scenario + ": target list has room for multiple rows; list=" + list.Size
            + "; window=" + panel.ClientSize + "; minimum=" + panel.MinimumSize);
        var page = (Control)Field(panel, "page");
        Assert(page.Size == page.Parent.ClientSize, scenario + ": page follows client area without overflow");
        Assert(!((ScrollableControl)page.Parent).AutoScroll && !((ScrollableControl)page.Parent).HorizontalScroll.Visible
            && !((ScrollableControl)page.Parent).VerticalScroll.Visible, scenario + ": no outer scrollbars");
        foreach (var label in Descendants(panel).OfType<Label>().Where(label => label.Text.Length > 0))
        {
            var measured = TextRenderer.MeasureText(label.Text, label.Font, new Size(label.ClientSize.Width, Int32.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            Assert(label.Height >= measured.Height, scenario + ": full label height: " + label.Text);
            Assert(label.Left >= 0 && label.Top >= 0 && label.Right <= label.Parent.ClientSize.Width
                && label.Bottom <= label.Parent.ClientSize.Height, scenario + ": label stays inside parent: " + label.Text);
        }
        CheckButtonPainting(panel, scenario);
    }

    private static void CheckButtonPainting(Form window, string scenario)
    {
        foreach (var button in Descendants(window).OfType<Button>())
        {
            var expected = window.BackColor;
            for (Control ancestor = button.Parent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor.GetType().Name == "RoundedCard") { expected = Color.FromArgb(26, 34, 48); break; }
            bool wasEnabled = button.Enabled;
            try
            {
                for (int state = 0; state < 4; state++)
                {
                    button.Enabled = state != 3;
                    button.GetType().GetMethod("OnMouseLeave", Private).Invoke(button, new object[] { EventArgs.Empty });
                    if (state == 1 || state == 2)
                        button.GetType().GetMethod("OnMouseEnter", Private).Invoke(button, new object[] { EventArgs.Empty });
                    if (state == 2)
                        button.GetType().GetMethod("OnMouseDown", Private).Invoke(button,
                            new object[] { new MouseEventArgs(MouseButtons.Left, 1, button.Width / 2, button.Height / 2, 0) });
                    using (var bitmap = new Bitmap(button.Width, button.Height))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.Magenta);
                        IntPtr target = graphics.GetHdc();
                        // WM_PAINT exercises the on-screen double-buffered path. DrawToBitmap's
                        // WM_PRINT path can hide the missing transparent background regression.
                        try { SendMessage(button.Handle, 0x000F, target, IntPtr.Zero); }
                        finally { graphics.ReleaseHdc(target); }
                        var corners = new[] { new Point(0, 0), new Point(button.Width - 1, 0),
                            new Point(0, button.Height - 1), new Point(button.Width - 1, button.Height - 1) };
                        Assert(corners.All(point => bitmap.GetPixel(point.X, point.Y).ToArgb() == expected.ToArgb()),
                            scenario + ": native button corners match the parent background: " + button.Text + "; state=" + state
                            + "; actual=" + bitmap.GetPixel(0, 0) + "; expected=" + expected);
                    }
                }
            }
            finally
            {
                button.GetType().GetMethod("OnMouseUp", Private).Invoke(button,
                    new object[] { new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0) });
                button.GetType().GetMethod("OnMouseLeave", Private).Invoke(button, new object[] { EventArgs.Empty });
                button.Enabled = wasEnabled;
            }
        }
    }

    private static void ClickList(CheckedListBox list, Point point, bool doubleClick, bool rightClick)
    {
        var position = new IntPtr((point.Y << 16) | (point.X & 0xFFFF));
        int down = rightClick ? 0x0204 : doubleClick ? 0x0203 : 0x0201;
        SendMessage(list.Handle, down, new IntPtr(rightClick ? 2 : 1), position);
        SendMessage(list.Handle, rightClick ? 0x0205 : 0x0202, IntPtr.Zero, position);
    }

    private static void CheckTargetClicks(Form panel, string scenario)
    {
        var list = (CheckedListBox)Field(panel, "catchList");
        var search = (TextBox)Field(panel, "catchSearch");
        string previousSearch = search.Text;
        int previousSelection = list.SelectedIndex;
        int previousExtent = list.HorizontalExtent;
        search.Text = ((CatchCatalogEntry)list.Items[0]).Id.ToString();
        var row = list.GetItemRectangle(0);
        var text = new Point(Math.Min(100, list.ClientSize.Width - 10), row.Top + row.Height / 2);
        var box = new Point(row.Left + 5, text.Y);
        bool original = list.GetItemChecked(0);
        int changes = 0;
        ItemCheckEventHandler changed = (sender, e) => changes++;
        list.ItemCheck += changed;
        try
        {
            ClickList(list, text, false, false);
            ClickList(list, text, false, false);
            ClickList(list, text, true, false);
            ClickList(list, new Point(list.ClientSize.Width - 10, text.Y), false, false);
            ClickList(list, box, false, true);
            ClickList(list, new Point(text.X, list.ClientSize.Height - 5), false, false);
            Assert(list.GetItemChecked(0) == original && changes == 0,
                scenario + ": text, repeated/double clicks, row whitespace, empty area and right click do not toggle targets");
            ClickList(list, text, false, false);
            Assert(list.SelectedIndex == 0, scenario + ": text click still highlights its row");
            ClickList(list, box, false, false);
            Assert(list.GetItemChecked(0) != original && changes == 1, scenario + ": checkbox click toggles exactly once");
            ClickList(list, box, false, false);
            Assert(list.GetItemChecked(0) == original && changes == 2, scenario + ": repeated checkbox click toggles back");
            SendMessage(list.Handle, 0x0102, new IntPtr(' '), IntPtr.Zero);
            Assert(list.GetItemChecked(0) != original && changes == 3, scenario + ": keyboard space can check the target");
            SendMessage(list.Handle, 0x0102, new IntPtr(' '), IntPtr.Zero);
            Assert(list.GetItemChecked(0) == original && changes == 4, scenario + ": keyboard space can uncheck the target");
            list.HorizontalExtent = list.ClientSize.Width * 3;
            SendMessage(list.Handle, 0x0114, new IntPtr(7), IntPtr.Zero); // WM_HSCROLL / SB_RIGHT.
            Assert(GetScrollPos(list.Handle, 0) > row.Width, scenario + ": list is horizontally scrolled past its checkbox");
            ClickList(list, box, false, false);
            Assert(list.GetItemChecked(0) == original && changes == 4,
                scenario + ": left-side text or whitespace cannot toggle a horizontally hidden checkbox");
            SendMessage(list.Handle, 0x0114, new IntPtr(6), IntPtr.Zero); // SB_LEFT.
            list.HorizontalExtent = previousExtent;
            ClickList(list, box, false, false);
            Assert(list.GetItemChecked(0) != original && changes == 5,
                scenario + ": checkbox remains clickable after scrolling back");
            ClickList(list, box, false, false);
        }
        finally
        {
            list.ItemCheck -= changed;
            SendMessage(list.Handle, 0x0114, new IntPtr(6), IntPtr.Zero);
            list.HorizontalExtent = previousExtent;
            list.SetItemChecked(0, original);
            search.Text = previousSearch;
            list.SelectedIndex = previousSelection < list.Items.Count ? previousSelection : -1;
        }
    }

    private static void SavePreview(Form panel, string path)
    {
        using (var bitmap = new Bitmap(panel.Width, panel.Height))
        {
            panel.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex)
        {
            while (ex != null)
            {
                Console.WriteLine(ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                ex = ex.InnerException;
            }
            Environment.ExitCode = 1;
        }
    }

    private static void Run(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        panelType = Assembly.LoadFrom(args[0]).GetType("TerrariaAutoFish.ControlPanel", true);
        string dataDirectory = Path.Combine(Path.GetFullPath(args[1]), "panel-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllLines(Path.Combine(dataDirectory, "settings.txt"), new[] { "D:\\Terraria.exe", "900", "200", "30", "True", "False", "4", "False" });
        var catalog = new[] {
            new CatchCatalogEntry { Id = 2308, Name = "装甲洞穴鱼", Category = "鱼获" },
            new CatchCatalogEntry { Id = 2314, Name = "蜂蜜鱼", Category = "鱼获" },
            new CatchCatalogEntry { Id = 2334, Name = "木匣", Category = "宝匣" },
            new CatchCatalogEntry { Id = 2450, Name = "任务鱼示例", Category = "任务鱼" }
        };
        using (var panel = (Form)Activator.CreateInstance(panelType, Private, null, new object[] { dataDirectory }, null))
        {
            Assert(((NumericUpDown)Field(panel, "recast")).Value == 900 && !((CheckBox)Field(panel, "stopFull")).Checked,
                "legacy eight-line preferences preserved");
            Assert(!((CheckBox)Field(panel, "sonarFilter")).Checked, "legacy preferences default sonar to disabled");
            panelType.GetField("catalog", Private).SetValue(panel, catalog);
            Call(panel, "RefreshCatchList");
            var list = (CheckedListBox)Field(panel, "catchList");
            var search = (TextBox)Field(panel, "catchSearch");
            var category = (ComboBox)Field(panel, "catchCategory");
            list.SetItemChecked(0, true);
            search.Text = "蜂蜜";
            Assert(list.Items.Count == 1 && ((CatchCatalogEntry)list.Items[0]).Id == 2314, "localized name search");
            Call(panel, "SelectVisibleCatches", true);
            search.Text = "2334";
            Assert(list.Items.Count == 1, "item ID search");
            Call(panel, "SelectVisibleCatches", true);
            search.Clear();
            category.SelectedIndex = 1;
            Call(panel, "SelectVisibleCatches", false);
            var options = (BotOptions)Call(panel, "Options");
            Assert(options.SelectedCatchIds.OrderBy(id => id).SequenceEqual(new[] { 2308, 2314 }), "clearing crates preserves hidden selected fish");
            category.SelectedIndex = 3;
            Assert(list.Items.Count == 2, "selected category reflects complete selection");
            ((CheckBox)Field(panel, "sonarFilter")).Checked = true;
            Call(panel, "SavePreferences");
            var live = new RecordingBridge();
            panelType.GetField("bridge", Private).SetValue(panel, live);
            panelType.GetField("gameRunning", Private).SetValue(panel, true);
            var stopFull = (CheckBox)Field(panel, "stopFull");
            stopFull.Checked = true;
            Assert(live.InventoryFullStop && live.InventoryFullStopChanges == 1 && live.Starts == 0,
                "enabling the inventory-full safeguard updates the running session without restarting");
            Assert(File.ReadAllLines(Path.Combine(dataDirectory, "settings.txt"))[5] == "True",
                "enabling the inventory-full safeguard immediately saves the preference");
            stopFull.Checked = false;
            Assert(!live.InventoryFullStop && live.InventoryFullStopChanges == 2 && live.Starts == 0,
                "disabling the inventory-full safeguard updates the running session without restarting");
            Assert(File.ReadAllLines(Path.Combine(dataDirectory, "settings.txt"))[5] == "False",
                "disabling the inventory-full safeguard immediately saves the preference");
            list.SetItemChecked(0, false);
            Assert(live.FilterEnabled && live.Targets.SequenceEqual(new[] { 2314 }), "unchecking immediately replaces live targets");
            list.SetItemChecked(1, false);
            Assert(live.FilterEnabled && live.Targets.Length == 0, "clearing targets keeps an empty enabled live filter");
            category.SelectedIndex = 0;
            list.SetItemChecked(2, true);
            Assert(live.Targets.SequenceEqual(new[] { 2334 }), "newly selected catch replaces the original target while running");
            ((CheckBox)Field(panel, "sonarFilter")).Checked = false;
            Assert(!live.FilterEnabled, "live filter switch can be disabled");
            ((CheckBox)Field(panel, "sonarFilter")).Checked = true;
            Assert(live.FilterEnabled, "live filter switch can be enabled");
            search.Text = "蜂蜜";
            Call(panel, "SelectVisibleCatches", true);
            Assert(live.Targets.SequenceEqual(new[] { 2314, 2334 }) && live.Starts == 0,
                "bulk live selection does not restart fishing");
            search.Clear();
            list.SetItemChecked(2, false);
            list.SetItemChecked(0, true);
            panelType.GetField("gameRunning", Private).SetValue(panel, false);
            panelType.GetField("bridge", Private).SetValue(panel, null);
            stopFull.Checked = true;
            Assert(live.InventoryFullStopChanges == 2, "changing the saved safeguard while the game is closed sends no live command");
            using (var tutorial = (Form)Call(panel, "CreateTutorialWindow"))
            {
                tutorial.Opacity = 0;
                tutorial.ShowInTaskbar = false;
                tutorial.Show();
                Application.DoEvents();
                CheckButtonPainting(tutorial, "tutorial window");
                var content = Descendants(tutorial).OfType<Label>().Single(label => label.Name == "tutorialContent");
                Assert(content.Text.Contains("F8") && content.Text.Contains("F9"), "tutorial explains starting and stopping");
                Assert(!content.Text.Contains("保持 Steam 登录") && !content.Text.Contains("工具只能接管"),
                    "first tutorial page uses the simplified startup instructions");
                var sonarTopic = Descendants(tutorial).OfType<Button>().Single(button => button.Name == "tutorialTopic1");
                sonarTopic.PerformClick();
                Assert(content.Text.Contains("声纳药水") && content.Text.Contains("更改选择的渔获时，无需停止或重新抛竿"),
                    "tutorial explains live sonar filtering in plain language");
                Descendants(tutorial).OfType<Button>().Single(button => button.Name == "tutorialTopic2").PerformClick();
                Assert(content.Text.Contains("背包") && content.Text.Contains("下次开始"), "tutorial explains inventory and settings");
                if (args.Length > 2) SavePreview(tutorial, Path.ChangeExtension(args[2], "tutorial.png"));
            }
        }
        using (var restored = (Form)Activator.CreateInstance(panelType, Private, null, new object[] { dataDirectory }, null))
        {
            var options = (BotOptions)Call(restored, "Options");
            Assert(options.SonarFilterEnabled && options.SelectedCatchIds.SequenceEqual(new[] { 2308, 2314 }), "sonar targets and enabled switch persist across launches");
            Assert(options.StopWhenInventoryFull, "the inventory-full safeguard preference persists across launches");
            if (args.Length > 2)
            {
                if (args.Length > 3)
                    catalog = File.ReadAllLines(args[3]).Select(line => line.Split('\t')).Select(parts => new CatchCatalogEntry {
                        Id = Int32.Parse(parts[0]), Category = parts[1], Name = parts[2] }).ToArray();
                panelType.GetField("catalog", Private).SetValue(restored, catalog);
                Call(restored, "RefreshCatchList");
                restored.Opacity = 0;
                restored.ShowInTaskbar = false;
                restored.Show();
                Application.DoEvents();
                ((Label)Field(restored, "status")).Text = "连续 3 次等待超时，已停止；请检查落点、水量及岩浆钓鱼条件，确认后重新手动抛竿并开始接管。";
                ((Label)Field(restored, "sessionInfo")).Text = "本次挂机 12:34:56 · 超时 2/10 · 声纳跳过 12345 次 · 装甲洞穴鱼 · 已跳过 · 日志已开启";
                CheckTargetClicks(restored, "normal window");
                CheckLayout(restored, "normal window");
                SavePreview(restored, args[2]);
                var normalList = ((Control)Field(restored, "catchList")).Size;
                restored.ClientSize = new Size(1920, 1080);
                CheckLayout(restored, "expanded window");
                var expandedList = ((Control)Field(restored, "catchList")).Size;
                Assert(expandedList.Width > normalList.Width * 1.5 && expandedList.Height > normalList.Height + 180,
                    "expanding the window grows both list dimensions");
                Assert(((Control)Field(restored, "page")).Width >= restored.ClientSize.Width - 25,
                    "expanded content fills the window width");
                SavePreview(restored, Path.ChangeExtension(args[2], "wide.png"));
                restored.WindowState = FormWindowState.Maximized;
                CheckLayout(restored, "maximized window");
                Assert(((Control)Field(restored, "page")).Width >= restored.ClientSize.Width - 25,
                    "maximized content fills the window width");
                restored.WindowState = FormWindowState.Normal;
                restored.Size = new Size(400, 300);
                CheckLayout(restored, "minimum window");
                Assert(restored.Size.Width >= restored.MinimumSize.Width && restored.Size.Height >= restored.MinimumSize.Height,
                    "minimum size blocks shrinking into clipped content");
                restored.ClientSize = new Size(1080, 800);
                CheckLayout(restored, "restored window");
                // Exercise WinForms scaling without changing the user's Windows display settings.
                foreach (float factor in new[] { 1.25f, 1.2f })
                {
                    var fonts = Descendants(restored).Concat(new Control[] { restored })
                        .Select(control => new { Control = control, Family = control.Font.FontFamily,
                            Size = control.Font.Size, Style = control.Font.Style }).ToArray();
                    restored.Scale(new SizeF(factor, factor));
                    foreach (var entry in fonts)
                        entry.Control.Font = new Font(entry.Family, entry.Size * factor, entry.Style);
                    restored.Size = factor == 1.25f ? new Size(1366, 1039) : new Size(1636, 1039);
                    CheckTargetClicks(restored, factor == 1.25f ? "125 percent scaling" : "150 percent scaling");
                    CheckLayout(restored, factor == 1.25f ? "125 percent scaling" : "150 percent scaling");
                    SavePreview(restored, Path.ChangeExtension(args[2], factor == 1.25f ? "125.png" : "150.png"));
                }
                restored.Close();
            }
        }
        Console.WriteLine("PASS: " + checks + " panel checks; isolated data at " + dataDirectory);
    }
}
