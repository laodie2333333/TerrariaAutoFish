using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using GameMain = Terraria.Main;

namespace TerrariaAutoFish
{
    public sealed class RuntimeBridge : IBotBridge
    {
        private static RuntimeBridge current;
        private static bool checkStartupOnly;
        private static bool startupVerified;
        private const string HarmonyId = "local.terraria.autofish.1458";
        private Harmony harmony;
        private readonly ConcurrentQueue<Action> commands = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<string> log = new ConcurrentQueue<string>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly FishingEngine engine = new FishingEngine();
        private readonly object snapshotLock = new object();
        private BotSnapshot snapshot = new BotSnapshot();
        private BotOptions options = new BotOptions();
        private Vector2 aim;
        private Vector2 startPosition;
        private bool hasAim;
        private int aimRod;
        private int selectedRod;
        private bool oldPlayWhenUnfocused;
        private bool settingsCaptured;
        private long lastPublish;
        private string lastReason;
        private bool sessionActive;
        private long sessionStarted;
        private long sessionFinished;
        private long stopSequence;
        private string stopReason;
        private bool automaticStop;
        private volatile IntPtr gameHandle;
        private CatchCatalogEntry[] catchCatalog = new CatchCatalogEntry[0];
        private readonly Dictionary<int, int> observedBites = new Dictionary<int, int>();
        private int skippedCatches;
        private string lastCatch = "尚未检测到咬钩";
        private bool lastSonar;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        public static IBotBridge Install()
        {
            if (current != null) throw new InvalidOperationException("钓鱼模块已安装。");
            var instance = new RuntimeBridge();
            current = instance;
            instance.harmony = new Harmony(HarmonyId);
            try
            {
                // Harmony may run a target type's initializer when patching it. Main's initializer
                // needs Program.SavePath, which is assigned inside LaunchGame.
                // Never resolve or patch Main here, even for the compatibility probe.
                // Patching RunGame itself too early also JITs code that touches Main. This earlier
                // console checkpoint has no Main dependencies and runs just after SavePath is set.
                Patch(instance.harmony, typeof(Terraria.Program), "InitializeConsoleOutput", "BeforeStartupCheckpoint", null, null);
                instance.WriteLog("启动引导已就绪，等待游戏初始化存档路径。");
                return instance;
            }
            catch
            {
                instance.harmony.UnpatchAll(HarmonyId);
                current = null;
                throw;
            }
        }

        public static void EnableStartupCheck() { checkStartupOnly = true; }

