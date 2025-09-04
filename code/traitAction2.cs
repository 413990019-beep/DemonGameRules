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

        #region 日志总开关

        // ===== TXT 总开关（默认开）=====
        private static bool _txtLogEnabled = true;
        // ===== 详细日志开关（默认关）=====
        private static bool _txtLogVerboseEnabled = false;

        // 只读公开（给别处判断用）
        public static bool TxtLogEnabled => _txtLogEnabled;
        public static bool TxtLogVerboseEnabled => _txtLogVerboseEnabled;

        // 设置面板回调：TXT 总开关
        public static void OnTxtLogSwitchChanged(bool enabled)
        {
            _txtLogEnabled = enabled;
            UnityEngine.Debug.Log($"[恶魔规则] TXT日志 => {(enabled ? "开启/ON" : "关闭/OFF")}");
        }

        // 设置面板回调：详细日志开关
        public static void OnTxtLogDetailSwitchChanged(bool enabled)
        {
            _txtLogVerboseEnabled = enabled;
            UnityEngine.Debug.Log($"[恶魔规则] 详细日志 => {(enabled ? "开启/ON" : "关闭/OFF")}");
        }

        /// <summary>
        /// 供你在任何写日志前统一判断的“闸门”方法：
        /// 只要返回 false 就别写文件了；true 再写。
        /// </summary>

        // ======= traitAction 里（DemonGameRules2.code.traitAction）追加 =======

        public static void WriteWarCountSummary(string reasonTag = null)
        {
            if (!TxtLogEnabled || !TxtLogVerboseEnabled) return;
            try
            {
                var w = World.world;
                var wm = w?.wars;
                int active = 0;
                int parties = 0;
                if (wm != null)
                {
                    var list = wm.getActiveWars(); // IEnumerable<War>
                    foreach (var war in list)
                    {
                        if (war == null || war.hasEnded()) continue;
                        active++;
                        try
                        {
                            var a = war.main_attacker ?? war.getMainAttacker();
                            var d = war.main_defender ?? war.getMainDefender();
                            if (a != null) parties++;
                            if (d != null) parties++;
                        }
                        catch { }
                    }
                }
                var tag = string.IsNullOrEmpty(reasonTag) ? "战争数量" : $"战争数量/{reasonTag}";
                DemonGameRules.code.WorldLogPatchUtil.Write(
                    $"[世界历{YearNow()}年] [{tag}] 活跃战争: {active} 场，参战方(粗计): {parties}\n"
                );
            }
            catch { }
        }

        /// <summary>
        /// 亡国时“保守猜测”胜者：从仍在进行的战争里找该亡国的对手；
        /// 没找到就返回 null。只是日志参考，不干预逻辑。
        /// </summary>
        public static Kingdom TryGuessVictor(Kingdom dead)
        {
            try
            {
                var wm = World.world?.wars;
                if (dead == null || wm == null) return null;

                foreach (var war in wm.getActiveWars())
                {
                    if (war == null) continue;
                    Kingdom a = null, d = null;
                    try { a = war.main_attacker ?? war.getMainAttacker(); } catch { }
                    try { d = war.main_defender ?? war.getMainDefender(); } catch { }
                    if (a == null || d == null) continue;

                    if (a == dead && d != null && d.isAlive()) return d;
                    if (d == dead && a != null && a.isAlive()) return a;
                }
            }
            catch { }
            return null;
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

        #region   恶魔伤害系统（特质化）

        // === 恶魔系特质 ID（别改名，和上面 traits.Init 一致） ===
        private const string T_DEMON_MASK = "demon_mask";
        private const string T_DEMON_EVASION = "demon_evasion";
        private const string T_DEMON_REGEN = "demon_regen";
        private const string T_DEMON_AMPLIFY = "demon_amplify";
        private const string T_DEMON_ATTACK = "demon_attack";
        private const string T_DEMON_BULWARK = "demon_bulwark";
        private const string T_DEMON_FRENZY = "demon_frenzy";
        private const string T_DEMON_EXECUTE = "demon_execute";
        private const string T_DEMON_BLOODTHIRST = "demon_bloodthirst";

        private static bool Has(Actor a, string tid) => a != null && a.hasTrait(tid);

        // 标志位
        private static bool _isProcessingExchange = false; // 交换过程中不重入
        private static bool _isProcessingHit = false;      // 包裹 getHit，避免前缀递归

        // 参数
        private const float BASE_DMG_WEIGHT = 3f;     // 面板伤基础权重（无“狂热”）
        private const float FRENZY_EXTRA_WEIGHT = 1.5f;   // 恶魔狂热额外权重
        private const float KILL_RATE_PER_100_DEMON = 0.05f;  // 恶魔增幅：每 100 杀 +5%
        private const float KILL_RATE_CAP = 5.00f;  // 倍率封顶 +500%
        private const float TARGET_HP_HARD_CAP = 0.15f;  // 单击上限（受击者有“壁障”才启用）
        private const float HEAL_MAX_FRACTION_BASE = 0.05f;  // 基础回血上限 5%
        private const float HEAL_MAX_FRACTION_BLOOD = 0.08f;  // 嗜血：回血上限 8%
        private const float DEMON_EVADE_CHANCE = 0.20f;  // 恶魔闪避 20%
        private const int MIN_DAMAGE = 1;

        // —— 统一入口：被 getHit 前缀调用 ——
        // 触发条件：双方任一拥有 demon_mask
        public static void ExecuteDamageExchange(BaseSimObject source, BaseSimObject target)
        {
            if (_isProcessingExchange) return;

            try
            {
                if (source == null || !source.isActor() || !source.hasHealth()) return;
                if (target == null || !target.isActor() || !target.hasHealth()) return;

                Actor A = source.a; // A 视为“源”，先吃对手伤害，再反打
                Actor B = target.a;

                if (A == null || B == null) return;

                // 没有恶魔面具就不启用本系统
                bool demonOn = Has(A, T_DEMON_MASK) || Has(B, T_DEMON_MASK);
                if (!demonOn) return;

                int Ak = A.data?.kills ?? 0;
                int Bk = B.data?.kills ?? 0;

                _isProcessingExchange = true;

                // ===== A 先吃一发来自 B 的伤害（可被 A 的“闪避”闪掉）=====
                int dmgToA = CalculateDemonDamage(B, A, Bk);

                bool aEvaded = Has(A, T_DEMON_EVASION) && UnityEngine.Random.value < DEMON_EVADE_CHANCE;
                if (!aEvaded)
                {
                    try { _isProcessingHit = true; A.getHit(dmgToA, true, AttackType.Other, target, false, false, false); }
                    finally { _isProcessingHit = false; }
                }

                if (A.getHealth() <= 0) return;

                // ===== A 回血（仅 A 拥有“恶魔回血”才回） =====
                if (Has(A, T_DEMON_REGEN) && A.getHealth() > 100 && A.getHealth() < A.getMaxHealth())
                {
                    int heal = Mathf.Max(1, Ak / 2);
                    float capFrac = Has(A, T_DEMON_BLOODTHIRST) ? HEAL_MAX_FRACTION_BLOOD : HEAL_MAX_FRACTION_BASE;
                    int cap = Mathf.Max(1, Mathf.RoundToInt(A.getMaxHealth() * capFrac));
                    heal = Mathf.Clamp(heal, 1, cap);

                    int room = Mathf.Max(0, A.getMaxHealth() - A.getHealth() - 1);
                    if (room > 0) A.restoreHealth(Mathf.Min(heal, room));
                }

                // ===== A 反打 B（可被 B 的“闪避”闪掉） =====
                int dmgToB = CalculateDemonDamage(A, B, Ak);

                bool bEvaded = Has(B, T_DEMON_EVASION) && UnityEngine.Random.value < DEMON_EVADE_CHANCE;
                if (!bEvaded)
                {
                    try { _isProcessingHit = true; B.getHit(dmgToB, true, AttackType.Other, source, false, false, false); }
                    finally { _isProcessingHit = false; }
                }

                // ===== B 回血（仅 B 拥有“恶魔回血”才回） =====
                if (Has(B, T_DEMON_REGEN) && B.getHealth() > 100 && B.getHealth() < B.getMaxHealth())
                {
                    int heal = Mathf.Max(1, Bk / 2);
                    float capFrac = Has(B, T_DEMON_BLOODTHIRST) ? HEAL_MAX_FRACTION_BLOOD : HEAL_MAX_FRACTION_BASE;
                    int cap = Mathf.Max(1, Mathf.RoundToInt(B.getMaxHealth() * capFrac));
                    heal = Mathf.Clamp(heal, 1, cap);

                    int room = Mathf.Max(0, B.getMaxHealth() - B.getHealth() - 1);
                    if (room > 0) B.restoreHealth(Mathf.Min(heal, room));
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[恶魔伤害交换异常] {ex.Message}");
            }
            finally
            {
                _isProcessingExchange = false;
            }
        }

        // 计算“恶魔伤害系统”的伤害（完全特质化）
        private static int CalculateDemonDamage(Actor from, Actor to, int fromKills)
        {
            if (from == null) return MIN_DAMAGE;

            // 1) 面板伤害读取
            int panelDmg = 0;
            try
            {
                float raw = from.stats["damage"];
                panelDmg = raw > 0f ? Mathf.RoundToInt(raw) : 0;
            }
            catch { panelDmg = 0; }

            // 2) 面板权重：有“恶魔狂热”则提高权重
            float weight = BASE_DMG_WEIGHT + (Has(from, T_DEMON_FRENZY) ? FRENZY_EXTRA_WEIGHT : 0f);

            // 3) 基础伤害：面板*权重 + 击杀直加
            int baseDamage = Mathf.Max(MIN_DAMAGE, Mathf.RoundToInt(panelDmg * weight) + Mathf.Max(0, fromKills));

            // 4) 击杀倍率：持有“恶魔增幅”才启用，每 100 杀 +5%，封顶 +500%
            float mult = 1f;
            if (Has(from, T_DEMON_AMPLIFY))
            {
                float killRate = Mathf.Min((fromKills / 100f) * KILL_RATE_PER_100_DEMON, KILL_RATE_CAP);
                mult += killRate;
            }

            int finalDamage = Mathf.Max(MIN_DAMAGE, Mathf.RoundToInt(baseDamage * mult));

            // 5) 保底：持有“恶魔攻击”才启用 ≥ 击杀数 的保底
            if (Has(from, T_DEMON_ATTACK))
                finalDamage = Mathf.Max(finalDamage, Mathf.Max(0, fromKills));

            // 6) 斩杀：当目标血量 ≤20%，且攻击者有“恶魔斩首”时 ×1.25
            if (to != null && Has(from, T_DEMON_EXECUTE))
            {
                int toMax = 0, toCur = 0;
                try { toMax = Mathf.Max(1, to.getMaxHealth()); toCur = Mathf.Max(0, to.getHealth()); } catch { }
                if (toMax > 0 && (toCur <= toMax * 0.2f))
                    finalDamage = Mathf.Max(MIN_DAMAGE, Mathf.RoundToInt(finalDamage * 1.25f));
            }

            // 7) 单击硬上限：仅当“受击者”拥有“恶魔壁障”时启用
            if (to != null && Has(to, T_DEMON_BULWARK))
            {
                int toMax = 0;
                try { toMax = Mathf.Max(0, to.getMaxHealth()); } catch { }
                if (toMax > 0)
                {
                    int cap = Mathf.Max(MIN_DAMAGE, Mathf.RoundToInt(toMax * TARGET_HP_HARD_CAP));
                    if (finalDamage > cap) finalDamage = cap;
                }
            }

            return finalDamage;
        }

        #endregion


        #region  强杀机制
        // 放在 traitAction 类里
        public static void TryKill(Actor a)
        {
            if (a == null) return;

            try
            {
                // 1) 用一次“超额伤害”走完整的受击/死亡流程
                float lethal = Mathf.Max(10f, a.getHealth() + a.getMaxHealth() + 99999999f);
                a.getHit(lethal, true, AttackType.Other, null, false, false, false);
            }
            catch { /* 忽略 */ }

            try
            {
                // 2) 如果还没死（比如被某些护栏拦截），直接移除
                if (a != null && !a.isRekt() && a.hasHealth())
                {
                    ActionLibrary.removeUnit(a);
                }
            }
            catch { /* 忽略 */ }
        }
        #endregion

        #region 城市叛乱独立机制
        public static void TryRandomWar()
        {
            if (World.world == null || World.world.wars == null) return;

            List<Kingdom> kingdoms = World.world.kingdoms.list;
            if (kingdoms == null || kingdoms.Count == 0) return;

            int y = YearNow(); // 年份，一次拿够

            // ========= 新增：仅剩一个王国时，改为强制内部叛乱 =========
            if (kingdoms.Count == 1)
            {
                Kingdom solo = kingdoms[0];
                if (solo != null && solo.isAlive())
                {
                    int cityCount = solo.cities != null ? solo.cities.Count : 0;
                    if (cityCount >= 2)
                    {
                        UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][单国局面] 世界只剩 {solo.data.name}，改为触发内部叛乱。");
                        WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-叛乱", $"世界仅剩 {WorldLogPatchUtil.K(solo)}，强制触发内部叛乱"));
                        TriggerCityRebellion(solo);
                    }
                    else
                    {
                        UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][单国局面] 仅剩 {solo.data.name} 且城市数={cityCount}，无法触发叛乱（至少需要2座城）。");
                    }
                }
                return;
            }
            // =====================================================

            // 2个及以上王国的正常分支（保留你的既有行为）
            List<Kingdom> validAttackers = kingdoms
                .Where(k => k != null && k.isAlive() && k.cities != null && k.cities.Count >= 2)
                .ToList();
            if (validAttackers.Count == 0)
                validAttackers = kingdoms.Where(k => k != null && k.isAlive()).ToList();

            if (validAttackers.Count == 0) return;

            WarManager warManager = World.world.wars;
            Kingdom attacker = validAttackers[Randy.randomInt(0, validAttackers.Count)];

            // 找目标
            Kingdom defender = FindSuitableWarTarget(attacker, kingdoms, warManager);
            WarTypeAsset warType = GetDefaultWarType();

            // 没有任何目标或拿不到战争类型 —— 直接叛乱
            if (defender == null || warType == null)
            {
                UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][无宣战目标/类型] {attacker.data.name} 无法宣战，改为触发叛乱。");
                WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-叛乱", $"{WorldLogPatchUtil.K(attacker)} 无法找到宣战目标，触发内部叛乱"));

                if (attacker.cities != null && attacker.cities.Count >= 2)
                    TriggerCityRebellion(attacker);
                else
                    UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][叛乱跳过] {attacker.data.name} 城市不足，避免叛乱导致异常。");
                return;
            }

            // 有目标就尝试宣战
            War newWar = warManager.newWar(attacker, defender, warType);
            if (newWar != null)
            {
                UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉]{attacker.data.name} 强制向 {defender.data.name} 宣战！战争名称: {newWar.data.name}");
                WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-宣战", $"{WorldLogPatchUtil.K(attacker)} 强制向 {WorldLogPatchUtil.K(defender)} 宣战"));
                return;
            }

            // 宣战失败则直接叛乱
            UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][宣战失败] {attacker.data.name} 无法对 {defender.data.name} 宣战，改为触发叛乱。");
            WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-叛乱", $"{WorldLogPatchUtil.K(attacker)} 对 {WorldLogPatchUtil.K(defender)} 宣战失败，触发内部叛乱"));

            if (attacker.cities != null && attacker.cities.Count >= 2)
                TriggerCityRebellion(attacker);
            else
                UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][叛乱跳过] {attacker.data.name} 城市不足，避免叛乱导致异常。");
        }



        private static void TriggerCityRebellion(Kingdom targetKingdom)
        {
            int y = YearNow();
            if (targetKingdom == null || targetKingdom.cities == null || targetKingdom.cities.Count == 0)
            {
                UnityEngine.Debug.Log($"[世界历{y}年]{targetKingdom?.data.name} 没有城市，无法触发叛乱。");
                return;
            }

            List<City> rebelliousCities = targetKingdom.cities
                .Where(c => c.getLoyalty() < 30)
                .ToList();

            if (rebelliousCities.Count == 0)
                rebelliousCities = new List<City>(targetKingdom.cities);

            int rebellionCount = Randy.randomInt(1, Mathf.Min(20, rebelliousCities.Count) + 1);
            rebelliousCities.Shuffle();
            List<City> citiesToRebel = rebelliousCities.Take(rebellionCount).ToList();

            UnityEngine.Debug.Log($"[世界历{y}年]{targetKingdom.data.name} 发生叛乱！{rebellionCount}个城市宣布独立。");
            WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-叛乱", $"{WorldLogPatchUtil.K(targetKingdom)} 发生叛乱，{rebellionCount}个城市宣布独立"));

            foreach (City rebelCity in citiesToRebel)
            {
                Actor leader = FindCityLeader(rebelCity);
                if (leader != null)
                {
                    Kingdom newKingdom = rebelCity.makeOwnKingdom(leader, true, false);
                    if (newKingdom != null)
                    {
                        UnityEngine.Debug.Log($"[世界历{y}年]{rebelCity.data.name} 成功独立，成立 {newKingdom.data.name}，领导者: {leader.name}");
                        WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-建国/建制", $"{WorldLogPatchUtil.C(rebelCity)} 独立为 {WorldLogPatchUtil.K(newKingdom)}，领导者: {WorldLogPatchUtil.U(leader)}"));

                        WarTypeAsset warType = GetDefaultWarType();
                        if (warType != null)
                        {
                            World.world.wars.newWar(newKingdom, targetKingdom, warType);
                            WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-宣战", $"{WorldLogPatchUtil.K(newKingdom)} 独立后立即向 {WorldLogPatchUtil.K(targetKingdom)} 宣战"));
                        }
                    }
                }
                else
                {
                    UnityEngine.Debug.Log($"[世界历{y}年]{rebelCity.data.name} 没有合适的领导者，叛乱失败。");
                }
            }
        }

        // 叛军领袖：优先城主；否则城内最能打且活着的单位
        private static Actor FindCityLeader(City city)
        {
            if (city == null) return null;

            try
            {
                if (city.leader != null && city.leader.isAlive())
                    return city.leader;

                if (city.units != null && city.units.Count > 0)
                {
                    return city.units
                        .Where(a => a != null && a.isAlive())
                        .OrderByDescending(a =>
                        {
                            try { return a.stats != null ? a.stats["health"] : 0f; }
                            catch { return 0f; }
                        })
                        .FirstOrDefault();
                }
            }
            catch { /* 静默 */ }

            return null;
        }

        // 默认战争类型：先拿 WarTypeLibrary.normal；兜底从资源库取 "normal"
        private static WarTypeAsset GetDefaultWarType()
        {
            try
            {
                if (WarTypeLibrary.normal != null)
                    return WarTypeLibrary.normal;

                if (AssetManager.war_types_library != null)
                    return AssetManager.war_types_library.get("normal");
            }
            catch { /* 静默 */ }

            return null;
        }

        // 选宣战目标：排除自己/已在交战/已亡；随机一个可打的
        private static Kingdom FindSuitableWarTarget(Kingdom attacker, List<Kingdom> kingdoms, WarManager warManager)
        {
            if (attacker == null || kingdoms == null || warManager == null) return null;

            try
            {
                var possibleTargets = kingdoms.Where(k =>
                    k != null &&
                    k != attacker &&
                    k.isAlive() &&
                    !warManager.isInWarWith(attacker, k)
                ).ToList();

                if (possibleTargets.Count == 0) return null;
                return possibleTargets[Randy.randomInt(0, possibleTargets.Count)];
            }
            catch { /* 静默 */ }

            return null;
        }


        /// <summary>
        /// 定期把当前 WarManager 里正进行的战争抓出来，与上一次快照对比：
        /// 新增 => “宣战”；丢失 => “停战/战争结束”
        /// 完全不依赖具体方法名，适配性最好。
        /// </summary>
        /// <summary>
        /// 快照对比（配对键 + 活跃状态判定）
        /// - 当前活跃集合：按【两国ID排序后的 pairKey】收集（例如 "12-34"）
        /// - 旧快照：上一轮活跃 pairKey 集合
        /// - 新出现的 pairKey => 宣战
        /// - 旧有但当前不活跃的 pairKey => 停战/战争结束
        /// </summary>
        private static readonly HashSet<string> _warPairsLive = new HashSet<string>();

        private static void ScanWarDiffs()
        {
            try
            {
                var w = World.world;
                if (w == null || w.wars == null) return;

                float nowSec = (float)(w.getCurWorldTime() * 60.0);
                if (nowSec < _lastWarScanWorldSeconds + WAR_SCAN_INTERVAL_SECONDS) return;
                _lastWarScanWorldSeconds = nowSec;

                // 1) 当前活跃战争 -> pairKey 集合
                var currentActivePairs = new HashSet<string>();
                foreach (var war in GetAllAliveWars())
                {
                    if (war == null || war.hasEnded()) continue;

                    var k1 = war.main_attacker ?? war.getMainAttacker();
                    var k2 = war.main_defender ?? war.getMainDefender();
                    if (k1 == null || k2 == null) continue;

                    long idA = k1.id, idB = k2.id;
                    if (idA <= 0 || idB <= 0) continue;
                    if (idA > idB) { var t = idA; idA = idB; idB = t; }
                    currentActivePairs.Add($"{idA}-{idB}");
                }

                // 2) 首帧只建快照
                if (!_warScanPrimed)
                {
                    _warPairsLive.Clear();
                    foreach (var p in currentActivePairs) _warPairsLive.Add(p);
                    _warScanPrimed = true;
                    return;
                }

                int year = YearNow();

                // 3) 新增 => 宣战
                foreach (var pair in currentActivePairs)
                {
                    if (_warPairsLive.Contains(pair)) continue;

                    if (TryResolvePair(pair, w, out var ka, out var kb) &&
                        WorldEventGuard.ShouldLogWarStart(ka, kb, year))
                    {
                        WriteWorldEventSilently($"[世界历{year}年] [宣战] {DemonGameRules.code.WorldLogPatchUtil.K(ka)} vs {DemonGameRules.code.WorldLogPatchUtil.K(kb)}\n");
                    }
                }

                // 4) 旧有但消失 => 停战/结束
                foreach (var pair in _warPairsLive)
                {
                    if (currentActivePairs.Contains(pair)) continue;

                    if (TryResolvePair(pair, w, out var ka, out var kb) &&
                        WorldEventGuard.ShouldLogWarEnd(ka, kb, year))
                    {
                        WriteWorldEventSilently($"[世界历{year}年] [停战/战争结束] {DemonGameRules.code.WorldLogPatchUtil.K(ka)} vs {DemonGameRules.code.WorldLogPatchUtil.K(kb)}\n");
                    }
                }

                // 5) 覆盖快照
                _warPairsLive.Clear();
                foreach (var p in currentActivePairs) _warPairsLive.Add(p);
            }
            catch { /* 静默 */ }
        }






        /// <summary>从 WarManager 里尽量取出“正在进行”的 War 列表（字段名多版本兼容）。</summary>
        private static List<War> GetAllAliveWars()
        {
            var wm = World.world?.wars;
            if (wm == null) return new List<War>();
            // 直接用官方的活跃枚举
            return wm.getActiveWars().ToList(); // IEnumerable<War> -> List<War>
        }


        /// <summary>多重兜底拿 War 的唯一 ID。</summary>
        // 修正后（参数类型用 MapBox；调用处传入的 w 本来就是 MapBox）
        private static bool TryResolvePair(string pair, MapBox w, out Kingdom a, out Kingdom b)
        {
            a = null; b = null;
            var parts = pair.Split('-');
            if (parts.Length != 2) return false;
            if (!long.TryParse(parts[0], out var ida)) return false;
            if (!long.TryParse(parts[1], out var idb)) return false;

            foreach (var k in w.kingdoms.list)
            {
                if (k == null) continue;
                if (k.id == ida) a = k;
                else if (k.id == idb) b = k;
            }
            return a != null && b != null;
        }


        #endregion



        #region 随机生成
        private static readonly List<string> _majorRaces = new List<string>
        {
            "human", "elf", "orc", "dwarf"
        };

        private static readonly List<string> _otherUnits = new List<string>
        {
            "sheep","skeleton","white_mage","alien","necromancer","snowman","lil_pumpkin",
            "bandit",
            "civ_cat","civ_dog","civ_chicken","civ_rabbit","civ_monkey","civ_fox","civ_sheep","civ_cow","civ_armadillo","civ_wolf",
            "civ_bear","civ_rhino","civ_buffalo","civ_hyena","civ_rat","civ_alpaca","civ_capybara","civ_goat","civ_scorpion","civ_crab",
            "civ_penguin","civ_turtle","civ_crocodile","civ_snake","civ_frog","civ_liliar","civ_garlic_man","civ_lemon_man",
            "civ_acid_gentleman","civ_crystal_golem","civ_candy_man","civ_beetle",
            "bear","bee","beetle","buffalo","butterfly","smore","cat","chicken","cow","crab","crocodile",
            "druid","fairy","fire_skull","fly","flower_bud","lemon_snail","garl","fox","frog","ghost",
            "grasshopper","hyena","jumpy_skull","monkey","penguin","plague_doctor","rabbit","raccoon","seal","ostrich",
            "unicorn","rat","rhino","snake","turtle","wolf"
        };

        public static void SpawnRandomUnit()
        {

            if (!_autoSpawnEnabled) return; // 新增：没开就不生成

            string tID;
            if (UnityEngine.Random.value < 0.9f)
            {
                int randomMajorIndex = UnityEngine.Random.Range(0, _majorRaces.Count);
                tID = _majorRaces[randomMajorIndex];
            }
            else
            {
                int randomOtherIndex = UnityEngine.Random.Range(0, _otherUnits.Count);
                tID = _otherUnits[randomOtherIndex];
            }

            var hillTiles = TileLibrary.hills.getCurrentTiles();
            WorldTile tTile = null;
            if (hillTiles != null && hillTiles.Count > 0)
            {
                int randomTileIndex = UnityEngine.Random.Range(0, hillTiles.Count);
                tTile = hillTiles[randomTileIndex];
            }

            bool tMiracleSpawn = UnityEngine.Random.value < 0.1f;

            if (tTile != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    World.world.units.spawnNewUnit(tID, tTile, false, tMiracleSpawn, 6f, null, false);
                }
            }
        }
        #endregion

        #region UI榜单

        public static string BuildLeaderboardRichText()
        {
            if (World.world == null)
                return "<color=#AAAAAA>世界还没加载，想看榜单也得有世界。</color>";

            // 先确保内存里的两份榜是最新的
            EnsureLivingFirstPlace();
            UpdatePowerLeaderboard();

            int year = YearNow();
            int living = World.world.units?.Count(a => a != null && a.data != null && a.data.id > 0 && a.hasHealth()) ?? 0;

            var sb = new StringBuilder();
            sb.AppendLine($"<b>【世界历{year}年】</b>");
            sb.AppendLine($"存活单位数量: {living}\n");

            // —— 战力榜 ——
            sb.AppendLine("<color=#FF9900><b>【战力排行榜/Power Leaderboard】</b></color>");
            if (powerLeaderboard != null && powerLeaderboard.Count > 0)
            {
                for (int i = 0; i < Mathf.Min(powerLeaderboard.Count, 10); i++)
                {
                    var e = powerLeaderboard[i];
                    string rankColor = i == 0 ? "#FFD700" : i == 1 ? "#00CED1" : i == 2 ? "#CD7F32" : "#DDDDDD";
                    string ageInfo = GetUnitAgeInfo(e.Key);
                    sb.AppendLine($"{i + 1}. <color={rankColor}>{e.Key}</color> - <color=#FFCC00>{e.Value}</color><color=#AAAAAA>{ageInfo}</color>");
                }
            }
            else sb.AppendLine("<color=#AAAAAA>暂无上榜者/No rankers yet</color>");

            sb.AppendLine("<color=#666666>────────────────────</color>");

            // —— 击杀榜 ——
            sb.AppendLine("<color=#66AAFF><b>【杀戮排行榜/Kills Leaderboard】</b></color>");
            if (killLeaderboard != null && killLeaderboard.Count > 0)
            {
                for (int i = 0; i < Mathf.Min(killLeaderboard.Count, 10); i++)
                {
                    var e = killLeaderboard[i];
                    string rankColor = i == 0 ? "#FFD700" : i == 1 ? "#00CED1" : i == 2 ? "#CD7F32" : (IsUnitAlive(e.Key) ? "#DDDDDD" : "#AAAAAA");
                    string ageInfo = GetUnitAgeInfo(e.Key);
                    sb.AppendLine($"{i + 1}. <color={rankColor}>{e.Key}</color> - <color={GetKillColor(e.Value)}>{e.Value}</color><color=#AAAAAA>{ageInfo}</color>");
                }
            }
            else sb.AppendLine("<color=#AAAAAA>暂无上榜者/No rankers yet</color>");

            return sb.ToString();
        }

        #endregion




    }
}

