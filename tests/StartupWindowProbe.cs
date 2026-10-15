using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TerrariaAutoFish;

// Opt-in integration probe: opens the real menu with an isolated save directory, then exits.
internal static class StartupWindowProbe
{
    private static string report;
    private static bool menuDrawn;
    private static int menuUpdates;
    private static IBotBridge bridge;
    private static readonly Stopwatch elapsed = Stopwatch.StartNew();
    private static int runtimeChecks;

    [STAThread]
    private static int Main(string[] args)
    {
        report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window-check.txt");
        try
        {
            var game = GameLoader.Prepare(args[0]);
            bridge = GameLoader.CreateRuntime();
            InstallObserver();
            var isolatedSaveDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window-check-saves");
            Directory.CreateDirectory(isolatedSaveDirectory);
            if (args.Length > 1 && File.Exists(args[1]))
                File.Copy(args[1], Path.Combine(isolatedSaveDirectory, "config.json"), true);
            File.WriteAllText(report, "Starting real game window with isolated saves.\r\n");
            game.EntryPoint.Invoke(null, new object[] { new[] { "-savedirectory", isolatedSaveDirectory } });
            if (!menuDrawn || menuUpdates < 30)
                throw new Exception("Game returned before the menu completed 30 updates.");
            if (bridge.Snapshot().LastUpdateUtc == default(DateTime))
                throw new Exception("Fishing update patch did not publish a snapshot.");
            File.AppendAllText(report, "PASS: Game returned normally after menu test.\r\n");
            return 0;
        }
        catch (Exception ex)
        {
            File.AppendAllText(report, ex + "\r\n");
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InstallObserver()
    {
        new Harmony("local.terraria.startup.window.probe").Patch(
            AccessTools.DeclaredMethod(typeof(Terraria.Program), "InitializeConsoleOutput"),
            postfix: new HarmonyMethod(typeof(StartupWindowProbe), "AfterSavePath"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AfterSavePath()
    {
        Terraria.Main.OnEngineLoad += () => {
            menuDrawn = true;
            File.AppendAllText(report, "PASS: Menu draw event reached.\r\n");
        };
        new Harmony("local.terraria.startup.window.probe").Patch(
            AccessTools.DeclaredMethod(typeof(Terraria.Main), "Update"),
            postfix: new HarmonyMethod(typeof(StartupWindowProbe), "AfterUpdate"));
    }

    private static void AfterUpdate()
    {
        if (menuDrawn && ++menuUpdates == 30)
        {
            // Verify the installed game's catalog after content/localization initialization.
            bridge.GetType().GetMethod("LoadCatchCatalog", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bridge, null);
            var catches = bridge.CatchCatalog();
            if (catches.Length < 50 || catches.Any(entry => entry.Id <= 0 || String.IsNullOrWhiteSpace(entry.Name))
                || catches.Select(entry => entry.Id).Distinct().Count() != catches.Length
                || !catches.Any(entry => entry.Category == "宝匣") || !catches.Any(entry => entry.Category == "任务鱼"))
                throw new Exception("Fishing catalog did not contain unique named fish, crates and quest fish.");
            File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catch-catalog.txt"),
                catches.Select(entry => entry.Id + "\t" + entry.Category + "\t" + entry.Name));
            File.AppendAllText(report, "PASS: Loaded " + catches.Length + " named fishing targets, including crates and quest fish.\r\n");
            RunRuntimeRegressions();
            File.AppendAllText(report, "PASS: Menu completed 30 updates; ShowSplash="
                + Terraria.Main.showSplash + "; elapsed=" + elapsed.Elapsed.TotalSeconds.ToString("F1") + "s.\r\n");
            Terraria.Main.WeGameRequireExitGame();
        }
        if (elapsed.Elapsed.TotalSeconds > 120)
        {
            File.AppendAllText(report, "FAIL: Menu startup timed out.\r\n");
            Terraria.Main.WeGameRequireExitGame();
        }
    }

    private static void RuntimeAssert(bool value, string name)
    {
        runtimeChecks++;
        if (!value) throw new Exception("Runtime regression: " + name);
    }

    private static FieldInfo RuntimeField(string name)
    {
        return bridge.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    }

    private static void PumpRuntime()
    {
        bridge.GetType().GetMethod("BeforeUpdate", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Terraria.Main.instance });
    }

    private static bool ObserveItemCheck(Terraria.Player player)
    {
        // Run the real automation hook against synthetic bobbers, without executing game loot
        // creation. Verify that its virtual input scope is restored exactly as in the postfix.
        int x = Terraria.Main.mouseX, y = Terraria.Main.mouseY;
        int inputX = Terraria.GameInput.PlayerInput.MouseX, inputY = Terraria.GameInput.PlayerInput.MouseY;
        player.controlUseItem = false;
        player.delayUseItem = true;
        player.mouseInterface = true;
        var mouseItem = Terraria.Main.mouseItem;
        var args = new object[] { player, null };
        bridge.GetType().GetMethod("BeforeItemCheck", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        bool use = player.controlUseItem;
        RuntimeAssert(!player.mouseInterface && !player.delayUseItem, "inventory hover does not block virtual fishing input");
        RuntimeAssert(Terraria.Main.playerInventory && Object.ReferenceEquals(mouseItem, Terraria.Main.mouseItem),
            "automation leaves the open inventory and cursor item untouched");
        bridge.GetType().GetMethod("AfterItemCheck", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        RuntimeAssert(Terraria.Main.mouseX == x && Terraria.Main.mouseY == y
            && Terraria.GameInput.PlayerInput.MouseX == inputX && Terraria.GameInput.PlayerInput.MouseY == inputY
            && !player.controlUseItem && player.delayUseItem && player.mouseInterface,
            "virtual mouse and player input restore after ItemCheck");
        return use;
    }

    private static void RunRuntimeRegressions()
    {
        // No real world is loaded or updated. All globals are restored before the menu resumes.
        bool menu = Terraria.Main.gameMenu, inventory = Terraria.Main.playerInventory;
        bool autoPause = Terraria.Main.autoPause, optionsWindow = Terraria.Main.ingameOptionsWindow;
        bool chat = Terraria.Main.drawingPlayerChat, map = Terraria.Main.mapFullscreen;
        bool playUnfocused = Terraria.Main.SettingPlayWhenUnfocused;
        int netMode = Terraria.Main.netMode;
        var originalPlayer = Terraria.Main.player[Terraria.Main.myPlayer];
        var originalProjectiles = Terraria.Main.projectile;
        var pauseMethod = AccessTools.DeclaredMethod(typeof(Terraria.Main), "CanPauseGame");
        var synthetic = new Terraria.Player { active = true, whoAmI = Terraria.Main.myPlayer,
            name = "Runtime probe", statLife = 100, statLifeMax2 = 100, sonarPotion = true,
            chest = -1, sign = -1 };
        synthetic.inventory[synthetic.selectedItem].SetDefaults(2294);
        synthetic.inventory[54].SetDefaults(2676);
        synthetic.inventory[54].stack = 99;
        synthetic.position = new Microsoft.Xna.Framework.Vector2(800, 800);
        var settings = new BotOptions { StopWhenInventoryFull = false, StopBelowHealthPercent = 0,
            SonarFilterEnabled = true, SelectedCatchIds = new[] { 2308 } };
        try
        {
            Terraria.Main.player[Terraria.Main.myPlayer] = synthetic;
            Terraria.Main.projectile = Enumerable.Range(0, 1001).Select(i => new Terraria.Projectile { whoAmI = i }).ToArray();
            Terraria.Main.gameMenu = false;
            Terraria.Main.netMode = 0;
            Terraria.Main.playerInventory = true;
            Terraria.Main.autoPause = true;
            Terraria.Main.ingameOptionsWindow = Terraria.Main.drawingPlayerChat = Terraria.Main.mapFullscreen = false;
            RuntimeAssert((bool)pauseMethod.Invoke(null, null), "vanilla inventory auto-pause works before fishing starts");
            RuntimeField("hasAim").SetValue(bridge, true);
            RuntimeField("aimRod").SetValue(bridge, 2294);
            RuntimeField("aim").SetValue(bridge, new Microsoft.Xna.Framework.Vector2(180, 0));
            bridge.Start(settings);
            PumpRuntime();
            RuntimeAssert(bridge.Snapshot().Enabled, "fishing starts with the inventory open");
            RuntimeAssert(!(bool)pauseMethod.Invoke(null, null) && Terraria.Main.autoPause,
                "backpack no longer pauses active fishing while AutoPause preference stays enabled");
            var safetyMethod = bridge.GetType().GetMethod("CheckSafety", BindingFlags.Instance | BindingFlags.NonPublic);
            synthetic.chest = 0;
            RuntimeAssert(safetyMethod.Invoke(bridge, new object[] { synthetic, settings, true }) != null,
                "chest access remains a safety stop while ordinary inventory is allowed");
            synthetic.chest = -2;
            RuntimeAssert(safetyMethod.Invoke(bridge, new object[] { synthetic, settings, true }) != null,
                "personal bank access remains a safety stop");
            synthetic.chest = -1;
            typeof(Terraria.Player).GetProperty("talkNPC").GetSetMethod(true).Invoke(synthetic, new object[] { 0 });
            RuntimeAssert(safetyMethod.Invoke(bridge, new object[] { synthetic, settings, true }) != null,
                "NPC and shop interaction remains a safety stop");
            typeof(Terraria.Player).GetProperty("talkNPC").GetSetMethod(true).Invoke(synthetic, new object[] { -1 });
            Terraria.Main.ingameOptionsWindow = true;
            RuntimeAssert((bool)pauseMethod.Invoke(null, null), "options menu retains vanilla pause");
            Terraria.Main.ingameOptionsWindow = false;
            synthetic.sign = 0;
            RuntimeAssert((bool)pauseMethod.Invoke(null, null), "sign interaction retains vanilla pause");
            RuntimeAssert(safetyMethod.Invoke(bridge, new object[] { synthetic, settings, true }) != null,
                "reading a sign remains a safety stop");
            synthetic.sign = -1;
            var engine = RuntimeField("engine").GetValue(bridge);
            var engineType = engine.GetType();
            var nextAction = engineType.GetField("nextAction", BindingFlags.Instance | BindingFlags.NonPublic);
            var awaitingReturn = engineType.GetField("awaitingReturn", BindingFlags.Instance | BindingFlags.NonPublic);
            nextAction.SetValue(engine, 0L);
            var bobber = Terraria.Main.projectile[0];
            bobber.active = bobber.bobber = true;
            bobber.owner = synthetic.whoAmI;
            bobber.ai[1] = -100;
            bobber.localAI[1] = 2308;
            RuntimeAssert(ObserveItemCheck(synthetic), "initial selected catch requests a reel");
            long sessionStarted = (long)RuntimeField("sessionStarted").GetValue(bridge);
            int reels = (int)engineType.GetProperty("Reels").GetValue(engine, null);
            var replacement = new[] { 2334 };
            bridge.UpdateSonarFilter(true, replacement);
            replacement[0] = 2308;
            RuntimeAssert(((BotOptions)RuntimeField("options").GetValue(bridge)).SelectedCatchIds[0] == 2308,
                "UI changes wait for the game-thread command queue");
            PumpRuntime();
            var applied = (BotOptions)RuntimeField("options").GetValue(bridge);
            RuntimeAssert(applied.SelectedCatchIds.SequenceEqual(new[] { 2334 }), "live filter update owns its captured target array");
            RuntimeAssert((long)RuntimeField("sessionStarted").GetValue(bridge) == sessionStarted
                && (int)engineType.GetProperty("Reels").GetValue(engine, null) == reels
                && bridge.Snapshot().Enabled, "target changes preserve the current session and counters");
            awaitingReturn.SetValue(engine, false);
            nextAction.SetValue(engine, 0L);
            RuntimeAssert(!ObserveItemCheck(synthetic), "old selection stops reeling after live target change");
            bobber.localAI[1] = 2334;
            RuntimeAssert(ObserveItemCheck(synthetic), "new selection starts reeling without restarting fishing");
            bridge.UpdateSonarFilter(true, new int[0]);
            PumpRuntime();
            awaitingReturn.SetValue(engine, false);
            nextAction.SetValue(engine, 0L);
            RuntimeAssert(!ObserveItemCheck(synthetic) && bridge.Snapshot().Enabled,
                "clearing all targets keeps the session enabled and rejects the pending sonar bite");
            bridge.UpdateSonarFilter(false, new int[0]);
            PumpRuntime();
            RuntimeAssert(ObserveItemCheck(synthetic), "turning the filter off resumes ordinary fishing immediately");
            bridge.Stop();
            PumpRuntime();
            RuntimeAssert(!bridge.Snapshot().Enabled && (bool)pauseMethod.Invoke(null, null) && Terraria.Main.autoPause,
                "manual stop restores normal backpack pause without changing the preference");
            RuntimeField("hasAim").SetValue(bridge, true);
            bridge.Start(settings);
            PumpRuntime();
            Terraria.Main.drawingPlayerChat = true;
            PumpRuntime();
            RuntimeAssert(!bridge.Snapshot().Enabled && bridge.Snapshot().Status.Contains("聊天"),
                "chat interaction still stops automation");
            Terraria.Main.drawingPlayerChat = false;
            RunInventoryRegressions(synthetic);
            File.AppendAllText(report, "PASS: " + runtimeChecks
                + " isolated runtime checks: live filters, inventory protection, inventory pause and scoped input restoration.\r\n");
        }
        finally
        {
            bridge.Stop();
            PumpRuntime();
            Terraria.Main.player[Terraria.Main.myPlayer] = originalPlayer;
            Terraria.Main.projectile = originalProjectiles;
            Terraria.Main.gameMenu = menu;
            Terraria.Main.netMode = netMode;
            Terraria.Main.playerInventory = inventory;
            Terraria.Main.autoPause = autoPause;
            Terraria.Main.ingameOptionsWindow = optionsWindow;
            Terraria.Main.drawingPlayerChat = chat;
            Terraria.Main.mapFullscreen = map;
            Terraria.Main.SettingPlayWhenUnfocused = playUnfocused;
            RuntimeField("hasAim").SetValue(bridge, false);
        }
    }

    private static void RunInventoryRegressions(Terraria.Player player)
    {
        var settings = new BotOptions { StopWhenInventoryFull = true, StopBelowHealthPercent = 0 };
        var safety = bridge.GetType().GetMethod("CheckSafety", BindingFlags.Instance | BindingFlags.NonPublic);
        var engine = RuntimeField("engine").GetValue(bridge);
        var engineType = engine.GetType();
        var started = RuntimeField("sessionStarted");
        for (int i = 0; i < 50; i++)
            if (i != player.selectedItem) player.inventory[i].SetDefaults(2);
        for (int i = 50; i < 54; i++) player.inventory[i].TurnToAir();
        RuntimeAssert(Enumerable.Range(0, 50).All(i => !player.inventory[i].IsAir),
            "inventory fixture fills all fifty slots including the hotbar");
        RuntimeAssert(safety.Invoke(bridge, new object[] { player, settings, false }) != null,
            "full main inventory is rejected during Begin safety checks");
        RuntimeAssert(safety.Invoke(bridge, new object[] { player, settings, true }) != null,
            "empty coin and ammo slots do not count as main inventory space");
        RuntimeField("hasAim").SetValue(bridge, true);
        bridge.Start(settings);
        PumpRuntime();
        RuntimeAssert(!bridge.Snapshot().Enabled && bridge.Snapshot().Status.Contains("主背包"),
            "starting with a full inventory refuses fishing and publishes the reason");

        player.inventory[49].TurnToAir();
        bridge.Start(settings);
        PumpRuntime();
        RuntimeAssert(bridge.Snapshot().Enabled, "forty-nine occupied main slots allow fishing");
        player.inventory[49].SetDefaults(2);
        RuntimeAssert(safety.Invoke(bridge, new object[] { player, settings, true }) != null,
            "the last slot filling is observed immediately without a one-second cache");
        int reels = (int)engineType.GetProperty("Reels").GetValue(engine, null);
        int casts = (int)engineType.GetProperty("Casts").GetValue(engine, null);
        var bobber = Terraria.Main.projectile[0];
        bobber.ai[0] = 0;
        bobber.ai[1] = -100;
        bobber.localAI[1] = 2308;
        engineType.GetField("nextAction", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(engine, 0L);
        var args = new object[] { player, null };
        bridge.GetType().GetMethod("BeforeItemCheck", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        RuntimeAssert(!bridge.Snapshot().Enabled && args[1] == null
            && (int)engineType.GetProperty("Reels").GetValue(engine, null) == reels
            && (int)engineType.GetProperty("Casts").GetValue(engine, null) == casts,
            "ItemCheck stops on a newly full inventory before injecting a reel or cast");
        RuntimeAssert(bridge.Snapshot().AutomaticStop && bridge.Snapshot().StopReason.Contains("主背包"),
            "full inventory emits the normal automatic stop reason");
        player.inventory[49].TurnToAir();
        long stoppedSequence = bridge.Snapshot().StopSequence;
        PumpRuntime();
        RuntimeAssert(!bridge.Snapshot().Enabled && bridge.Snapshot().StopSequence == stoppedSequence,
            "freeing inventory space does not automatically restart fishing");

        bridge.Start(settings);
        PumpRuntime();
        RuntimeAssert(bridge.Snapshot().Enabled, "manual restart succeeds with a free slot");
        player.inventory[49].SetDefaults(2);
        PumpRuntime();
        RuntimeAssert(!bridge.Snapshot().Enabled && bridge.Snapshot().StopReason.Contains("主背包"),
            "BeforeUpdate stops as soon as the last slot fills");
        player.inventory[9].TurnToAir();
        bridge.Start(settings);
        PumpRuntime();
        RuntimeAssert(bridge.Snapshot().Enabled, "an empty hotbar slot counts as main inventory space");
        long originalStarted = (long)started.GetValue(bridge);
        player.inventory[9].SetDefaults(2);
        bridge.UpdateInventoryFullStop(false);
        PumpRuntime();
        RuntimeAssert(bridge.Snapshot().Enabled
            && !((BotOptions)RuntimeField("options").GetValue(bridge)).StopWhenInventoryFull,
            "disabling protection while fishing takes effect before the same update safety check");
        RuntimeAssert((long)started.GetValue(bridge) == originalStarted
            && bridge.Snapshot().Reels == 0 && bridge.Snapshot().Casts == 0,
            "changing inventory protection preserves session timing and counters");
        bridge.UpdateInventoryFullStop(true);
        PumpRuntime();
        RuntimeAssert(!bridge.Snapshot().Enabled && bridge.Snapshot().StopReason.Contains("主背包"),
            "enabling protection while full stops within the same game update");
        bridge.UpdateInventoryFullStop(false);
        PumpRuntime();
        RuntimeAssert(!bridge.Snapshot().Enabled, "turning off protection after a stop does not restart fishing");

        player.inventory[49].TurnToAir();
        bridge.Start(new BotOptions { StopWhenInventoryFull = false, StopBelowHealthPercent = 0 });
        PumpRuntime();
        originalStarted = (long)started.GetValue(bridge);
        bridge.UpdateInventoryFullStop(true);
        PumpRuntime();
        RuntimeAssert(bridge.Snapshot().Enabled && (long)started.GetValue(bridge) == originalStarted,
            "enabling protection with room left keeps the current session running");
        bridge.Stop();
        PumpRuntime();
    }
}
