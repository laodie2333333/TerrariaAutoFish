using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
                        + "PASS: Main static initializer completed; all three fishing patches verified.\r\n"
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

    internal sealed class ControlPanel : Form
    {
        private static readonly Color Background = Color.FromArgb(17, 24, 39);
        private static readonly Color Card = Color.FromArgb(30, 41, 59);
        private static readonly Color TextColor = Color.FromArgb(226, 232, 240);
        private static readonly Color Muted = Color.FromArgb(148, 163, 184);
        private static readonly Color Accent = Color.FromArgb(45, 212, 191);
        private readonly TextBox pathBox = new TextBox();
        private readonly Button launchButton = new Button();
        private readonly Button startButton = new Button();
        private readonly Button stopButton = new Button();
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

        public ControlPanel()
        {
            Text = "泰拉瑞亚后台钓鱼 · 1.4.5.8 · 工具 0.2.0";
            ClientSize = new Size(920, 826);
            MinimumSize = new Size(936, 865);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Background;
            ForeColor = TextColor;
            Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            using (var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TerrariaAutoFish.GoldenFishingRod.ico"))
            using (var appIcon = new Icon(iconStream))
                Icon = (Icon)appIcon.Clone();
            BuildLayout();
            LoadPreferences();
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
            Shown += (s, e) => {
                f8Registered = RegisterHotKey(Handle, 8, 0x4000, (uint)Keys.F8);
                f9Registered = RegisterHotKey(Handle, 9, 0x4000, (uint)Keys.F9);
                AppendLog("仅支持原版 Windows / Steam Terraria 1.4.5.8。先保存退出当前游戏，再从这里启动。");
                if (!f8Registered) AppendLog("F8 注册失败（可能已被其他工具占用），请使用开始按钮。");
                if (!f9Registered) AppendLog("F9 注册失败（可能已被其他工具占用），请使用停止按钮或托盘菜单。");
            };
        }

        private Label LabelAt(Control parent, string text, int x, int y, int width, int height,
            Color color, float size = 10f, FontStyle style = FontStyle.Regular)
        {
            var label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height),
                ForeColor = color, BackColor = Color.Transparent, Font = new Font(Font.FontFamily, size, style) };
            parent.Controls.Add(label);
            return label;
        }

        private void StyleButton(Button button, string text, Color background, int x, int y, int width)
        {
            button.Text = text;
            button.SetBounds(x, y, width, 38);
            button.BackColor = background;
            button.ForeColor = background == Accent ? Background : TextColor;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;
            button.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        }

        private Panel MakeCard(int x, int y, int width, int height)
        {
            var panel = new Panel { Location = new Point(x, y), Size = new Size(width, height), BackColor = Card };
            Controls.Add(panel);
            return panel;
        }

        private void BuildLayout()
        {
            LabelAt(this, "泰拉瑞亚 · 后台钓鱼", 26, 20, 610, 42, TextColor, 23f, FontStyle.Bold);
            LabelAt(this, "1.4.5.8 专用  /  游戏内执行  /  不占用系统鼠标", 29, 66, 800, 25, Muted);
            var gameCard = MakeCard(26, 106, 868, 100);
            LabelAt(gameCard, "游戏启动", 16, 12, 120, 25, TextColor, 11f, FontStyle.Bold);
            pathBox.SetBounds(16, 48, 574, 31);
            pathBox.BackColor = Background;
            pathBox.ForeColor = TextColor;
            pathBox.BorderStyle = BorderStyle.FixedSingle;
            gameCard.Controls.Add(pathBox);
            var browse = new Button();
            StyleButton(browse, "选择…", Color.FromArgb(51, 65, 85), 600, 43, 94);
            browse.Click += (s, e) => {
                using (var dialog = new OpenFileDialog { Filter = "Terraria.exe|Terraria.exe", Title = "选择原版 Terraria.exe" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) pathBox.Text = dialog.FileName;
            };
            gameCard.Controls.Add(browse);
            StyleButton(launchButton, "启动游戏", Accent, 706, 43, 146);
            launchButton.Click += (s, e) => LaunchGame();
            gameCard.Controls.Add(launchButton);

            var helpCard = MakeCard(26, 222, 402, 254);
            LabelAt(helpCard, "使用方法", 16, 14, 350, 26, TextColor, 11f, FontStyle.Bold);
            LabelAt(helpCard, "1   从上方启动游戏，进入世界。\r\n2   拿起钓竿，手动向水面抛一次竿。\r\n3   在游戏中按 F8，接管自动钓鱼。\r\n4   切到其他窗口；按 F9 随时停止。", 16, 53, 376, 116, TextColor);
            LabelAt(helpCard, "最小化面板后可从系统托盘打开。", 16, 174, 376, 24, Muted, 9f);
            LabelAt(helpCard, "关闭面板会停止钓鱼；最小化则继续。", 16, 203, 376, 26, Muted, 9f);

            var settingsCard = MakeCard(444, 222, 450, 254);
            LabelAt(settingsCard, "挂机设置（下次开始时应用）", 16, 14, 420, 26, TextColor, 11f, FontStyle.Bold);
            AddNumber(settingsCard, "重抛间隔 / 毫秒", recast, 300, 5000, 800, 50);
            AddNumber(settingsCard, "无咬钩超时 / 秒", timeout, 30, 1800, 180, 86);
            AddNumber(settingsCard, "低生命停止 / %", health, 0, 90, 25, 122);
            AddNumber(settingsCard, "连续超时停止 / 次", maxTimeouts, 1, 20, 3, 158);
            skipEnemies.Text = "跳过怪物咬钩";
            skipEnemies.Checked = true;
            skipEnemies.SetBounds(16, 198, 192, 26);
            stopFull.Text = "背包无空格时停止";
            stopFull.Checked = true;
            stopFull.SetBounds(216, 198, 225, 26);
            settingsCard.Controls.AddRange(new Control[] { skipEnemies, stopFull });

            var stateCard = MakeCard(26, 492, 868, 145);
            status.SetBounds(16, 13, 836, 27);
            status.Text = "等待从启动器进入游戏";
            status.ForeColor = Accent;
            status.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            stateCard.Controls.Add(status);
            telemetry.SetBounds(16, 49, 830, 23);
            telemetry.ForeColor = TextColor;
            telemetry.Text = "角色 —     钓竿 —     鱼饵 —     重抛 0     咬钩收杆 0";
            stateCard.Controls.Add(telemetry);
            heartbeat.SetBounds(16, 80, 830, 22);
            heartbeat.ForeColor = Muted;
            heartbeat.Font = new Font(Font.FontFamily, 9f);
            heartbeat.Text = "收杆次数是自动收杆操作数，实际入包物品以游戏为准。";
            stateCard.Controls.Add(heartbeat);
            sessionInfo.SetBounds(16, 111, 836, 25);
            sessionInfo.ForeColor = Muted;
            sessionInfo.Font = new Font(Font.FontFamily, 9f);
            sessionInfo.Text = "本次挂机 00:00:00  ·  连续超时 0 次  ·  日志已开启";
            stateCard.Controls.Add(sessionInfo);

            StyleButton(startButton, "开始接管 · F8", Accent, 26, 653, 196);
            StyleButton(stopButton, "停止钓鱼 · F9", Color.FromArgb(127, 29, 29), 234, 653, 196);
            startButton.Enabled = stopButton.Enabled = false;
            startButton.Click += (s, e) => StartFishing();
            stopButton.Click += (s, e) => StopFishing();
            Controls.AddRange(new Control[] { startButton, stopButton });
            notifyStops.Text = "自动停止时通知";
            notifyStops.Checked = true;
            notifyStops.SetBounds(448, 659, 210, 28);
            notifyStops.CheckedChanged += (s, e) => SavePreferences();
            Controls.Add(notifyStops);
            var logsButton = new Button();
            StyleButton(logsButton, "打开日志文件夹", Color.FromArgb(51, 65, 85), 674, 653, 220);
            logsButton.Click += (s, e) => OpenLogs();
            Controls.Add(logsButton);

            logBox.SetBounds(26, 706, 868, 94);
            logBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BackColor = Color.FromArgb(10, 17, 29);
            logBox.ForeColor = Muted;
            logBox.BorderStyle = BorderStyle.None;
            logBox.Font = new Font("Microsoft YaHei UI", 9f);
            Controls.Add(logBox);
        }

        private void AddNumber(Control parent, string text, NumericUpDown field, int min, int max, int value, int y)
        {
            LabelAt(parent, text, 16, y + 3, 250, 25, Muted);
            field.SetBounds(296, y, 132, 29);
            field.Minimum = min;
            field.Maximum = max;
            field.Value = value;
            field.BackColor = Background;
            field.ForeColor = TextColor;
            parent.Controls.Add(field);
        }

        private BotOptions Options()
        {
            return new BotOptions { RecastDelayMs = (int)recast.Value, TimeoutSeconds = (int)timeout.Value,
                StopBelowHealthPercent = (int)health.Value, SkipEnemies = skipEnemies.Checked,
                MaxConsecutiveTimeouts = (int)maxTimeouts.Value,
                StopWhenInventoryFull = stopFull.Checked };
        }

        private void StartFishing()
        {
            if (bridge == null) return;
            SavePreferences();
            bridge.Start(Options());
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
            ObserveStop(state);
            status.Text = state.Status;
            status.ForeColor = state.Enabled ? Accent : TextColor;
            startButton.Enabled = state.InWorld && !state.Enabled;
            stopButton.Enabled = state.Enabled;
            telemetry.Text = String.Format("{0}  ♥ {1}/{2}    {3}    鱼饵 {4}    鱼漂 {5}    重抛 {6}    咬钩收杆 {7}",
                state.Player, state.Health, state.MaxHealth, state.Rod, state.Bait, state.Bobbers, state.Casts, state.Reels);
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
            sessionInfo.Text = String.Format("本次挂机 {0:00}:{1:00}:{2:00}  ·  连续超时 {3} 次 / 累计 {4} 次  ·  日志{5}",
                (int)duration.TotalHours, duration.Minutes, duration.Seconds, state.ConsecutiveTimeouts,
                state.TotalTimeouts, logErrorShown ? "写入失败" : "已开启");
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
                    maxTimeouts.Value.ToString(), notifyStops.Checked.ToString() });
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
