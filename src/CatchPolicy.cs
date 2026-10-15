using System;
using System.Collections.Generic;

namespace TerrariaAutoFish
{
    // A single use-item action reels every bobber. All active bites must be allowed.
    public static class CatchPolicy
    {
        public static bool CanReel(IEnumerable<int> bites, bool sonarActive, BotOptions options)
        {
            bool hasBite = false;
            foreach (int id in bites)
            {
                if (id == 0) continue;
                hasBite = true;
                if (id < 0 && options.SkipEnemies) return false;
                if (sonarActive && options.SonarFilterEnabled &&
                    (id < 0 || options.SelectedCatchIds == null || Array.IndexOf(options.SelectedCatchIds, id) < 0))
                    return false;
            }
            return hasBite;
        }
    }
}
