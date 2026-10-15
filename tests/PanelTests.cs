using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using TerrariaAutoFish;

internal static class PanelTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static Type panelType;
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
        Assert(list.Height > list.Font.Height * 4, scenario + ": target list has room for multiple rows");
        foreach (var label in Descendants(panel).OfType<Label>().Where(label => label.Text.Length > 0))
        {
            var measured = TextRenderer.MeasureText(label.Text, label.Font, new Size(label.ClientSize.Width, Int32.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            Assert(label.Height >= measured.Height, scenario + ": full label height: " + label.Text);
            Assert(label.Left >= 0 && label.Top >= 0 && label.Right <= label.Parent.ClientSize.Width
                && label.Bottom <= label.Parent.ClientSize.Height, scenario + ": label stays inside parent: " + label.Text);
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
        }
        using (var restored = (Form)Activator.CreateInstance(panelType, Private, null, new object[] { dataDirectory }, null))
        {
            var options = (BotOptions)Call(restored, "Options");
            Assert(options.SonarFilterEnabled && options.SelectedCatchIds.SequenceEqual(new[] { 2308, 2314 }), "sonar targets and enabled switch persist across launches");
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
                restored.ClientSize = new Size(850, 580);
                CheckLayout(restored, "small scrollable window");
                Assert(((Control)Field(restored, "page")).Width > restored.ClientSize.Width,
                    "small windows preserve readable controls with scrolling");
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
                    restored.ClientSize = factor == 1.25f ? new Size(1350, 1000) : new Size(1620, 1000);
                    CheckLayout(restored, factor == 1.25f ? "125 percent scaling" : "150 percent scaling");
                    SavePreview(restored, Path.ChangeExtension(args[2], factor == 1.25f ? "125.png" : "150.png"));
                }
                restored.Close();
            }
        }
        Console.WriteLine("PASS: " + checks + " panel checks; isolated data at " + dataDirectory);
    }
}
