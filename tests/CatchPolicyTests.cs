using System;
using TerrariaAutoFish;

internal static class CatchPolicyTests
{
    private static int checks;
    private static void Assert(bool value, string name)
    {
        checks++;
        if (!value) throw new Exception("FAIL: " + name);
    }

    private static void Main()
    {
        var settings = new BotOptions { SonarFilterEnabled = true, SelectedCatchIds = new[] { 2308, 2334 } };
        Assert(CatchPolicy.CanReel(new[] { 2308 }, true, settings), "selected catch reels with sonar");
        Assert(!CatchPolicy.CanReel(new[] { 2290 }, true, settings), "unselected catch waits");
        Assert(CatchPolicy.CanReel(new[] { 2290 }, false, settings), "expired sonar falls back to ordinary fishing");
        Assert(CatchPolicy.CanReel(new[] { 2308, 2334 }, true, settings), "multiple selected bites reel together");
        Assert(!CatchPolicy.CanReel(new[] { 2308, 2290 }, true, settings), "selected bite cannot pull unselected second bobber");
        Assert(!CatchPolicy.CanReel(new[] { 2308, -620 }, true, settings), "monster blocks all bobbers");
        settings.SkipEnemies = false;
        Assert(!CatchPolicy.CanReel(new[] { 2308, -620 }, true, settings), "fish allowlist still rejects monsters when enemy skip disabled");
        Assert(CatchPolicy.CanReel(new[] { -620 }, false, settings), "ordinary fishing respects disabled monster protection");
        settings.SkipEnemies = true;
        Assert(!CatchPolicy.CanReel(new[] { -620 }, false, settings), "ordinary fishing respects monster protection");
        settings.SonarFilterEnabled = false;
        Assert(CatchPolicy.CanReel(new[] { 2290 }, true, settings), "disabled filter allows other fish even with sonar");
        Assert(!CatchPolicy.CanReel(new int[0], true, settings), "no bite cannot reel");
        Assert(!CatchPolicy.CanReel(new[] { 0 }, true, settings), "unknown empty catch cannot reel");
        settings.SonarFilterEnabled = true;
        var copy = settings.Copy();
        settings.SelectedCatchIds[0] = 2290;
        Assert(CatchPolicy.CanReel(new[] { 2308 }, true, copy), "session targets are isolated from UI edits");
        Assert(!CatchPolicy.CanReel(new[] { 2290 }, true, copy), "UI edits cannot change active session targets");
        bool invalid = false;
        try { new BotOptions { SonarFilterEnabled = true }.Validate(); }
        catch (ArgumentException) { invalid = true; }
        Assert(invalid, "empty enabled allowlist rejected");
        new BotOptions { SonarFilterEnabled = false }.Validate();
        Console.WriteLine("PASS: " + checks + " sonar policy checks.");
    }
}
