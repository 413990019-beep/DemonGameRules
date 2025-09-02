using System;
using System.Linq;
using System.Threading.Tasks;
using ai;
using UnityEngine;
using DemonGameRules.code;
using DemonGameRules;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib; // 为 Traverse 反射取字段
using System.Reflection;
using System.Collections;


namespace DemonGameRules2.code
{
    public static partial class traitAction
    {

        #region  亚种特质
        // —— 配置区：黑名单、核心包、附加包 ——
        private static readonly string[] _traitsToRemove = new string[]
        {
            "fire_elemental_form", "fenix_born", "metamorphosis_crab",
            "metamorphosis_chicken", "metamorphosis_wolf", "metamorphosis_butterfly",
            "death_grow_tree", "death_grow_plant", "death_grow_mythril", "metamorphosis_sword"
        };

        private static readonly string[] _coreBrainTraits = new string[]
        {
            "amygdala", "advanced_hippocampus", "prefrontal_cortex", "wernicke_area"
        };

        private static readonly string[] _fullTraitGroup = new string[]
        {
            "high_fecundity", "long_lifespan"
        };

        // 避免和 UnityEngine.Random 撞名，老老实实用 System.Random
        private static readonly System.Random _rng = new System.Random();

        /// <summary>
        /// 强制激活脑部特质逻辑（供 Subspecies.newSpecies Postfix 调用）
        /// </summary>
        public static void ApplyBrainTraitPackage(Subspecies s)
        {
            if (s == null) return;


            if (!_autoSpawnEnabled) return; // 新增：没开就不生成

            // 世界法则：必须打开“自动生成动物”，否则不处理
            if (WorldLawLibrary.world_law_animals_spawn == null ||
                !WorldLawLibrary.world_law_animals_spawn.isEnabled())
            {
                return;
            }



            // 1) 先清理会搅局的变形/重生类特质
            foreach (var t in _traitsToRemove)
            {
                if (s.hasTrait(t))
                    s.removeTrait(t);
            }

            // 2) 如果已经有 prefrontal_cortex，则补齐四件套并加完整属性组
            if (s.hasTrait("prefrontal_cortex"))
            {
                foreach (var core in _coreBrainTraits)
                {
                    if (!s.hasTrait(core))
                        s.addTrait(core, true);
                }
                foreach (var extra in _fullTraitGroup)
                {
                    s.addTrait(extra, true);
                }
                return;
            }

            // 3) 否则走概率：90% 只给 population_minimal；10% 尝试装四件套
            double roll = _rng.NextDouble();

            if (roll < 0.90)
            {
                // 90%：仅极简人口特质
                s.addTrait("population_minimal", true);
                return;
            }

            // 10%：尝试塞入核心脑部特质
            bool cortexAdded = false;
            s.addTrait("population_minimal", true);

            foreach (var core in _coreBrainTraits)
            {
                if (!s.hasTrait(core))
                {
                    s.addTrait(core, true);
                    if (core == "prefrontal_cortex" && s.hasTrait("prefrontal_cortex"))
                        cortexAdded = true;
                }
            }

            if (cortexAdded)
            {
                // 成功拿到前额叶，解锁完整属性组
                foreach (var extra in _fullTraitGroup)
                {
                    s.addTrait(extra, true);
                }
            }
            else
            {
                // 没装上前额叶，维持极简人口特质即可（重复 addTrait 引擎会自己去重）
                s.addTrait("population_minimal", true);
            }
        }

        #endregion
        #region 模式切换

        // ===== 新增：updateStats 补丁总开关（默认 false）=====
        public static bool _statPatchEnabled = false;

        // 供外部只读（可选）
        public static bool StatPatchEnabled => _statPatchEnabled;

        // 设置面板/配置文件回调：切换总开关
        public static void OnStatPatchSwitchChanged(bool enabled)
        {
            _statPatchEnabled = enabled;
            UnityEngine.Debug.Log($"[恶魔规则] updateStats击杀成长补丁 => {(enabled ? "开启/ON" : "关闭/OFF")}");

            // 关掉时清空护栏缓存，避免旧值干扰
            if (!enabled)
            {
                try { DemonGameRules.code.patch.ClearStatPatchCaches(); } catch { }
            }
        }

        // ====== 新增：自动生成生物总开关 ======
        private static bool _autoSpawnEnabled = false;

        // 配置面板勾选时由 ModLoader 调用
        public static void OnAutoSpawnChanged(bool enabled)
        {
            _autoSpawnEnabled = enabled;
            UnityEngine.Debug.Log($" AutoSpawn/自动生成生物 => {(_autoSpawnEnabled ? "开启ON" : "关闭OFF")}");
        }
        // 这个方法会在开关被切换时调用
        // 切换模式的方法
        public static void OnModeSwitchChanged(bool isDemonModeEnabled)
        {
            isDemonMode = isDemonModeEnabled;

            if (isDemonMode)
            {
                // 激活恶魔模式
                ActivateDemonMode();
            }
            else
            {
                // 切换到正常模式
                ActivateNormalMode();
            }
        }


