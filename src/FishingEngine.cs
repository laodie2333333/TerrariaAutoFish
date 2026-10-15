using System;

namespace TerrariaAutoFish
{
    public enum FishingAction { None, Cast, Reel, ResetLine, Stop }

    public sealed class FishingObservation
    {
        public bool Safe = true;
        public string UnsafeReason;
        public bool CanUse;
        public int Bobbers;
        public bool WaitingBobber;
        public bool CatchReady;
        public bool BiteActive;
    }

    // Time is supplied by the caller, so background frame rate does not change cooldowns.
    public sealed class FishingEngine
    {
        private BotOptions options;
        private long nextAction;
        private long deployedAt = -1;
        private int failedCasts;
        private bool awaitingCast;
        private bool awaitingReturn;
        private long returnStarted;
        public bool Enabled { get; private set; }
        public int Casts { get; private set; }
        public int Reels { get; private set; }
        public int ConsecutiveTimeouts { get; private set; }
        public int TotalTimeouts { get; private set; }
        public string Status { get; private set; }

        public void Start(BotOptions settings, long now)
        {
            settings.Validate();
            options = settings.Copy();
            Enabled = true;
            Casts = Reels = failedCasts = 0;
            ConsecutiveTimeouts = TotalTimeouts = 0;
            awaitingCast = false;
            awaitingReturn = false;
            deployedAt = -1;
            nextAction = now + 200;
            Status = "已接管，等待咬钩";
        }

        public void Stop(string reason)
        {
            Enabled = false;
            Status = reason;
        }

        public FishingAction Tick(FishingObservation observation, long now)
        {
            if (!Enabled) return FishingAction.None;
            if (!observation.Safe)
            {
                Stop(observation.UnsafeReason ?? "条件改变，已停止");
                return FishingAction.Stop;
            }
            if (observation.Bobbers > 0)
            {
                awaitingCast = false;
                failedCasts = 0;
                if (awaitingReturn)
                {
                    Status = "等待鱼漂完全返回";
                    if (now - returnStarted >= 15000)
                    {
                        Stop("收线后鱼漂超过 15 秒未返回，已停止；请检查游戏或网络状态");
                        return FishingAction.Stop;
                    }
                    return FishingAction.None;
                }
                if (deployedAt < 0) deployedAt = now;
                Status = observation.WaitingBobber ? "等待咬钩（可切换到其他窗口）" : "鱼漂正在返回";
                if (now < nextAction || !observation.CanUse) return FishingAction.None;
                if (observation.CatchReady)
                {
                    Reels++;
                    ConsecutiveTimeouts = 0;
                    awaitingReturn = true;
                    returnStarted = now;
                    nextAction = now + options.RecastDelayMs;
                    Status = "咬钩，收杆";
                    return FishingAction.Reel;
                }
                if (observation.WaitingBobber && !observation.BiteActive && now - deployedAt >= options.TimeoutSeconds * 1000L)
                {
                    ConsecutiveTimeouts++;
                    TotalTimeouts++;
                    if (ConsecutiveTimeouts >= options.MaxConsecutiveTimeouts)
                    {
                        Stop("连续 " + ConsecutiveTimeouts + " 次等待超时，已停止；请检查落点、水量及岩浆钓鱼条件");
                        return FishingAction.Stop;
                    }
                    awaitingReturn = true;
                    returnStarted = now;
                    nextAction = now + options.RecastDelayMs;
                    Status = "等待超时（" + ConsecutiveTimeouts + "/" + options.MaxConsecutiveTimeouts + "），收线后重抛";
                    return FishingAction.ResetLine;
                }
                return FishingAction.None;
            }
            deployedAt = -1;
            awaitingReturn = false;
            if (now < nextAction || !observation.CanUse) return FishingAction.None;
            if (awaitingCast && ++failedCasts >= 5)
            {
                Stop("连续抛竿未生成鱼漂，请检查位置、鱼饵和钓竿");
                return FishingAction.Stop;
            }
            awaitingCast = true;
            Casts++;
            nextAction = now + Math.Max(1500, options.RecastDelayMs);
            Status = "自动抛竿";
            return FishingAction.Cast;
        }
    }
}
