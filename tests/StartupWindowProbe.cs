using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
}