        private static void ActivateDemonMode()
        {
            // 恶魔模式的逻辑
            UnityEngine.Debug.Log("已切换到恶魔模式 (Switched to Demon Mode)");

            // 恶魔模式下保持现有逻辑
            // 恶魔飞升、强制宣战、恶魔使徒等功能将继续存在
            // 这里的代码保持不变，确保这些功能继续生效
        }

        private static void ActivateNormalMode()
        {
            // 正常模式的逻辑
            UnityEngine.Debug.Log("已切换到正常模式 (Switched to Normal Mode)");

            // 禁用恶魔模式的功能
            DisableDemonModeEvents();
            // 替换为武道大会风格的文本
        }

        private static void DisableDemonModeEvents()
        {

            // 禁用恶魔使徒事件
            _lastEvilRedMageYear = 0;
            EvilRedMage = null;
            EvilRedMageRevertYear = -1;

            // 其他与恶魔模式相关的禁用代码
        }

        // ====== 新增：自动收藏总开关 ======
        private static bool _autoFavoriteEnabled = true;

        // 配置面板勾选时由 ModLoader 调用
        public static void OnAutoFavoriteChanged(bool enabled)
        {
            _autoFavoriteEnabled = enabled;
            UnityEngine.Debug.Log($" AutoFavorite/自动收藏 => {(_autoFavoriteEnabled ? "开启ON" : "关闭OFF")}");

        }

        public static void OnWarIntervalChanged(float yearsFloat)
        {
            int years = Mathf.Clamp(Mathf.RoundToInt(yearsFloat), 5, 200);
            _warIntervalYears = years;
            UnityEngine.Debug.Log($"强制宣战间隔 => {years} 年 (War Interval)");

            // 重对齐
            var w = World.world;
            if (w != null)
            {
                int curYear = YearNow();
                lastWarYear = curYear - (curYear % _warIntervalYears);
            }
        }

        public static void OnGreatContestIntervalChanged(float yearsFloat)
        {
            int years = Mathf.Clamp(Mathf.RoundToInt(yearsFloat), 60, 2000);
            _greatContestIntervalYears = years;
            UnityEngine.Debug.Log($"大道争锋间隔 => {years} 年 (Great Contest Interval)");

            var w = World.world;
            if (w != null)
            {
                int curYear = YearNow();
                _lastGreatContestYear = curYear - (curYear % _greatContestIntervalYears);
            }
        }



        #endregion

        #region 地图重置

        // —— 标记文件路径 ——
        // 放在 logs 根下放一个“全局上膛”标记；每个槽位自己的目录里放一个“本槽已执行”标记
        private static string GetArmFlagPath()
            => System.IO.Path.Combine(LogDirectory, "RESET_ARM.flag"); // 上膛标记（全局只有一个）

        private static string GetDoneFlagPathForCurrentSlot()
        {
            if (string.IsNullOrEmpty(_currentSessionDir))
            {
                // 兜底：尝试从当前槽位推导目录
                string slotName = "Slot?";
                try { int cur = SaveManager.getCurrentSlot(); if (cur >= 0) slotName = "Slot" + cur; } catch { }
                _currentSessionDir = System.IO.Path.Combine(LogDirectory, $"slot_{slotName}");
                if (!System.IO.Directory.Exists(_currentSessionDir)) System.IO.Directory.CreateDirectory(_currentSessionDir);
            }
            return System.IO.Path.Combine(_currentSessionDir, "RESET_DONE.flag");
        }


        // 兼容：旧布尔仍保留，但只用于日志
        private static bool _yearZeroPurgePending = false;

