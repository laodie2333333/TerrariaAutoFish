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
        public bool SonarFilterEnabled;
        public int[] SelectedCatchIds = new int[0];

        public BotOptions Copy()
        {
            var copy = (BotOptions)MemberwiseClone();
            copy.SelectedCatchIds = SelectedCatchIds == null ? new int[0] : (int[])SelectedCatchIds.Clone();
            return copy;
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
            if (SelectedCatchIds != null && Array.Exists(SelectedCatchIds, id => id <= 0))
                throw new ArgumentException("声纳目标必须是有效的物品 ID。");
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
        public bool SonarActive;
        public bool SonarFilterEnabled;
        public int SkippedCatches;
        public string LastCatch = "尚未检测到咬钩";
        public string Status = "等待游戏启动";
        public string Player = "—";
        public string Rod = "—";
        public int Bait;
        public int Health;
        public int MaxHealth;
        public long UpdateCount;
        public DateTime LastUpdateUtc;
    }

    public sealed class CatchCatalogEntry
    {
        public int Id;
        public string Name;
        public string Category;
        public override string ToString() { return Name + "  ·  " + Category + "  #" + Id; }
    }

    public interface IBotBridge
    {
        void Start(BotOptions options);
        void UpdateSonarFilter(bool enabled, int[] selectedCatchIds);
        void UpdateInventoryFullStop(bool enabled);
        void Stop();
        BotSnapshot Snapshot();
        string[] DrainLog();
        bool IsGameForeground();
        CatchCatalogEntry[] CatchCatalog();
    }
}
