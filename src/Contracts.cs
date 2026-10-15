using System;

namespace TerrariaAutoFish
{
    public sealed class BotOptions
    {
        public int RecastDelayMs = 800;
        public int TimeoutSeconds = 180;
        public int MaxConsecutiveTimeouts = 3;
        public int StopBelowHealthPercent = 25;
        public bool SkipEnemies = true;
        public bool StopWhenInventoryFull = true;

        public BotOptions Copy()
        {
            return (BotOptions)MemberwiseClone();
        }

        public void Validate()
        {
            if (RecastDelayMs < 300 || RecastDelayMs > 5000)
                throw new ArgumentException("重抛间隔应为 300～5000 毫秒。");
            if (TimeoutSeconds < 30 || TimeoutSeconds > 1800)
                throw new ArgumentException("无咬钩超时应为 30～1800 秒。");
            if (MaxConsecutiveTimeouts < 1 || MaxConsecutiveTimeouts > 20)
                throw new ArgumentException("连续超时停止次数应为 1～20。");
            if (StopBelowHealthPercent < 0 || StopBelowHealthPercent > 90)
                throw new ArgumentException("低生命停止阈值应为 0～90%。");
        }
    }

    public sealed class BotSnapshot
    {
        public bool Enabled;
        public bool InWorld;
        public bool HasAim;
        public int Casts;
        public int Reels;
        public int Bobbers;
        public int ConsecutiveTimeouts;
        public int TotalTimeouts;
        public long SessionSeconds;
        public long StopSequence;
        public string StopReason;
        public bool AutomaticStop;
        public string Status = "等待游戏启动";
        public string Player = "—";
        public string Rod = "—";
        public int Bait;
        public int Health;
        public int MaxHealth;
        public long UpdateCount;
        public DateTime LastUpdateUtc;
    }

    public interface IBotBridge
    {
        void Start(BotOptions options);
        void Stop();
        BotSnapshot Snapshot();
        string[] DrainLog();
        bool IsGameForeground();
    }
}