        // ========== 设置面板回调：只负责“上膛/卸膛” ==========
        // ON：创建全局上膛标记文件（下一次读档时触发一次）
        // OFF：删除上膛标记，同时清掉所有槽位的“已执行”标记，方便下次再 ON 能再次触发
        public static void OnResetLoopChanged(bool enabled)
        {
            _yearZeroPurgePending = enabled;
            try
            {
                var arm = GetArmFlagPath();
                if (enabled)
                {
                    // 仅(重)写 ARM（更新时间戳，用于“再次上膛”判定）
                    System.IO.File.WriteAllText(arm, $"armed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    Debug.Log("[DGR] 世界归零与清图/World Reset 已上膛（标记已写入）/ ARMED (flag written).");
                }
                else
                {
                    // 关：删 ARM，并清理所有槽位 DONE
                    if (System.IO.File.Exists(arm)) System.IO.File.Delete(arm);
                    if (System.IO.Directory.Exists(LogDirectory))
                    {
                        foreach (var dir in System.IO.Directory.GetDirectories(LogDirectory, "slot_*"))
                        {
                            var done = System.IO.Path.Combine(dir, "RESET_DONE.flag");
                            if (System.IO.File.Exists(done)) System.IO.File.Delete(done);
                        }
                    }
                    Debug.Log("[DGR] 世界归零与清图/World Reset 已卸膛；已清理所有槽位 DONE 标记 / DISARMED; all DONE flags cleared.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DGR] 世界归零与清图/World Reset 回调异常 / OnResetLoopChanged exception: {ex.Message}");
            }
            Debug.Log($"[DGR] 世界归零与清图/World Reset => {enabled}（开=ON / 关=OFF）");
        }


        // ========== 读档/新世界就绪时调用：若“上膛”且本槽未执行过，则执行一次 ==========
        // 你现在在 BeginNewGameSession(slotName) 里调用了 ApplyYearZero... —— 把那行改成调用本方法
        public static void ApplyYearZeroAndPurge_ArmedFileOnce()
        {
            if (!Config.game_loaded || World.world == null) return;

            var arm = GetArmFlagPath();
            var done = GetDoneFlagPathForCurrentSlot();

            if (!System.IO.File.Exists(arm)) return; // 未上膛，不执行

            // 若已做过且 ARM 没有“更新上膛”，就不重复
            if (System.IO.File.Exists(done))
            {
                var tArm = System.IO.File.GetLastWriteTimeUtc(arm);
                var tDone = System.IO.File.GetLastWriteTimeUtc(done);
                if (tArm <= tDone)
                {
                    Debug.Log("[DGR] 世界归零与清图/World Reset 已上膛，但本槽已执行（未重新上膛）/ armed but already done for this slot (no rearm).");
                    return;
                }
            }

            try
            {
                ForceWorldYearToZero();
                PurgeAllUnits_TryKill();
                Debug.Log("[DGR] 年归零与清图已执行（标记触发，已刷新时间戳）/ Year-Zero & Purge executed (armed-file, timestamp rearmed).");
                DemonGameRules.code.patch.ClearStatPatchCaches();

                // 标记本槽已执行（更新时间戳）
                System.IO.File.WriteAllText(done, $"done at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                // 注意：不再删除 ARM，让它保持“开启状态”，下次如果你再次“开”(重写ARM)即可再触发
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DGR] 年归零与清图失败 / Year-Zero & Purge failed: {ex.Message}");
            }
        }

        // ========== 把世界年归零 ==========
        private static void ForceWorldYearToZero()
        {
            var mb = MapBox.instance;
            if (mb == null) return;

            var fiMapStats = mb.GetType().GetField("map_stats", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var mapStats = fiMapStats?.GetValue(mb);
            if (mapStats == null) return;

            var fiWorldTime = mapStats.GetType().GetField("world_time", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            fiWorldTime?.SetValue(mapStats, 0.0d);

            var fiHist = mapStats.GetType().GetField("history_current_year", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            fiHist?.SetValue(mapStats, 0);

            var gameStats = mb.game_stats;
            var fiData = gameStats?.GetType().GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
            var dataObj = fiData?.GetValue(gameStats);
            var fiGameTime = dataObj?.GetType().GetField("gameTime", BindingFlags.Public | BindingFlags.Instance);
            fiGameTime?.SetValue(dataObj, 0.0d);
        }

        // ========== 清图（用你自己的 TryKill） ==========
        private static void PurgeAllUnits_TryKill()
        {
            var mb = MapBox.instance;
            if (mb?.units == null) return;

            var list = mb.units.getSimpleList();
            if (list == null || list.Count == 0) return;

            foreach (var a in list.ToArray())
            {
                if (a == null) continue;
                TryKill(a);
            }
        }

        #endregion

        #region    野生单位与超级生命清理
        // ====== 新增：野生单位与“超级生命”清理总开关（默认关闭）======
        private static bool _wildCleanupEnabled = false;

        // 配置面板勾选时调用
        public static void OnWildCleanupChanged(bool enabled)
        {
            _wildCleanupEnabled = enabled;
            Debug.Log($" WildCleanup/野生单位与超级生命清理 => {(_wildCleanupEnabled ? "开启ON" : "关闭OFF")}");
        }

        /// <summary>
        /// 给 Actor.updateAge Postfix 调用的统一入口。
        /// 这里面做：按战力尝试自动收藏、清理 super_health、50% 处死野生阵营单位。
        /// </summary>
        public static void OnActorAgeTick(Actor a)
        {
            if (a == null || !a.hasHealth())
                return;


            if (!_wildCleanupEnabled)
                return;

            // 2) 清除“超级生命”buff
            if (a.hasTrait("super_health"))
            {
                a.removeTrait("super_health");
            }

            // 3) 野生阵营 50% 清理
            var k = a.kingdom;
            if (k != null && k.wild)
            {
                // 显式限定 UnityEngine.Random，避免和 System.Random 撞名
                if (UnityEngine.Random.value < 0.50f)
                {
                    TryKill(a); // 你已有封装
                }
            }
        }
        #endregion

    }
}