        public static bool StartupVerified() { return startupVerified; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BeforeStartupCheckpoint()
        {
            if (String.IsNullOrWhiteSpace(Terraria.Program.SavePath))
                throw new InvalidOperationException("游戏尚未初始化存档路径，已中止补丁安装。");
            current.InstallGamePatches();
            if (checkStartupOnly)
                Patch(current.harmony, typeof(Terraria.Program), "RunGame", "SkipGameWindowForCheck", null, null);
            startupVerified = true;
        }

        private static bool SkipGameWindowForCheck() { return false; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void InstallGamePatches()
        {
            // Force the initializer now so a poisoned Main cannot silently pass a patch-only check.
            RuntimeHelpers.RunClassConstructor(typeof(GameMain).TypeHandle);
            string savePath = Terraria.Program.SavePath;
            if (GameMain.SavePath != savePath
                || GameMain.WorldPath != System.IO.Path.Combine(savePath, "Worlds")
                || GameMain.PlayerPath != System.IO.Path.Combine(savePath, "Players"))
                throw new InvalidOperationException("游戏存档目录初始化不一致，已中止启动。");
            try
            {
                Patch(harmony, typeof(GameMain), "Update", "BeforeUpdate", null, null);
                Patch(harmony, typeof(GameMain), "CanPauseGame", null, "AfterCanPauseGame", null);
                Patch(harmony, typeof(Player), "ItemCheck", "BeforeItemCheck", "AfterItemCheck", "FinallyItemCheck");
                Patch(harmony, typeof(Player), "ItemCheck_Shoot", "BeforeShoot", null, null);
                foreach (var target in new[] { AccessTools.DeclaredMethod(typeof(GameMain), "Update"),
                    AccessTools.DeclaredMethod(typeof(GameMain), "CanPauseGame"),
                    AccessTools.DeclaredMethod(typeof(Player), "ItemCheck"),
                    AccessTools.DeclaredMethod(typeof(Player), "ItemCheck_Shoot") })
                {
                    var patches = Harmony.GetPatchInfo(target);
                    if (patches == null || !patches.Owners.Contains(HarmonyId))
                        throw new InvalidOperationException("钓鱼补丁安装验证失败：" + target.Name);
                }
                WriteLog("存档路径初始化完成，已安装 Terraria 1.4.5.8 后台钓鱼模块。");
            }
            catch
            {
                harmony.UnpatchAll(HarmonyId);
                throw;
            }
        }

        private static void Patch(Harmony harmony, Type target, string name, string prefix, string postfix, string finalizer)
        {
            var method = AccessTools.DeclaredMethod(target, name);
            if (method == null) throw new MissingMethodException(target.FullName, name);
            Func<string, HarmonyMethod> hook = n => n == null ? null : new HarmonyMethod(typeof(RuntimeBridge), n);
            harmony.Patch(method, hook(prefix), hook(postfix), null, hook(finalizer));
        }

        public void Start(BotOptions settings)
        {
            settings.Validate();
            var copy = settings.Copy();
            commands.Enqueue(() => Begin(copy));
        }

        public void Stop() { commands.Enqueue(() => StopNow("已手动停止", false)); }

        public void UpdateInventoryFullStop(bool enabled)
        {
            commands.Enqueue(() => {
                if (options.StopWhenInventoryFull == enabled) return;
                options.StopWhenInventoryFull = enabled;
                if (engine.Enabled)
                    WriteLog(enabled ? "背包无空格时停止已实时开启；立即检查主背包。"
                        : "背包无空格时停止已实时关闭。");
                Publish(true);
            });
        }

        public void UpdateSonarFilter(bool enabled, int[] selectedCatchIds)
        {
            // The UI owns its array. Capture a private copy before crossing onto the game thread.
            var ids = selectedCatchIds == null ? new int[0] : (int[])selectedCatchIds.Clone();
            if (Array.Exists(ids, id => id <= 0))
                throw new ArgumentException("声纳目标必须是有效的物品 ID。");
            ids = ids.Distinct().OrderBy(id => id).ToArray();
            commands.Enqueue(() => {
                if (options.SonarFilterEnabled == enabled
                    && (options.SelectedCatchIds ?? new int[0]).OrderBy(id => id).SequenceEqual(ids)) return;
                options.SonarFilterEnabled = enabled;
                options.SelectedCatchIds = ids;
                // Reclassify a bite already waiting in the water against the new selection.
                observedBites.Clear();
                lastCatch = "筛选已更新，等待咬钩";
                if (InWorld()) lastSonar = GameMain.LocalPlayer.sonarPotion;
                if (engine.Enabled)
                    WriteLog(enabled ? "声纳目标已实时更新，选择 " + ids.Length + " 项；"
                        + (ids.Length == 0 ? "有声纳效果时跳过全部鱼获，等待选择目标。"
                            : "后续收杆按新目标判断。") : "声纳筛选已实时关闭，恢复普通钓鱼。");
                Publish(true);
            });
        }

        public BotSnapshot Snapshot()
        {
            lock (snapshotLock) return snapshot;
        }

        public string[] DrainLog()
        {
            var result = new System.Collections.Generic.List<string>();
            string entry;
            while (log.TryDequeue(out entry)) result.Add(entry);
            return result.ToArray();
        }

        public bool IsGameForeground()
        {
            return gameHandle != IntPtr.Zero && GetForegroundWindow() == gameHandle;
        }

        public CatchCatalogEntry[] CatchCatalog()
        {
            lock (snapshotLock) return (CatchCatalogEntry[])catchCatalog.Clone();
        }

        private void LoadCatchCatalog()
        {
            if (catchCatalog.Length != 0 || GameMain.FishDropsDB == null) return;
            // Read the installed version's full drop catalog, without rolling or changing drops.
            var rules = AccessTools.Field(GameMain.FishDropsDB.GetType(), "_rules").GetValue(GameMain.FishDropsDB)
                as IEnumerable<Terraria.GameContent.FishDropRules.FishDropRule>;
            if (rules == null) throw new InvalidOperationException("无法读取游戏鱼获目录。");
            var ids = new SortedSet<int>(rules.SelectMany(rule => rule.PossibleItems ?? new int[0]).Where(id => id > 0));
            foreach (int id in GameMain.anglerQuestItemNetIDs) ids.Add(id);
            var entries = new List<CatchCatalogEntry>();
            foreach (int id in ids)
            {
                var item = new Item();
                item.SetDefaults(id);
                entries.Add(new CatchCatalogEntry { Id = id, Name = item.Name,
                    Category = item.questItem ? "任务鱼" : Terraria.ID.ItemID.Sets.IsFishingCrate[id] ? "宝匣" : "鱼获" });
            }
            lock (snapshotLock) catchCatalog = entries.OrderBy(entry => entry.Category).ThenBy(entry => entry.Name).ToArray();
            WriteLog("声纳鱼获目录已加载，共 " + entries.Count + " 项；名称跟随游戏语言。");
        }

        private void WriteLog(string message)
        {
            log.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            while (log.Count > 300) { string ignored; log.TryDequeue(out ignored); }
        }

        private bool InWorld()
        {
            return !GameMain.gameMenu && !GameMain.dedServ && GameMain.netMode != 2
                && GameMain.myPlayer >= 0 && GameMain.myPlayer < GameMain.player.Length
                && GameMain.LocalPlayer != null && GameMain.LocalPlayer.active;
        }

        private void Begin(BotOptions settings)
        {
            if (engine.Enabled) return;
            if (!InWorld()) { WriteLog("请先进入世界，拿起钓竿并手动抛一次竿。"); return; }
            var player = GameMain.LocalPlayer;
            string unsafeReason = CheckSafety(player, settings, false);
            if (unsafeReason != null) { WriteLog(unsafeReason); StopNow(unsafeReason); return; }
            if (!hasAim || aimRod != player.HeldItem.type)
            {
                WriteLog("尚未记录当前钓竿的抛竿方向。请在游戏中手动抛一次竿，再按 F8。");
                return;
            }
            // Only the normal fishing action is automated; no loot tables or fishing power are changed.
            options = settings;
            startPosition = player.Center;
            selectedRod = player.HeldItem.type;
            oldPlayWhenUnfocused = GameMain.SettingPlayWhenUnfocused;
            settingsCaptured = true;
            GameMain.SettingPlayWhenUnfocused = true;
            lastReason = null;
            observedBites.Clear();
            skippedCatches = 0;
            lastCatch = "尚未检测到咬钩";
            lastSonar = player.sonarPotion;
            engine.Start(options, clock.ElapsedMilliseconds);
            sessionActive = true;
            sessionStarted = clock.ElapsedMilliseconds;
            sessionFinished = sessionStarted;
            WriteLog("后台钓鱼已开启，可以切换到其他窗口。F9 随时停止。");
            if (options.SonarFilterEnabled)
                WriteLog("声纳筛选已开启，选择 " + options.SelectedCatchIds.Length + " 项；"
                    + (lastSonar ? "当前有声纳效果。" : "当前无声纳效果，按普通钓鱼收杆。"));
            Publish(true);
        }

        private void StopNow(string reason, bool automatic = true)
        {
            bool wasActive = sessionActive;
            engine.Stop(reason);
            sessionActive = false;
            if (wasActive)
            {
                sessionFinished = clock.ElapsedMilliseconds;
                stopSequence++;
                stopReason = reason;
                automaticStop = automatic;
                WriteLog(reason);
                WriteLog(String.Format("本次挂机 {0} 秒；抛竿 {1} 次；咬钩收杆 {2} 次；等待超时 {3} 次；声纳跳过 {4} 次。",
                    (sessionFinished - sessionStarted) / 1000, engine.Casts, engine.Reels, engine.TotalTimeouts, skippedCatches));
            }
            if (settingsCaptured)
            {
                GameMain.SettingPlayWhenUnfocused = oldPlayWhenUnfocused;
                settingsCaptured = false;
            }
            Publish(true);
        }

        private string CheckSafety(Player player, BotOptions settings, bool checkPosition)
        {
            if (player.dead || player.ghost) return "角色已死亡，已停止";
            if (player.HeldItem == null || player.HeldItem.fishingPole <= 0) return "请手持钓竿；切换物品后会停止挂机";
            if (checkPosition && player.HeldItem.type != selectedRod) return "钓竿已改变，已停止";
            if (player.CCed || player.noItems) return "角色暂时无法使用物品，已停止";
            if (GameMain.ingameOptionsWindow || GameMain.drawingPlayerChat
                || GameMain.editSign || GameMain.editChest || GameMain.mapFullscreen || GameMain.inFancyUI
                || player.chest != -1 || player.talkNPC >= 0 || player.sign >= 0 || GameMain.npcShop > 0
                || GameMain.InGameUI.IsVisible)
                return "打开了地图、聊天或交互界面，已停止";
            if (settings.StopBelowHealthPercent > 0 && player.statLifeMax2 > 0
                && player.statLife * 100L <= player.statLifeMax2 * (long)settings.StopBelowHealthPercent)
                return "生命低于设定阈值，已停止（不会暂停世界）";
            if (checkPosition && Vector2.DistanceSquared(startPosition, player.Center) > 96f * 96f)
            {
                hasAim = false;
                return "角色移动超过 6 格，已停止；请重新手动抛竿";
            }
            var conditions = player.GetFishingConditions();
            if (conditions.BaitItemType <= 0) return "鱼饵已用完，已停止";
            if (settings.SkipEnemies && conditions.BaitItemType == 2673)
                return "当前首选鱼饵是松露虫；请更换普通鱼饵后开始";
            if (settings.StopWhenInventoryFull)
            {
                // Slots 0..49 include the hotbar and main inventory. Inspect all
                // fifty on every safety check, including Begin and immediately
                // before an action; a cached free slot can disappear after loot.
                for (int i = 0; i < 50; i++)
                    if (player.inventory[i] == null || player.inventory[i].IsAir) return null;
                return "主背包没有空格，已停止（已有物品仍可能可以堆叠）";
            }
            return null;
        }

        private static void BeforeUpdate(GameMain __instance)
        {
            try
            {
                var self = current;
                if (__instance.Window != null) self.gameHandle = __instance.Window.Handle;
                Action command;
                while (self.commands.TryDequeue(out command)) command();
                if (self.engine.Enabled)
                {
                    if (!self.InWorld())
                    {
                        self.hasAim = false;
                        self.StopNow("已离开世界，已停止");
                    }
                    else
                    {
                        string reason = self.CheckSafety(GameMain.LocalPlayer, self.options, true);
                        if (reason != null) self.StopNow(reason);
                        else GameMain.SettingPlayWhenUnfocused = true;
                    }
                }
                if (!self.InWorld()) self.hasAim = false;
                else self.LoadCatchCatalog();
                self.Publish(false);
            }
            catch (Exception ex) { current.Fail(ex); }
        }

        private static void BeforeShoot(Player __instance, Item sItem)
        {
            var self = current;
            if (!self.engine.Enabled && __instance.whoAmI == GameMain.myPlayer && sItem.fishingPole > 0)
            {
                Vector2 direction = GameMain.MouseWorld - __instance.MountedCenter;
                if (direction.LengthSquared() > 16f && direction.LengthSquared() < 4000000f)
                {
                    self.aim = direction;
                    self.aimRod = sItem.type;
                    self.hasAim = true;
                }
            }
        }

        private static void AfterCanPauseGame(ref bool __result)
        {
            // Preserve the user's AutoPause preference. Only let ordinary backpack inspection
            // continue during a bot session; options, signs and other UI keep vanilla pausing.
            var self = current;
            if (__result && self != null && self.engine.Enabled && self.InWorld()
                && GameMain.netMode == 0 && GameMain.autoPause && GameMain.playerInventory
                && !GameMain.ingameOptionsWindow && GameMain.LocalPlayer.sign < 0
                && !GameMain.InGameUI.IsVisible)
                __result = false;
        }

        public sealed class ItemState
        {
            public bool Changed;
            public int MouseX, MouseY, InputX, InputY;
            public bool UseItem, DelayUse, MouseInterface;
        }

        private static void BeforeItemCheck(Player __instance, out ItemState __state)
        {
            __state = null;
            var self = current;
            if (!self.engine.Enabled || __instance.whoAmI != GameMain.myPlayer) return;
            try
            {
                string reason = self.CheckSafety(__instance, self.options, true);
                if (reason != null) { self.StopNow(reason); return; }
                int bobbers = 0;
                bool waiting = false;
                bool sonar = __instance.sonarPotion;
                var bites = new List<int>();
                var activeBites = new HashSet<int>();
                string filterStatus = sonar && self.options.SonarFilterEnabled && self.options.SelectedCatchIds.Length == 0
                    ? "未选择声纳目标，等待勾选（有声纳时不收杆）" : null;
                if (self.options.SonarFilterEnabled && sonar != self.lastSonar)
                {
                    self.WriteLog(sonar ? "声纳效果已生效，开始按目标筛选。" : "声纳效果已结束，恢复普通钓鱼。");
                    self.lastSonar = sonar;
                }
                foreach (var projectile in GameMain.projectile)
                {
                    if (projectile == null || !projectile.active || !projectile.bobber
                        || projectile.owner != __instance.whoAmI) continue;
                    bobbers++;
                    if (projectile.ai[0] != 0f) continue;
                    waiting = true;
                    if (projectile.ai[1] < 0f && projectile.localAI[1] != 0f)
                    {
                        int id = (int)projectile.localAI[1];
                        bites.Add(id);
                        activeBites.Add(projectile.whoAmI);
                        bool rejected = sonar && self.options.SonarFilterEnabled &&
                            (id < 0 || Array.IndexOf(self.options.SelectedCatchIds, id) < 0);
                        int previous;
                        if (!self.observedBites.TryGetValue(projectile.whoAmI, out previous) || previous != id)
                        {
                            self.observedBites[projectile.whoAmI] = id;
                            if (sonar)
                            {
                                string name = id > 0 ? Terraria.Lang.GetItemNameValue(id) : "怪物咬钩";
                                self.lastCatch = name + (rejected ? " · 已跳过" : " · 已识别");
                                if (rejected)
                                {
                                    self.skippedCatches++;
                                    self.WriteLog("声纳跳过：" + name + "；等待本次咬钩结束。");
                                }
                            }
                        }
                        if (rejected) filterStatus = "非目标咬钩，等待结束后继续（不收杆）";
                    }
                }
                foreach (int key in self.observedBites.Keys.Where(key => !activeBites.Contains(key)).ToArray())
                    self.observedBites.Remove(key);
                bool ready = CatchPolicy.CanReel(bites, sonar, self.options);
                var observation = new FishingObservation {
                    CanUse = __instance.itemAnimation == 0 && __instance.itemTime == 0 && __instance.reuseDelay == 0,
                    Bobbers = bobbers, WaitingBobber = waiting, CatchReady = ready, BiteActive = bites.Count > 0,
                    FilterStatus = filterStatus
                };
                var action = self.engine.Tick(observation, self.clock.ElapsedMilliseconds);
                if (action == FishingAction.Stop) { self.StopNow(self.engine.Status); return; }
                __state = new ItemState {
                    Changed = true, MouseX = GameMain.mouseX, MouseY = GameMain.mouseY,
                    InputX = PlayerInput.MouseX, InputY = PlayerInput.MouseY,
                    UseItem = __instance.controlUseItem, DelayUse = __instance.delayUseItem,
                    MouseInterface = __instance.mouseInterface
                };
                // Virtual aim exists only during ItemCheck. No OS mouse/keyboard input is sent.
                Vector2 worldAim = __instance.MountedCenter + self.aim;
                int x = (int)(worldAim.X - GameMain.screenPosition.X);
                int y = (int)(__instance.gravDir == -1f
                    ? GameMain.screenPosition.Y + GameMain.screenHeight - worldAim.Y
                    : worldAim.Y - GameMain.screenPosition.Y);
                GameMain.mouseX = PlayerInput.MouseX = x;
                GameMain.mouseY = PlayerInput.MouseY = y;
                bool use = action == FishingAction.Cast || action == FishingAction.Reel || action == FishingAction.ResetLine;
                __instance.controlUseItem = use;
                __instance.delayUseItem = false;
                __instance.mouseInterface = false;
                if (use)
                {
                    __instance.releaseUseItem = true;
                    self.WriteLog(self.engine.Status);
                }
                self.Publish(false);
            }
            catch (Exception ex)
            {
                Restore(__instance, __state);
                self.Fail(ex);
            }
        }

        private static void Restore(Player player, ItemState state)
        {
            if (state == null || !state.Changed) return;
            state.Changed = false;
            GameMain.mouseX = state.MouseX;
            GameMain.mouseY = state.MouseY;
            PlayerInput.MouseX = state.InputX;
            PlayerInput.MouseY = state.InputY;
            player.controlUseItem = state.UseItem;
            player.delayUseItem = state.DelayUse;
            player.mouseInterface = state.MouseInterface;
        }

        private static void AfterItemCheck(Player __instance, ItemState __state) { Restore(__instance, __state); }

        private static Exception FinallyItemCheck(Player __instance, ItemState __state, Exception __exception)
        {
            Restore(__instance, __state);
            if (__exception != null && __state != null) current.Fail(__exception);
            return __exception;
        }

        private void Fail(Exception ex)
        {
            // Retain the game's own exceptions; never hide a failure behind an enabled status.
            string reason = "钓鱼模块异常，已停止：" + ex.GetType().Name + " / " + ex.Message;
            if (lastReason == reason) return;
            lastReason = reason;
            if (!sessionActive) WriteLog(reason);
            StopNow(reason);
        }

        private void Publish(bool force)
        {
            long now = clock.ElapsedMilliseconds;
            if (!force && now - lastPublish < 200) return;
            lastPublish = now;
            bool inWorld = InWorld();
            var next = new BotSnapshot {
                Enabled = engine.Enabled, InWorld = inWorld, HasAim = hasAim,
                Casts = engine.Casts, Reels = engine.Reels, Status = engine.Status ?? "请进入世界，手动抛一次竿，再按 F8",
                ConsecutiveTimeouts = engine.ConsecutiveTimeouts, TotalTimeouts = engine.TotalTimeouts,
                SessionSeconds = ((sessionActive ? now : sessionFinished) - sessionStarted) / 1000,
                StopSequence = stopSequence, StopReason = stopReason, AutomaticStop = automaticStop,
                SonarFilterEnabled = options.SonarFilterEnabled, SkippedCatches = skippedCatches, LastCatch = lastCatch,
                UpdateCount = GameMain.GameUpdateCount, LastUpdateUtc = DateTime.UtcNow
            };
            if (inWorld)
            {
                var player = GameMain.LocalPlayer;
                next.SonarActive = player.sonarPotion;
                next.Player = player.name;
                next.Rod = player.HeldItem.fishingPole > 0 ? player.HeldItem.Name : "未手持钓竿";
                next.Health = player.statLife;
                next.MaxHealth = player.statLifeMax2;
                for (int i = 0; i < 58; i++)
                    if (player.inventory[i].bait > 0) next.Bait += player.inventory[i].stack;
                foreach (var projectile in GameMain.projectile)
                    if (projectile != null && projectile.active && projectile.bobber && projectile.owner == player.whoAmI)
                        next.Bobbers++;
            }
            lock (snapshotLock) snapshot = next;
        }
    }
}
