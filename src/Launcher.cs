using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TerrariaAutoFish
{
    internal static class Entry
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--check")
            {
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "compatibility-check.txt");
                try
                {
                    GameLoader.Prepare(args[1]);
                    GameLoader.CreateRuntime();
                    string isolatedSaves = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-check-saves");
                    GameLoader.CheckStartup(isolatedSaves);
                    File.WriteAllText(report, "PASS: Original entry point and LaunchGame executed.\r\n"
                        + "PASS: XNA content location points to installed game; PixelShader.xnb opened.\r\n"
                        + "PASS: SavePath initialized before Main; WorldPath and PlayerPath verified.\r\n"
                        + "PASS: Main static initializer completed; all fishing and inventory-pause patches verified.\r\n"
                        + "No game window was launched. Isolated check directory: " + isolatedSaves + "\r\n"
                        + "Actual fishing requires an in-game trial.\r\n");
                    return 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(report, ex.ToString());
                    return 1;
                }
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\TerrariaAutoFish1458", out created))
            {
                if (!created)
                {
                    MessageBox.Show("钓鱼启动器已经打开，请查看任务栏或托盘。", "泰拉瑞亚后台钓鱼");
                    return 1;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ControlPanel());
            }
            return 0;
        }
    }

    internal sealed class ThemedButton : Button
    {
        private bool hovered, pressed;
        public Color Surface { get; set; }
        public Color Ink { get; set; }
        public ThemedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            // ButtonBase enables Opaque by default. Clear it so WM_PAINT draws the
            // transparent parent background before painting the rounded button.
            SetStyle(ControlStyles.Opaque, false);
            BackColor = Color.Transparent;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Surface = Color.FromArgb(37, 49, 67);
            Ink = Color.FromArgb(225, 233, 244);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color surface = !Enabled ? Color.FromArgb(32, 41, 56) : pressed
                ? ControlPaint.Dark(Surface, 0.12f) : hovered ? ControlPaint.Light(Surface, 0.12f) : Surface;
            using (var path = RoundedCard.Shape(new RectangleF(1, 1, Width - 3, Height - 3), Math.Min(12, Height / 3f)))
            {
                using (var brush = new SolidBrush(surface)) e.Graphics.FillPath(brush, path);
                using (var pen = new Pen(Enabled ? ControlPaint.Light(surface, 0.12f) : Color.FromArgb(44, 54, 70)))
                    e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? Ink : Color.FromArgb(119, 137, 159),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -7, -7), Ink, surface);
        }
    }

    internal sealed class RoundedCard : Panel
    {
        public RoundedCard() { DoubleBuffered = true; BackColor = Color.Transparent; }
        internal static GraphicsPath Shape(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Max(1, radius * 2);
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Shape(new RectangleF(0, 0, Width - 1, Height - 1), 12))
            {
                using (var brush = new SolidBrush(Color.FromArgb(26, 34, 48))) e.Graphics.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(43, 54, 72))) e.Graphics.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }

    internal sealed class ControlPanel : Form
    {
        private static readonly Color Background = Color.FromArgb(14, 20, 30);
        private static readonly Color TextColor = Color.FromArgb(226, 232, 240);
        private static readonly Color Muted = Color.FromArgb(148, 163, 184);
        private static readonly Color Accent = Color.FromArgb(239, 195, 104);
        private readonly TextBox pathBox = new TextBox();
        private readonly Button launchButton = new ThemedButton();
        private readonly Button startButton = new ThemedButton();
        private readonly Button stopButton = new ThemedButton();
        private readonly Label status = new Label();
        private readonly Label telemetry = new Label();
        private readonly Label heartbeat = new Label();
        private readonly TextBox logBox = new TextBox();
        private readonly NumericUpDown recast = new NumericUpDown();
        private readonly NumericUpDown timeout = new NumericUpDown();
        private readonly NumericUpDown health = new NumericUpDown();
        private readonly NumericUpDown maxTimeouts = new NumericUpDown();
        private readonly CheckBox skipEnemies = new CheckBox();
        private readonly CheckBox stopFull = new CheckBox();
        private readonly CheckBox notifyStops = new CheckBox();
        private readonly Label sessionInfo = new Label();
        private readonly Panel page = new Panel();
        private TableLayoutPanel rootLayout;
        private readonly Button tutorialButton = new ThemedButton();
        private readonly CheckBox sonarFilter = new CheckBox();
        private readonly TextBox catchSearch = new TextBox();
        private readonly ComboBox catchCategory = new ComboBox();
        private readonly CheckedListBox catchList = new TargetCheckedListBox();
        private readonly Label filterSummary = new Label();
        private readonly Label sonarStatus = new Label();
        private readonly Label stateBadge = new Label();
        private readonly HashSet<int> selectedCatches = new HashSet<int>();
        private CatchCatalogEntry[] catalog = new CatchCatalogEntry[0];
        private bool updatingCatchList;
        private bool loadingPreferences;
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private IBotBridge bridge;
        private bool gameRunning;
        private bool gameWasLaunched;
        private bool allowClose;
        private bool f8Registered, f9Registered;
        private long previousUpdates;
        private DateTime previousSample = DateTime.UtcNow;
        private double updateRate;
        private long lastStopSequence;
        private bool updateWarningShown;
        private bool logErrorShown;
        private SessionLog sessionLog;
        private readonly string preferencesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TerrariaAutoFish");

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr handle, int id);

        public ControlPanel() : this(null) { }

        internal ControlPanel(string dataDirectory)
        {
            if (!String.IsNullOrWhiteSpace(dataDirectory)) preferencesDirectory = dataDirectory;
            Text = "TerrariaAutoFish·自动后台钓鱼";
            ClientSize = new Size(1080, 800);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Background;
            ForeColor = TextColor;
            Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            using (var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TerrariaAutoFish.GoldenFishingRod.ico"))
            using (var appIcon = new Icon(iconStream))
                Icon = (Icon)appIcon.Clone();
            BuildLayout();
            UpdateMinimumWindowSize();
            loadingPreferences = true;
            LoadPreferences();
            loadingPreferences = false;
            RefreshCatchList();
            sessionLog = new SessionLog(Path.Combine(preferencesDirectory, "Logs"));
            AppendLog("控制面板已打开；本次运行日志会持续保存到日志文件夹。");
            if (String.IsNullOrWhiteSpace(pathBox.Text)) pathBox.Text = FindGame();
            tray.Icon = Icon;
            tray.Text = "泰拉瑞亚后台钓鱼";
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示控制面板", null, (s, e) => RestoreWindow());
            menu.Items.Add("停止钓鱼（F9）", null, (s, e) => StopFishing());
            menu.Items.Add("打开日志文件夹", null, (s, e) => OpenLogs());
            menu.Items.Add("退出控制面板", null, (s, e) => Close());
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => RestoreWindow();
            tray.Visible = true;
            timer.Interval = 300;
            timer.Tick += (s, e) => RefreshStatus();
            timer.Start();
            FormClosing += OnClosing;
            Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) Hide(); };
            FontChanged += (s, e) => UpdateMinimumWindowSize();
            Shown += (s, e) => {
                UpdateMinimumWindowSize();
                f8Registered = RegisterHotKey(Handle, 8, 0x4000, (uint)Keys.F8);
                f9Registered = RegisterHotKey(Handle, 9, 0x4000, (uint)Keys.F9);
                AppendLog("仅支持原版 Windows / Steam Terraria 1.4.5.8。先保存退出当前游戏，再从这里启动。");
                if (!f8Registered) AppendLog("F8 注册失败（可能已被其他工具占用），请使用开始按钮。");
                if (!f9Registered) AppendLog("F9 注册失败（可能已被其他工具占用），请使用停止按钮或托盘菜单。");
            };
        }

        private TableLayoutPanel Grid(int columns, params SizeType[] rows)
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns,
                RowCount = rows.Length, Margin = Padding.Empty, Padding = Padding.Empty,
                BackColor = Color.Transparent, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            for (int i = 0; i < columns; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            foreach (var row in rows) grid.RowStyles.Add(new RowStyle(row, row == SizeType.Percent ? 100f : 0f));
            return grid;
        }

        private Label CopyLabel(Label label, string text, Color color, float size = 10f, FontStyle style = FontStyle.Regular)
        {
            label.Text = text;
            label.AutoSize = true;
            label.Dock = DockStyle.Fill;
            label.Margin = new Padding(0, 4, 0, 8);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.Font = new Font(Font.FontFamily, size, style);
            label.UseMnemonic = false;
            return label;
        }

        private Label CopyLabel(string text, Color color, float size = 10f, FontStyle style = FontStyle.Regular)
        {
            return CopyLabel(new Label(), text, color, size, style);
        }

        private void StyleButton(Button button, string text, Color background)
        {
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(0, 38);
            button.Padding = new Padding(12, 7, 12, 7);
            button.Margin = new Padding(0, 4, 0, 4);
            var themed = (ThemedButton)button;
            themed.Surface = background;
            themed.Ink = background == Accent ? Background : TextColor;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;
            button.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        }

        private Panel MakeCard(Control content)
        {
            var card = new RoundedCard { Dock = DockStyle.Fill, Padding = new Padding(18),
                Margin = Padding.Empty };
            card.Controls.Add(content);
            return card;
        }

        private void ConfigureCheck(CheckBox check, string text, bool initial)
        {
            check.Text = text;
            check.AutoSize = true;
            check.Dock = DockStyle.Fill;
            check.Margin = new Padding(0, 6, 8, 6);
            check.Checked = initial;
        }

        private void BuildLayout()
        {
            // The form's minimum size protects the full layout. Only the fish list scrolls.
            var viewport = new Panel { Dock = DockStyle.Fill, BackColor = Background };
            page.Dock = DockStyle.Fill;
            page.BackColor = Background;
            viewport.Controls.Add(page);
            Controls.Add(viewport);
            var root = Grid(1, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
            rootLayout = root;
            root.AutoSize = false;
            root.Padding = new Padding(24, 18, 24, 14);
            page.Controls.Add(root);

            var heading = Grid(4, SizeType.AutoSize);
            heading.ColumnStyles.Clear();
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var emblem = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(46, 46),
                Margin = new Padding(0, 10, 16, 0) };
            using (var imageStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TerrariaAutoFish.GoldenFishingRod.png"))
            using (var image = new Bitmap(imageStream)) emblem.Image = new Bitmap(image);
            heading.Controls.Add(emblem, 0, 0);
            var title = Grid(1, SizeType.AutoSize, SizeType.AutoSize);
            title.Controls.Add(CopyLabel("TerrariaAutoFish", TextColor, 22f, FontStyle.Bold), 0, 0);
            title.Controls.Add(CopyLabel("泰拉瑞亚后台钓鱼  /  Steam 原版 1.4.5.8", Muted, 9f), 0, 1);
            heading.Controls.Add(title, 1, 0);
            heading.Controls.Add(CopyLabel("v0.4.0", Accent, 10f, FontStyle.Bold), 2, 0);
            StyleButton(tutorialButton, "操作教程", Color.FromArgb(37, 49, 67));
            tutorialButton.Dock = DockStyle.None;
            tutorialButton.Anchor = AnchorStyles.Right;
            tutorialButton.Margin = new Padding(18, 4, 0, 4);
            tutorialButton.Click += (s, e) => ShowTutorial();
            heading.Controls.Add(tutorialButton, 3, 0);
            heading.Margin = new Padding(0, 0, 0, 16);
            root.Controls.Add(heading, 0, 0);

            var game = Grid(3, SizeType.AutoSize, SizeType.AutoSize);
            game.ColumnStyles.Clear();
            game.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            game.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            game.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var pathLabel = CopyLabel("游戏位置", TextColor, 10f, FontStyle.Bold);
            game.Controls.Add(pathLabel, 0, 0);
            game.SetColumnSpan(pathLabel, 3);
            pathBox.Dock = DockStyle.Fill;
            pathBox.Margin = new Padding(0, 10, 14, 4);
            pathBox.BackColor = Background;
            pathBox.ForeColor = TextColor;
            pathBox.BorderStyle = BorderStyle.FixedSingle;
            game.Controls.Add(pathBox, 0, 1);
            var browse = new ThemedButton();
            StyleButton(browse, "选择文件", Color.FromArgb(46, 58, 77));
            browse.Margin = new Padding(0, 4, 12, 4);
            browse.Click += (s, e) => {
                using (var dialog = new OpenFileDialog { Filter = "Terraria.exe|Terraria.exe", Title = "选择原版 Terraria.exe" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) pathBox.Text = dialog.FileName;
            };
            game.Controls.Add(browse, 1, 1);
            StyleButton(launchButton, "启动游戏", Accent);
            launchButton.Click += (s, e) => LaunchGame();
            game.Controls.Add(launchButton, 2, 1);
            var gameCard = MakeCard(game);
            gameCard.AutoSize = true;
            gameCard.Margin = new Padding(0, 0, 0, 16);
            root.Controls.Add(gameCard, 0, 1);

            var body = Grid(2, SizeType.Percent);
            body.AutoSize = false;
            body.ColumnStyles[0].Width = 59;
            body.ColumnStyles[1].Width = 41;
            root.Controls.Add(body, 0, 2);
            var left = Grid(1, SizeType.Percent, SizeType.AutoSize);
            left.AutoSize = false;
            left.Margin = new Padding(0, 0, 16, 0);
            body.Controls.Add(left, 0, 0);

            var state = Grid(1, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize);
            state.AutoSize = false;
            var stateHeading = Grid(2, SizeType.AutoSize);
            stateHeading.Controls.Add(CopyLabel("钓鱼控制", TextColor, 11f, FontStyle.Bold), 0, 0);
            CopyLabel(stateBadge, "●  等待游戏", Muted, 9f);
            stateBadge.TextAlign = ContentAlignment.MiddleRight;
            stateHeading.Controls.Add(stateBadge, 1, 0);
            state.Controls.Add(stateHeading, 0, 0);
            CopyLabel(status, "从启动器进入世界，手动抛一次竿", Accent, 12f, FontStyle.Bold);
            status.Margin = new Padding(0, 8, 0, 12);
            state.Controls.Add(status, 0, 1);
            state.Controls.Add(CopyLabel(telemetry, "角色 —    生命 —    钓竿 —\r\n鱼饵 —    鱼漂 —    重抛 0    收杆 0", TextColor, 9f), 0, 3);
            var actions = Grid(2, SizeType.AutoSize);
            StyleButton(startButton, "开始接管   F8", Accent);
            StyleButton(stopButton, "停止钓鱼   F9", Color.FromArgb(96, 47, 57));
            startButton.Margin = new Padding(0, 4, 6, 0);
            stopButton.Margin = new Padding(6, 4, 0, 0);
            startButton.Enabled = stopButton.Enabled = false;
            startButton.Click += (s, e) => StartFishing();
            stopButton.Click += (s, e) => StopFishing();
            actions.Controls.Add(startButton, 0, 0);
            actions.Controls.Add(stopButton, 1, 0);
            state.Controls.Add(actions, 0, 4);
            var stateCard = MakeCard(state);
            stateCard.Margin = new Padding(0, 0, 0, 16);
            left.Controls.Add(stateCard, 0, 0);

            var settings = Grid(2, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize);
            settings.Controls.Add(CopyLabel("挂机设置", TextColor, 11f, FontStyle.Bold), 0, 0);
            var settingsHint = CopyLabel("参数下次应用 · 满包开关即时生效", Muted, 9f);
            settingsHint.TextAlign = ContentAlignment.MiddleRight;
            settings.Controls.Add(settingsHint, 1, 0);
            settings.Controls.Add(NumberSetting("重抛间隔 / ms", recast, 300, 5000, 800), 0, 1);
            settings.Controls.Add(NumberSetting("等待超时 / 秒", timeout, 30, 1800, 180), 1, 1);
            settings.Controls.Add(NumberSetting("低生命停止 / %", health, 0, 90, 25), 0, 2);
            settings.Controls.Add(NumberSetting("连续超时 / 次", maxTimeouts, 1, 20, 3), 1, 2);
            ConfigureCheck(skipEnemies, "跳过怪物咬钩", true);
            ConfigureCheck(stopFull, "背包无空格时停止", true);
            stopFull.CheckedChanged += (s, e) => {
                if (loadingPreferences) return;
                SavePreferences();
                if (bridge != null && gameRunning) bridge.UpdateInventoryFullStop(stopFull.Checked);
            };
            ConfigureCheck(notifyStops, "自动停止时通知", true);
            notifyStops.CheckedChanged += (s, e) => { if (!loadingPreferences) SavePreferences(); };
            settings.Controls.Add(skipEnemies, 0, 3);
            settings.Controls.Add(stopFull, 1, 3);
            settings.Controls.Add(notifyStops, 0, 4);
            var logsButton = new ThemedButton();
            StyleButton(logsButton, "查看运行日志", Color.FromArgb(46, 58, 77));
            logsButton.Click += (s, e) => ShowLogs();
            settings.Controls.Add(logsButton, 1, 4);
            var settingsCard = MakeCard(settings);
            settingsCard.AutoSize = true;
            left.Controls.Add(settingsCard, 0, 1);

            var sonar = Grid(1, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize,
                SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize);
            sonar.AutoSize = false;
            var sonarHeading = Grid(2, SizeType.AutoSize);
            sonarHeading.Controls.Add(CopyLabel("声纳目标", TextColor, 11f, FontStyle.Bold), 0, 0);
            ConfigureCheck(sonarFilter, "启用筛选", false);
            sonarFilter.Dock = DockStyle.Right;
            sonarFilter.Margin = new Padding(8, 4, 0, 8);
            sonarHeading.Controls.Add(sonarFilter, 1, 0);
            sonar.Controls.Add(sonarHeading, 0, 0);
            sonar.Controls.Add(CopyLabel(sonarStatus, "等待游戏 · 无声纳效果时照常收杆", Accent, 9f), 0, 1);
            var filterHint = CopyLabel("勾选后立即生效；非目标等待咬钩结束。", Muted, 9f);
            filterHint.Name = "filterHint";
            sonar.Controls.Add(filterHint, 0, 2);
            var searchHint = CopyLabel("搜索名称 / 物品 ID", Muted, 9f);
            searchHint.Name = "searchHint";
            searchHint.Margin = new Padding(0, 4, 0, 6);
            sonar.Controls.Add(searchHint, 0, 3);
            var search = Grid(2, SizeType.AutoSize);
            search.ColumnStyles[0].Width = 60;
            search.ColumnStyles[1].Width = 40;
            catchSearch.Dock = DockStyle.Fill;
            catchSearch.Margin = new Padding(0, 0, 10, 0);
            catchSearch.BackColor = Background;
            catchSearch.ForeColor = TextColor;
            catchSearch.BorderStyle = BorderStyle.FixedSingle;
            catchSearch.TextChanged += (s, e) => RefreshCatchList();
            search.Controls.Add(catchSearch, 0, 0);
            catchCategory.Dock = DockStyle.Fill;
            catchCategory.Margin = Padding.Empty;
            catchCategory.DropDownStyle = ComboBoxStyle.DropDownList;
            catchCategory.BackColor = Background;
            catchCategory.ForeColor = TextColor;
            catchCategory.FlatStyle = FlatStyle.Flat;
            catchCategory.DrawMode = DrawMode.OwnerDrawFixed;
            catchCategory.DrawItem += (s, e) => {
                using (var brush = new SolidBrush(Background)) e.Graphics.FillRectangle(brush, e.Bounds);
                if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, catchCategory.Items[e.Index].ToString(),
                    catchCategory.Font, e.Bounds, TextColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                e.DrawFocusRectangle();
            };
            catchCategory.Items.AddRange(new object[] { "全部鱼获", "宝匣", "任务鱼", "已选择" });
            catchCategory.SelectedIndex = 0;
            catchCategory.SelectedIndexChanged += (s, e) => RefreshCatchList();
            search.Controls.Add(catchCategory, 1, 0);
            search.Margin = new Padding(0, 0, 0, 14);
            sonar.Controls.Add(search, 0, 4);
            catchList.Dock = DockStyle.Fill;
            catchList.Margin = new Padding(0, 0, 0, 10);
            catchList.BackColor = Background;
            catchList.ForeColor = TextColor;
            catchList.BorderStyle = BorderStyle.None;
            catchList.CheckOnClick = true;
            catchList.IntegralHeight = false;
            catchList.HorizontalScrollbar = true;
            catchList.Font = new Font(Font.FontFamily, 9f);
            catchList.ItemCheck += (s, e) => {
                if (updatingCatchList) return;
                var item = (CatchCatalogEntry)catchList.Items[e.Index];
                if (e.NewValue == CheckState.Checked) selectedCatches.Add(item.Id);
                else selectedCatches.Remove(item.Id);
                UpdateFilterSummary();
                if (!loadingPreferences) { SavePreferences(); ApplySonarFilter(); }
                if (catchCategory.SelectedIndex == 3 && IsHandleCreated)
                    BeginInvoke((Action)RefreshCatchList);
            };
            sonar.Controls.Add(catchList, 0, 5);
            var selectionActions = Grid(2, SizeType.AutoSize);
            var all = new ThemedButton();
            var none = new ThemedButton();
            StyleButton(all, "勾选当前列表", Color.FromArgb(46, 58, 77));
            StyleButton(none, "取消当前列表", Color.FromArgb(46, 58, 77));
            all.Margin = new Padding(0, 0, 5, 4);
            none.Margin = new Padding(5, 0, 0, 4);
            all.Click += (s, e) => SelectVisibleCatches(true);
            none.Click += (s, e) => SelectVisibleCatches(false);
            selectionActions.Controls.Add(all, 0, 0);
            selectionActions.Controls.Add(none, 1, 0);
            sonar.Controls.Add(selectionActions, 0, 6);
            sonar.Controls.Add(CopyLabel(filterSummary, "", Muted, 9f), 0, 7);
            sonarFilter.CheckedChanged += (s, e) => {
                UpdateFilterSummary();
                if (!loadingPreferences) { SavePreferences(); ApplySonarFilter(); }
            };
            body.Controls.Add(MakeCard(sonar), 1, 0);

            var footer = Grid(1, SizeType.AutoSize, SizeType.AutoSize);
            footer.Margin = new Padding(0, 10, 0, 0);
            footer.Controls.Add(CopyLabel(sessionInfo, "本次挂机 00:00:00   ·   声纳跳过 0 次   ·   日志已开启", TextColor, 9f), 0, 0);
            footer.Controls.Add(CopyLabel(heartbeat, "启动游戏 → 手动抛竿 → F8 接管   /   最小化继续，关闭面板停止", Muted, 9f), 0, 1);
            root.Controls.Add(footer, 0, 3);

            root.Layout += (s, e) => UpdateMinimumWindowSize();
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BackColor = Background;
            logBox.ForeColor = TextColor;
            logBox.BorderStyle = BorderStyle.None;
            logBox.Font = new Font("Microsoft YaHei UI", 9f);
        }

        private void UpdateMinimumWindowSize()
        {
            if (rootLayout == null || IsDisposed) return;
            float scale = rootLayout.Padding.Left / 24f;
            var minimum = SizeFromClientSize(new Size((int)Math.Ceiling(1040 * scale), (int)Math.Ceiling(780 * scale)));
            if (MinimumSize != minimum) MinimumSize = minimum;
        }

        private Form CreateTutorialWindow()
        {
            var window = new Form { Text = "操作教程 · TerrariaAutoFish", ClientSize = new Size(760, 620),
                MinimumSize = new Size(660, 540), StartPosition = FormStartPosition.CenterParent,
                BackColor = Background, ForeColor = TextColor, Font = Font, Icon = Icon };
            var layout = Grid(1, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize);
            layout.AutoSize = false;
            layout.Padding = new Padding(24);
            layout.Controls.Add(CopyLabel("第一次使用？从这里开始", TextColor, 19f, FontStyle.Bold), 0, 0);
            layout.Controls.Add(CopyLabel("先启动游戏，再记录落点；声纳筛选可以在挂机中随时调整。", Muted, 10f), 0, 1);
            var topics = Grid(1, SizeType.AutoSize, SizeType.Percent);
            topics.AutoSize = false;
            var navigation = Grid(3, SizeType.AutoSize);
            var guidePanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Background,
                Margin = new Padding(0, 16, 0, 12), Padding = new Padding(4) };
            var guide = new Label { Name = "tutorialContent", Dock = DockStyle.Top, AutoSize = true,
                ForeColor = TextColor, BackColor = Color.Transparent, Font = new Font(Font.FontFamily, 11f),
                MaximumSize = new Size(680, 0), UseMnemonic = false };
            guidePanel.Controls.Add(guide);
            guidePanel.SizeChanged += (s, e) => guide.MaximumSize = new Size(
                Math.Max(100, guidePanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8), 0);
            string[] titles = { "开始挂机", "筛选鱼获", "背包与常见问题" };
            string[] sections = {
                "01   启动游戏\n\n保存并退出已打开的 Terraria。在主面板确认游戏位置，然后点击“启动游戏”。\n\n"
                + "02   记录抛竿落点\n\n进入世界，在安全的位置手持钓竿并准备鱼饵。手动向水面抛一次竿，程序会记录方向。\n\n"
                + "03   开启与停止\n\n游戏前台按 F8，或点击“开始接管”。看到自动收杆和重抛后，即可切到其他窗口。F9 可随时请求停止。\n\n"
                + "最小化工具会继续挂机，关闭面板会请求停止。离开前请在游戏中保存退出。移动超过 6 格或更换钓竿后，需要重新手动抛竿。",
                "01   选择想要的鱼获\n\n进入世界后，右侧会加载鱼获目录。按名称或物品 ID 搜索，也可以切换“宝匣”“任务鱼”“已选择”。点击左侧方框勾选目标，点击名称或空白只高亮该行。批量勾选或取消只影响当前可见项目。\n\n"
                + "02   开启声纳筛选\n\n打开“启用筛选”，在游戏中喝声纳药水。有声纳效果时只收勾选目标，其他咬钩会等待自然结束。工具不会自动喝药。\n\n"
                + "03   挂机中直接更换\n\n更改选择的渔获时，无需停止或重新抛竿。筛选开启但没有勾选任何项目时，会等待你选择目标。\n\n"
                + "药效结束后会恢复普通钓鱼，面板会提示。多鱼漂一起收杆，有任一非目标咬钩时会暂缓整组收杆，因此可能错过其他目标。",
                "查看背包\n\n挂机时可以打开背包查看收获，不需要重新接管；不要换下钓竿。接管期间会允许背包打开时世界继续更新，角色仍会受到伤害。\n\n"
                + "为什么会自动停止？\n\n生命达到设定阈值、鱼饵耗尽、移动超过 6 格、钓竿变化、打开地图或交互界面等会停止。启用“背包无空格时停止”后，快捷栏和主背包的 50 格全部占用就会停止，即使已有物品还可堆叠；钱币栏、弹药栏和虚空袋不计入。整理背包后需手动重新接管。\n\n"
                + "参数什么时候生效？\n\n声纳目标、筛选开关、背包无空格时停止及通知开关即时生效；其余挂机参数下次开始时应用。\n\n"
                + "收杆次数和鱼数量不同？\n\n收杆次数是操作数，实际收获请查看背包。电脑休眠、游戏卡住或世界不更新时无法钓鱼，可从“查看运行日志”定位原因。多人模式请遵守服务器规则。"
            };
            var topicButtons = new List<ThemedButton>();
            Action<int> selectTopic = index => {
                guide.Text = sections[index];
                guidePanel.AutoScrollPosition = Point.Empty;
                for (int i = 0; i < topicButtons.Count; i++)
                {
                    topicButtons[i].Surface = i == index ? Color.FromArgb(239, 195, 104) : Color.FromArgb(37, 49, 67);
                    topicButtons[i].Ink = i == index ? Background : TextColor;
                    topicButtons[i].Invalidate();
                }
            };
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i;
                var button = new ThemedButton { Name = "tutorialTopic" + i };
                StyleButton(button, titles[i], Color.FromArgb(37, 49, 67));
                button.Margin = new Padding(i == 0 ? 0 : 6, 4, i == titles.Length - 1 ? 0 : 6, 4);
                button.Click += (s, e) => selectTopic(index);
                topicButtons.Add(button);
                navigation.Controls.Add(button, i, 0);
            }
            topics.Controls.Add(navigation, 0, 0);
            topics.Controls.Add(guidePanel, 0, 1);
            layout.Controls.Add(topics, 0, 2);
            var close = new ThemedButton();
            StyleButton(close, "明白了，开始使用", Accent);
            close.DialogResult = DialogResult.OK;
            layout.Controls.Add(close, 0, 3);
            window.AcceptButton = window.CancelButton = close;
            window.Controls.Add(layout);
            selectTopic(0);
            return window;
        }

        private void ShowTutorial()
        {
            using (var window = CreateTutorialWindow()) window.ShowDialog(this);
        }

        private void ShowLogs()
        {
            using (var window = new Form { Text = "运行日志", Size = new Size(860, 500),
                StartPosition = FormStartPosition.CenterParent, BackColor = Background, ForeColor = TextColor,
                Font = Font, Icon = Icon })
            {
                var openFolder = new ThemedButton();
                StyleButton(openFolder, "打开日志文件夹", Accent);
                openFolder.Dock = DockStyle.Bottom;
                openFolder.Click += (s, e) => OpenLogs();
                logBox.Dock = DockStyle.Fill;
                window.Controls.Add(logBox);
                window.Controls.Add(openFolder);
                try { window.ShowDialog(this); }
                finally { window.Controls.Remove(logBox); }
            }
        }

        private Control NumberSetting(string text, NumericUpDown field, int min, int max, int value)
        {
            var grid = Grid(2, SizeType.AutoSize);
            grid.ColumnStyles.Clear();
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.Margin = new Padding(0, 5, 12, 5);
            grid.Controls.Add(CopyLabel(text, Muted, 9f), 0, 0);
            field.Dock = DockStyle.Fill;
            field.Width = 88;
            field.Margin = new Padding(8, 0, 0, 0);
            field.Minimum = min;
            field.Maximum = max;
            field.Value = value;
            field.BackColor = Background;
            field.ForeColor = TextColor;
            grid.Controls.Add(field, 1, 0);
            return grid;
        }

        private void RefreshCatchList()
        {
            if (updatingCatchList) return;
            updatingCatchList = true;
            catchList.BeginUpdate();
            try
            {
                catchList.Items.Clear();
                string query = catchSearch.Text.Trim();
                string category = catchCategory.SelectedItem as string;
                foreach (var entry in catalog)
                {
                    if (query.Length > 0 && entry.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0
                        && !entry.Id.ToString().Contains(query)) continue;
                    if (category == "已选择" && !selectedCatches.Contains(entry.Id)) continue;
                    if ((category == "宝匣" || category == "任务鱼") && entry.Category != category) continue;
                    catchList.Items.Add(entry, selectedCatches.Contains(entry.Id));
                }
            }
            finally
            {
                catchList.EndUpdate();
                updatingCatchList = false;
            }
            UpdateFilterSummary();
        }

        private void UpdateFilterSummary()
        {
            filterSummary.Text = catalog.Length == 0 ? "进入世界后加载鱼获列表，已保存 " + selectedCatches.Count + " 项"
                : "已选 " + selectedCatches.Count + " 项  /  当前显示 " + catchList.Items.Count + " 项"
                    + (sonarFilter.Checked && selectedCatches.Count == 0 ? " · 请选择目标" : "");
            filterSummary.ForeColor = sonarFilter.Checked && selectedCatches.Count == 0 ? Accent : Muted;
        }

        private void SelectVisibleCatches(bool selected)
        {
            foreach (CatchCatalogEntry entry in catchList.Items)
                if (selected) selectedCatches.Add(entry.Id); else selectedCatches.Remove(entry.Id);
            RefreshCatchList();
            SavePreferences();
            ApplySonarFilter();
        }

        private void ApplySonarFilter()
        {
            if (!loadingPreferences && bridge != null && gameRunning)
                bridge.UpdateSonarFilter(sonarFilter.Checked, selectedCatches.OrderBy(id => id).ToArray());
        }

        private BotOptions Options()
        {
            return new BotOptions { RecastDelayMs = (int)recast.Value, TimeoutSeconds = (int)timeout.Value,
                StopBelowHealthPercent = (int)health.Value, SkipEnemies = skipEnemies.Checked,
                MaxConsecutiveTimeouts = (int)maxTimeouts.Value,
                SonarFilterEnabled = sonarFilter.Checked, SelectedCatchIds = selectedCatches.OrderBy(id => id).ToArray(),
                StopWhenInventoryFull = stopFull.Checked };
        }

        private void StartFishing()
        {
            if (bridge == null) return;
            try
            {
                var options = Options();
                if (options.SonarFilterEnabled && options.SelectedCatchIds.Length == 0)
                    throw new ArgumentException("请先勾选至少一种声纳目标，或关闭筛选后开始接管。");
                options.Validate();
                SavePreferences();
                bridge.Start(options);
            }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "请检查挂机设置", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }

        private void StopFishing()
        {
            if (bridge != null) { bridge.Stop(); AppendLog("已请求停止；游戏下一次更新时执行。"); }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312)
            {
                if (message.WParam.ToInt32() == 9) StopFishing();
                if (message.WParam.ToInt32() == 8 && bridge != null && bridge.IsGameForeground())
                {
                    if (bridge.Snapshot().Enabled) StopFishing(); else StartFishing();
                }
            }
            base.WndProc(ref message);
        }

        private void LaunchGame()
        {
            if (gameWasLaunched)
            {
                MessageBox.Show(this, "游戏已结束。请重新打开启动器后再次启动。", Text);
                return;
            }
            if (Process.GetProcessesByName("Terraria").Length > 0)
            {
                MessageBox.Show(this, "检测到已有 Terraria 正在运行。\r\n请先在游戏中保存并退出，再点击启动游戏。", Text);
                return;
            }
            string path = pathBox.Text.Trim().Trim('"');
            if (!File.Exists(path)) { MessageBox.Show(this, "请选择正确的 Terraria.exe 路径。", Text); return; }
            SavePreferences();
            launchButton.Enabled = false;
            pathBox.ReadOnly = true;
            status.Text = "正在加载游戏和钓鱼模块…";
            gameRunning = gameWasLaunched = true;
            var thread = new Thread(() => {
                try
                {
                    GameLoader.Prepare(path);
                    var runtime = GameLoader.CreateRuntime();
                    BeginInvoke((Action)(() => {
                        bridge = runtime;
                        AppendLog("模块已加载。进入世界后，手动抛一次竿，再按 F8。");
                    }));
                    GameLoader.RunGame();
                }
                catch (Exception ex)
                {
                    while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                    string message = ex.ToString();
                    try
                    {
                        Directory.CreateDirectory(preferencesDirectory);
                        File.AppendAllText(Path.Combine(preferencesDirectory, "launcher.log"), DateTime.Now + "\r\n" + message + "\r\n");
                    }
                    catch { }
                    BeginInvoke((Action)(() => {
                        AppendLog("启动失败：" + message);
                        MessageBox.Show(this, "启动失败，详情已显示在日志中。\r\n" + message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
                finally
                {
                    BeginInvoke((Action)(() => {
                        if (bridge != null)
                        {
                            foreach (var entry in bridge.DrainLog()) AppendLog(entry, false);
                            var last = bridge.Snapshot();
                            ObserveStop(last);
                            AppendLog("游戏进程内的运行已结束。最后状态：" + last.Status);
                        }
                        gameRunning = false;
                        startButton.Enabled = stopButton.Enabled = false;
                        status.Text = "游戏已关闭，请重新打开启动器以再次启动";
                        stateBadge.Text = "●  游戏已退出";
                        stateBadge.ForeColor = Muted;
                        RestoreWindow();
                    }));
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = false;
            thread.Start();
        }

        private void RefreshStatus()
        {
            if (bridge == null || !gameRunning) return;
            foreach (var entry in bridge.DrainLog()) AppendLog(entry, false);
            var state = bridge.Snapshot();
            if (catalog.Length == 0)
            {
                var available = bridge.CatchCatalog();
                if (available.Length > 0) { catalog = available; RefreshCatchList(); }
            }
            ObserveStop(state);
            status.Text = state.Status;
            status.ForeColor = state.Enabled ? Accent : TextColor;
            stateBadge.Text = state.Enabled ? "●  自动钓鱼中" : state.InWorld ? "●  等待接管" : "●  等待进入世界";
            stateBadge.ForeColor = state.Enabled ? Accent : Muted;
            startButton.Enabled = state.InWorld && !state.Enabled;
            stopButton.Enabled = state.Enabled;
            telemetry.Text = String.Format("{0}  ♥ {1}/{2}    {3}\r\n鱼饵 {4}    鱼漂 {5}    重抛 {6}    收杆 {7}",
                state.Player, state.Health, state.MaxHealth, state.Rod, state.Bait, state.Bobbers, state.Casts, state.Reels);
            sonarStatus.Text = state.SonarActive
                ? (state.Enabled && state.SonarFilterEnabled ? "声纳生效 · 正在筛选" : "声纳生效 · 未启用筛选")
                : "无声纳效果 · 按普通钓鱼收杆";
            sonarStatus.ForeColor = state.SonarActive ? Accent : Muted;
            DateTime now = DateTime.UtcNow;
            if ((now - previousSample).TotalSeconds >= 1)
            {
                updateRate = (state.UpdateCount - previousUpdates) / (now - previousSample).TotalSeconds;
                previousSample = now;
                previousUpdates = state.UpdateCount;
            }
            bool stale = state.LastUpdateUtc != default(DateTime) && (now - state.LastUpdateUtc).TotalSeconds > 5;
            bool worldNotAdvancing = state.Enabled && updateRate < 1;
            heartbeat.Text = stale
                ? "超过 5 秒未收到游戏更新：当前可能暂停、加载或最小化后停止更新；请恢复游戏窗口检查。"
                : worldNotAdvancing ? "世界暂未推进：请检查游戏是否暂停；恢复窗口后观察更新频率。"
                : String.Format("世界更新约 {0:F0} 次/秒  ·  抛竿方向{1}  ·  收杆次数不等于实际入包数量", updateRate, state.HasAim ? "已记录" : "待记录");
            heartbeat.ForeColor = stale || worldNotAdvancing ? Color.FromArgb(251, 191, 36) : Muted;
            if (state.Enabled && stale && !updateWarningShown)
            {
                updateWarningShown = true;
                AppendLog("警告：超过 5 秒未收到游戏更新，挂机可能已中断；请恢复游戏窗口检查。");
                if (notifyStops.Checked)
                    tray.ShowBalloonTip(5000, "挂机可能已中断", "游戏超过 5 秒没有更新，请恢复游戏窗口检查。", ToolTipIcon.Warning);
            }
            if (!stale || !state.Enabled) updateWarningShown = false;
            var duration = TimeSpan.FromSeconds(state.SessionSeconds);
            sessionInfo.Text = String.Format("本次挂机 {0:00}:{1:00}:{2:00}  ·  超时 {3}/{4}  ·  声纳跳过 {6} 次  ·  {7}  ·  日志{5}",
                (int)duration.TotalHours, duration.Minutes, duration.Seconds, state.ConsecutiveTimeouts,
                state.TotalTimeouts, logErrorShown ? "写入失败" : "已开启", state.SkippedCatches, state.LastCatch);
        }

        private void ObserveStop(BotSnapshot state)
        {
            // A sequence catches an automatic stop even if start and stop happen between UI polls.
            if (state.StopSequence <= lastStopSequence) return;
            lastStopSequence = state.StopSequence;
            if (state.AutomaticStop && notifyStops.Checked)
                tray.ShowBalloonTip(5000, "自动钓鱼已停止", state.StopReason ?? state.Status, ToolTipIcon.Warning);
        }

        private void AppendLog(string text, bool timestamp = true)
        {
            if (logBox.TextLength > 24000) logBox.Text = logBox.Text.Substring(logBox.TextLength - 12000);
            string line = (timestamp ? DateTime.Now.ToString("HH:mm:ss") + "  " : "") + text;
            logBox.AppendText(line + Environment.NewLine);
            if (sessionLog != null)
            {
                bool saved = sessionLog.Append(text);
                if (!saved && !logErrorShown)
                    logBox.AppendText("日志文件写入失败：" + sessionLog.LastError + Environment.NewLine);
                logErrorShown = !saved;
            }
        }

        private void OpenLogs()
        {
            try
            {
                string folder = Path.Combine(preferencesDirectory, "Logs");
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex) { AppendLog("无法打开日志文件夹：" + ex.Message); }
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (gameRunning && !allowClose)
            {
                e.Cancel = true;
                StopFishing();
                Hide();
                tray.ShowBalloonTip(2500, "钓鱼已请求停止", "游戏仍在运行。请先在游戏中保存退出，再关闭启动器。", ToolTipIcon.Info);
                return;
            }
            allowClose = true;
            SavePreferences();
            timer.Stop();
            if (bridge != null)
                foreach (var entry in bridge.DrainLog()) AppendLog(entry, false);
            AppendLog("控制面板已退出。");
            if (f8Registered) UnregisterHotKey(Handle, 8);
            if (f9Registered) UnregisterHotKey(Handle, 9);
            tray.Visible = false;
            tray.Dispose();
        }

        private void SavePreferences()
        {
            try
            {
                Directory.CreateDirectory(preferencesDirectory);
                File.WriteAllLines(Path.Combine(preferencesDirectory, "settings.txt"), new[] {
                    pathBox.Text, recast.Value.ToString(), timeout.Value.ToString(), health.Value.ToString(),
                    skipEnemies.Checked.ToString(), stopFull.Checked.ToString(),
                    maxTimeouts.Value.ToString(), notifyStops.Checked.ToString(),
                    sonarFilter.Checked.ToString(), String.Join(",", selectedCatches.OrderBy(id => id)) });
            }
            catch (Exception ex) { AppendLog("设置未保存：" + ex.Message); }
        }

        private void LoadPreferences()
        {
            try
            {
                string file = Path.Combine(preferencesDirectory, "settings.txt");
                if (!File.Exists(file)) return;
                var values = File.ReadAllLines(file);
                if (values.Length < 6) return;
                pathBox.Text = values[0];
                NumericUpDown[] fields = { recast, timeout, health };
                for (int i = 0; i < fields.Length; i++)
                {
                    decimal value;
                    if (Decimal.TryParse(values[i + 1], out value))
                        fields[i].Value = Math.Min(fields[i].Maximum, Math.Max(fields[i].Minimum, value));
                }
                bool setting;
                if (Boolean.TryParse(values[4], out setting)) skipEnemies.Checked = setting;
                if (Boolean.TryParse(values[5], out setting)) stopFull.Checked = setting;
                decimal limit;
                if (values.Length > 6 && Decimal.TryParse(values[6], out limit))
                    maxTimeouts.Value = Math.Min(maxTimeouts.Maximum, Math.Max(maxTimeouts.Minimum, limit));
                if (values.Length > 7 && Boolean.TryParse(values[7], out setting)) notifyStops.Checked = setting;
                if (values.Length > 8 && Boolean.TryParse(values[8], out setting)) sonarFilter.Checked = setting;
                if (values.Length > 9)
                    foreach (string token in values[9].Split(','))
                    {
                        int id;
                        if (Int32.TryParse(token, out id) && id > 0) selectedCatches.Add(id);
                    }
            }
            catch { }
        }

        private static string FindGame()
        {
            var roots = new List<string> {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam") };
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string steam = key == null ? null : key.GetValue("SteamPath") as string;
                    if (!String.IsNullOrWhiteSpace(steam)) roots.Insert(0, steam);
                }
                foreach (var root in roots.ToArray())
                {
                    string libraries = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                    if (!File.Exists(libraries)) continue;
                    foreach (Match match in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\""))
                        roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                }
            }
            catch { }
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = Path.Combine(root, "steamapps", "common", "Terraria", "Terraria.exe");
                if (File.Exists(path)) return path;
            }
            return "";
        }
    }
}
