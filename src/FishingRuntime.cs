using System;
using System.Collections.Concurrent;
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
        private long lastInventoryCheck;
        private bool inventoryFull;
        private string lastReason;
        private bool sessionActive;
        private long sessionStarted;
        private long sessionFinished;
        private long stopSequence;
        private string stopReason;
        private bool automaticStop;
        private volatile IntPtr gameHandle;

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
                Patch(harmony, typeof(Player), "ItemCheck", "BeforeItemCheck", "AfterItemCheck", "FinallyItemCheck");
                Patch(harmony, typeof(Player), "ItemCheck_Shoot", "BeforeShoot", null, null);
                foreach (var target in new[] { AccessTools.DeclaredMethod(typeof(GameMain), "Update"),
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
            if (unsafeReason != null) { WriteLog(unsafeReason); return; }
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
            lastInventoryCheck = 0;
            inventoryFull = false;
            lastReason = null;
            engine.Start(options, clock.ElapsedMilliseconds);
            sessionActive = true;
            sessionStarted = clock.ElapsedMilliseconds;
            sessionFinished = sessionStarted;
            WriteLog("后台钓鱼已开启，可以切换到其他窗口。F9 随时停止。");
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
                WriteLog(String.Format("本次挂机 {0} 秒；抛竿 {1} 次；咬钩收杆 {2} 次；等待超时 {3} 次。",
                    (sessionFinished - sessionStarted) / 1000, engine.Casts, engine.Reels, engine.TotalTimeouts));
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
            if (GameMain.playerInventory || GameMain.ingameOptionsWindow || GameMain.drawingPlayerChat
                || GameMain.editSign || GameMain.editChest || GameMain.mapFullscreen || GameMain.inFancyUI)
                return "打开了背包、地图或交互界面，已停止";
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
            if (settings.StopWhenInventoryFull && checkPosition)
            {
                long now = clock.ElapsedMilliseconds;
                if (now - lastInventoryCheck >= 1000)
                {
                    lastInventoryCheck = now;
                    inventoryFull = true;
                    // Slots 0..49 are the main inventory; dedicated coin/ammo slots do not count.
                    for (int i = 0; i < 50; i++)
                        if (player.inventory[i].IsAir) { inventoryFull = false; break; }
                }
                if (inventoryFull) return "主背包没有空格，已停止（已有物品仍可能可以堆叠）";
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
                bool waiting = false, ready = false, biteActive = false, enemyBite = false;
                foreach (var projectile in GameMain.projectile)
                {
                    if (projectile == null || !projectile.active || !projectile.bobber
                        || projectile.owner != __instance.whoAmI) continue;
                    bobbers++;
                    if (projectile.ai[0] != 0f) continue;
                    waiting = true;
                    if (projectile.ai[1] < 0f && projectile.localAI[1] != 0f)
                    {
                        biteActive = true;
                        if (projectile.localAI[1] < 0f) enemyBite = true;
                        if (!self.options.SkipEnemies || projectile.localAI[1] > 0f) ready = true;
                    }
                }
                // A use-item action reels every bobber. Do not pull another bobber's monster.
                if (self.options.SkipEnemies && enemyBite) ready = false;
                var observation = new FishingObservation {
                    CanUse = __instance.itemAnimation == 0 && __instance.itemTime == 0 && __instance.reuseDelay == 0,
                    Bobbers = bobbers, WaitingBobber = waiting, CatchReady = ready, BiteActive = biteActive
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
                UpdateCount = GameMain.GameUpdateCount, LastUpdateUtc = DateTime.UtcNow
            };
            if (inWorld)
            {
                var player = GameMain.LocalPlayer;
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
