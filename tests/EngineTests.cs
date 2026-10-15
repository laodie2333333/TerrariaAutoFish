using System;
using TerrariaAutoFish;

internal static class EngineTests
{
    private static int checks;
    private static void Assert(bool condition, string name)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + name);
    }

    private static FishingObservation Observe(int count, bool ready = false, bool waiting = true)
    {
        return new FishingObservation { CanUse = true, Bobbers = count, CatchReady = ready, WaitingBobber = waiting };
    }

    private static void Main()
    {
        var engine = new FishingEngine();
        var options = new BotOptions();
        engine.Start(options, 0);
        Assert(engine.Tick(Observe(1), 1000) == FishingAction.None, "existing bobber is not reeled before bite");
        Assert(engine.Tick(Observe(1, true), 2000) == FishingAction.Reel, "bite reels normally");
        Assert(engine.Tick(Observe(1, true), 2016) == FishingAction.None, "same bite does not double trigger");
        Assert(engine.Tick(Observe(1, false, false), 3000) == FishingAction.None, "returning bobber blocks recast");
        Assert(engine.Tick(Observe(0), 3100) == FishingAction.Cast, "recast after bobber disappears");
        Assert(engine.Tick(Observe(1), 4000) == FishingAction.None, "new deployment acknowledged");
        Assert(engine.Reels == 1 && engine.Casts == 1, "operation counters are separate");

        engine.Start(options, 0);
        engine.Tick(Observe(1), 200);
        Assert(engine.Tick(Observe(1), 180199) == FishingAction.None, "no premature timeout");
        Assert(engine.Tick(Observe(1), 180201) == FishingAction.ResetLine, "monotonic timeout resets line");
        Assert(engine.Reels == 0, "timeout does not count as caught bite");
        Assert(engine.Tick(Observe(1, false, false), 400000) == FishingAction.Stop, "stuck returning bobber stops rather than reeling repeatedly");

        engine.Start(options, 0);
        engine.Tick(Observe(1), 200);
        var skippedBite = Observe(1);
        skippedBite.BiteActive = true;
        Assert(engine.Tick(skippedBite, 181000) == FishingAction.None, "timeout does not reel a skipped monster bite");
        Assert(engine.Tick(Observe(1), 182000) == FishingAction.None, "skipping a bite renews the wait without a forced reel");
        Assert(engine.Tick(Observe(1), 361000) == FishingAction.ResetLine, "a full idle timeout still recovers after skipped bite ends");

        engine.Start(options, 0);
        engine.Tick(Observe(1), 200);
        var filtered = Observe(1);
        filtered.BiteActive = true;
        filtered.FilterStatus = "filtered";
        for (int i = 1; i < 10; i++)
            Assert(engine.Tick(filtered, 180000L * i) == FishingAction.None, "rejected sonar bites never force timeout reels");
        Assert(engine.ConsecutiveTimeouts == 0 && engine.Reels == 0 && engine.Status == "filtered", "deliberate skips are not failed waits or catches");
        Assert(engine.Tick(Observe(1, true), 1621000) == FishingAction.Reel, "an allowed bite is caught after deliberate skips");

        engine.Start(options, 0);
        for (int i = 0; i < 5; i++) engine.Tick(Observe(0), 1000 + 2000 * i);
        Assert(engine.Tick(Observe(0), 11000) == FishingAction.Stop, "repeated failed casts stop safely");
        Assert(!engine.Enabled, "failed casting disables engine");

        engine.Start(options, 0);
        var unsafeObservation = Observe(1, true);
        unsafeObservation.Safe = false;
        unsafeObservation.UnsafeReason = "dead";
        Assert(engine.Tick(unsafeObservation, 1000) == FishingAction.Stop && engine.Status == "dead", "safety overrides bite");
        Assert(engine.Tick(Observe(1, true), 2000) == FishingAction.None, "stop prevents subsequent actions");

        engine.Start(options, 0);
        var animating = Observe(1, true);
        animating.CanUse = false;
        Assert(engine.Tick(animating, 1000) == FishingAction.None, "item animation prevents actions");
        engine.Stop("manual");
        Assert(engine.Tick(Observe(0), 9000) == FishingAction.None, "manual stop prevents recast");

        options.RecastDelayMs = 800;
        engine.Start(options, 0);
        options.RecastDelayMs = 5000;
        engine.Tick(Observe(1, true), 1000);
        Assert(engine.Tick(Observe(0), 2000) == FishingAction.Cast, "active settings are copied");

        options = new BotOptions { TimeoutSeconds = 30, MaxConsecutiveTimeouts = 2 };
        engine.Start(options, 0);
        engine.Tick(Observe(1), 200);
        Assert(engine.Tick(Observe(1), 30200) == FishingAction.ResetLine, "first timeout resets the line");
        Assert(engine.ConsecutiveTimeouts == 1 && engine.TotalTimeouts == 1, "timeout counted once");
        Assert(engine.Tick(Observe(1), 31000) == FishingAction.None, "timeout waits for return without counting twice");
        Assert(engine.Tick(Observe(0), 32000) == FishingAction.Cast, "timeout recovery recasts");
        engine.Tick(Observe(1), 34000);
        Assert(engine.Tick(Observe(1), 63999) == FishingAction.None, "each deployment gets its own full timeout");
        Assert(engine.Tick(Observe(1), 64000) == FishingAction.Stop, "consecutive timeout limit stops idle fishing");
        Assert(engine.ConsecutiveTimeouts == 2 && engine.TotalTimeouts == 2 && !engine.Enabled, "stopped counters remain available");

        engine.Start(options, 0);
        engine.Tick(Observe(1), 200);
        engine.Tick(Observe(1), 30200);
        engine.Tick(Observe(0), 32000);
        engine.Tick(Observe(1), 34000);
        Assert(engine.Tick(Observe(1, true), 35000) == FishingAction.Reel, "a bite following timeout is handled");
        Assert(engine.ConsecutiveTimeouts == 0 && engine.TotalTimeouts == 1, "bite resets streak but retains total timeouts");
        engine.Tick(Observe(0), 37000);
        engine.Tick(Observe(1), 39000);
        Assert(engine.Tick(Observe(1), 69000) == FishingAction.ResetLine, "new streak after bite does not stop prematurely");
        engine.Stop("manual");
        engine.Start(options, 70000);
        Assert(engine.ConsecutiveTimeouts == 0 && engine.TotalTimeouts == 0, "new session clears timeout counters");

        engine.Start(options, 0);
        engine.Tick(Observe(1, true), 1000);
        Assert(engine.Tick(Observe(1, true), 2000) == FishingAction.None, "cooldown expiry cannot reel unacknowledged action twice");
        Assert(engine.Tick(Observe(1, true), 16000) == FishingAction.Stop, "unacknowledged reel gets a bounded timeout");

        bool rejected = false;
        try { new BotOptions { MaxConsecutiveTimeouts = 0 }.Validate(); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "invalid timeout limit rejected");
        Console.WriteLine("PASS: " + checks + " engine checks.");
    }
}
