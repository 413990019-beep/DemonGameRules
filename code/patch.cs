using System.Collections.Generic;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using ai.behaviours;
using HarmonyLib;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using UnityEngine;
using ReflectionUtility;
using ai;
using System.Reflection;
using System.Reflection.Emit;
using DemonGameRules2.code;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace DemonGameRules.code
{
    internal class patch
    {
        // 静态字典声明（移至顶部，确保使用前已声明）
  

        private static readonly System.Random systemRandom = new System.Random(); // 明确使用System.Random并修改变量名避免混淆

   



        #region 1. MapBox.Start() 补丁 - 初始化+清空字典
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "Start")]
        public static void MapBox_Start_Postfix()
        {
            Debug.Log("[排行榜补丁] 已挂载到MapBox.Start()]");

            if (traitAction.killLeaderboard == null)
            {
                traitAction.killLeaderboard = new List<KeyValuePair<string, int>>();
                Debug.Log("[排行榜补丁] 初始化空排行榜列表");
            }

            // 每次进入场景复位“本世界是否已开过日志文件”的标记
            _sessionOpenedForThisWorld = false;

            // 清理所有静态数据
     
      

            _lastAppliedKills.Clear();
            _lastWrittenMaxHp.Clear();
            _lastAppliedKills_Dmg.Clear();
            _lastWrittenDamage.Clear();
        }
        #endregion



        // ===== 供 Update 调用的“新世界复位” =====
        private static void ResetStaticsForNewWorld()
        {
            // 这类是“按世界重置”的数据
         


            _lastAppliedKills.Clear();
            _lastWrittenMaxHp.Clear();
            _lastAppliedKills_Dmg.Clear();
            _lastWrittenDamage.Clear();

            // 排行榜容器只在 null 时建一次；如果你希望“每个世界都新榜”，就直接 Clear 而不是复用
            if (traitAction.killLeaderboard == null)
                traitAction.killLeaderboard = new List<KeyValuePair<string, int>>();
            else
                traitAction.killLeaderboard.Clear();

            // 如需同步清空战力榜
            DemonGameRules2.code.traitAction.powerLeaderboard?.Clear();

       

            Debug.Log("[编年史] 新世界：静态缓存与榜单已复位。");
        }

        #region 2. MapBox.Update() 补丁 - 控制排行榜显示 + 延迟初始化日志文件

        // ===== MapBox.Update 补丁（保留你已有的检测）=====
        private static bool _sessionOpenedForThisWorld = false;
        private static MapBox _lastWorldRefInPatch = null;
        private static bool _wasLoading = false;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "Update")]
        public static void MapBox_Update_Postfix()
        {
            try
            {
                bool nowLoading = SmoothLoader.isLoading();

                if (!Config.game_loaded || World.world == null)
                {
                    _sessionOpenedForThisWorld = false;
                    _lastWorldRefInPatch = null;
                    _wasLoading = nowLoading;
                    return;
                }

                bool loadJustFinished = _wasLoading && !nowLoading;
                bool worldRefChanged = !ReferenceEquals(_lastWorldRefInPatch, World.world);

                if (!_sessionOpenedForThisWorld || loadJustFinished || worldRefChanged)
                {
                    _lastWorldRefInPatch = World.world;

                    ResetStaticsForNewWorld();

                    string slotName = "Slot?";
                    try { int curSlot = SaveManager.getCurrentSlot(); if (curSlot >= 0) slotName = "Slot" + curSlot; } catch { }

                    DemonGameRules2.code.traitAction.BeginNewGameSession(slotName);
                    _sessionOpenedForThisWorld = true;

                    Debug.Log($"[编年史] 新世界会话日志已创建（{slotName}）");
                    DemonGameRules2.code.traitAction.WriteWorldEventSilently("[DEBUG] 世界就绪，日志已开档。\n");
                }

                // —— 放在最后，确保“复位/建档”完成后再推进你的周期逻辑
                if (Config.game_loaded && !nowLoading)
                {
                    DemonGameRules2.code.traitAction.UpdateLeaderboardTimer();
                }

                _wasLoading = nowLoading;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[编年史] 延迟初始化/世界监听失败: {ex.Message}");
            }
        }

        #endregion



        #region 4. 单位死亡时清理属性字典（新增补丁）
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "checkCallbacksOnDeath")]
        public static void Actor_CheckCallbacksOnDeath_Postfix(Actor __instance)
        {
            if (__instance != null && __instance.data != null && __instance.data.id > 0)
            {
                long unitId = __instance.data.id;
                // 移除死亡单位的基础属性记录，避免内存泄漏

                _lastAppliedKills.TryRemove(unitId, out _);
                _lastWrittenMaxHp.TryRemove(unitId, out _);
                _lastAppliedKills_Dmg.TryRemove(unitId, out _);
                _lastWrittenDamage.TryRemove(unitId, out _);
            }
        }
        #endregion

        #region  船只判断
        private static readonly HashSet<string> ShipIds = new(StringComparer.OrdinalIgnoreCase)
        {
            // 基础船只
            "boat_type_fishing",
            "boat_type_trading",
            "boat_type_transport",
            "boat_fishing",

            // 各族贸易船
            "boat_trading_human",
            "boat_trading_orc",
            "boat_trading_elf",
            "boat_trading_dwarf",
            "boat_trading_acid_gentleman",
            "boat_trading_alpaca",
            "boat_trading_angle",
            "boat_trading_armadillo",
            "boat_trading_bear",
            "boat_trading_buffalo",
            "boat_trading_candy_man",
            "boat_trading_capybara",
            "boat_trading_cat",
            "boat_trading_chicken",
            "boat_trading_cow",
            "boat_trading_crab",
            "boat_trading_crocodile",
            "boat_trading_crystal_golem",
            "boat_trading_dog",
            "boat_trading_fox",
            "boat_trading_frog",
            "boat_trading_garlic_man",
            "boat_trading_goat",
            "boat_trading_hyena",
            "boat_trading_lemon_man",
            "boat_trading_liliar",
            "boat_trading_monkey",
            "boat_trading_penguin",
            "boat_trading_piranha",
            "boat_trading_rabbit",
            "boat_trading_rat",
            "boat_trading_rhino",
            "boat_trading_scorpion",
            "boat_trading_sheep",
            "boat_trading_snake",
            "boat_trading_turtle",
            "boat_trading_wolf",
            "boat_trading_white_mage",
            "boat_trading_snowman",
            "boat_trading_necromancer",
            "boat_trading_evil_mage",
            "boat_trading_druid",
            "boat_trading_bee",
            "boat_trading_beetle",
            "boat_trading_fairy",
            "boat_trading_demon",
            "boat_trading_cold_one",
            "boat_trading_bandit",
            "boat_trading_alien",
            "boat_trading_greg",

            // 各族运输船
            "boat_transport_human",
            "boat_transport_orc",
            "boat_transport_elf",
            "boat_transport_dwarf",
            "boat_transport_acid_gentleman",
            "boat_transport_alpaca",
            "boat_transport_angle",
            "boat_transport_armadillo",
            "boat_transport_bear",
            "boat_transport_buffalo",
            "boat_transport_candy_man",
            "boat_transport_capybara",
            "boat_transport_cat",
            "boat_transport_chicken",
            "boat_transport_cow",
            "boat_transport_crab",
            "boat_transport_crocodile",
            "boat_transport_crystal_golem",
            "boat_transport_dog",
            "boat_transport_fox",
            "boat_transport_frog",
            "boat_transport_garlic_man",
            "boat_transport_goat",
            "boat_transport_hyena",
            "boat_transport_lemon_man",
            "boat_transport_liliar",
            "boat_transport_monkey",
            "boat_transport_penguin",
            "boat_transport_piranha",
            "boat_transport_rabbit",
            "boat_transport_rat",
            "boat_transport_rhino",
            "boat_transport_scorpion",
            "boat_transport_sheep",
            "boat_transport_snake",
            "boat_transport_turtle",
            "boat_transport_wolf",
            "boat_transport_white_mage",
            "boat_transport_snowman",
            "boat_transport_necromancer",
            "boat_transport_evil_mage",
            "boat_transport_druid",
            "boat_transport_bee",
            "boat_transport_beetle",
            "boat_transport_fairy",
            "boat_transport_demon",
            "boat_transport_cold_one",
            "boat_transport_bandit",
            "boat_transport_alien",
            "boat_transport_greg"
        };

        private static bool IsShip(Actor a)
        {
            try
            {
                string id = a?.asset?.id;
                if (string.IsNullOrEmpty(id)) return false;
                return ShipIds.Contains(id);
            }
            catch { return false; }
        }
        #endregion


        #region 5. 击杀奖励（原有逻辑保留）

        private static bool EnsureTrait(Actor a, string id, bool condition)
        {
            if (!condition || a == null || !a.hasHealth() || a.hasTrait(id)) return false;
            a.addTrait(id);
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "newKillAction")]
        public static void Actor_newKillAction_Postfix(Actor __instance, Actor pDeadUnit)
        {
            try
            {
                if (!__instance.isAlive())
                    return;


                if (IsShip(__instance))
                    return;


                if (__instance.hasTrait("bloodlust"))
                {
                    __instance.changeHappiness("just_killed", 1);
                }


                traitAction.TryMarkFavoriteByPower(__instance);


                // A：击杀额外 +1 击杀数（稳定滚雪球）
                if (__instance.hasTrait("rogue_kill_plus_one"))
                {
                    __instance.data.kills += 1;
                    // （如你有写入日志系统可在此记录）
                }

                // B：击杀时 1% 几率 +10 击杀数（偶尔暴富）
                if (__instance.hasTrait("rogue_kill_lucky10"))
                {
                    if (UnityEngine.Random.value < 0.01f) // 1% 概率
                    {
                        __instance.data.kills += 10;
                    }
                }

                // （可选）K：击杀回春（如果你想要“边杀边回血”的趣味项）
                // 说明：J 我做成“纯血量向”的出生轻量特质；如果你还想要“击杀回血版”，
                // 可以新建一个 ID，比如 rogue_heal_on_kill，并在 traits.Init() 中注册（无面板数值）。
                if (__instance.hasTrait("rogue_heal_on_kill"))
                {
                    // 直接小额回复固定生命（不破上限）
                    // 你也可以改成按最大生命百分比：
                    // int heal = Mathf.RoundToInt(__instance.stats["health"] * 0.05f); // 5%
                    int heal = 50;
                    __instance.restoreHealth(heal);
                }

                // === DGR: 击杀阶梯与特殊授勋 ===
                try
                {
                    var killer = __instance;
                    var victim = pDeadUnit;
                    int k = killer?.data?.kills ?? 0;

                    bool triggerFav = false; // 本次是否要触发收藏

                    // 【新增1】10杀必给恶魔面具
                    EnsureTrait(killer, "demon_mask", k >= 10);
                    EnsureTrait(killer, "demon_env_immunity", k >= 30);
                    EnsureTrait(killer, "demon_kill_bonus", k >= 10);

                    // 【新增2】拥有恶魔面具时，每次击杀 0.1% 概率随机获得一个“其他恶魔特质”
                    // 备注：只在还没拥有的恶魔特质里随机；一个不重复薅
                    if (killer != null && killer.hasTrait("demon_mask"))
                    {
                        if (UnityEngine.Random.value < 0.01f) // 1%
                        {
                            // 候选池（不含面具本体）
                            string[] pool = new string[]
                            {
                                "demon_evasion",     // 恶魔闪避
                                "demon_regen",       // 恶魔回血
                                "demon_amplify",     // 恶魔增幅
                                "demon_attack",      // 恶魔攻击（保底）
                                "demon_bulwark",     // 恶魔壁障（单击上限）
                                "demon_frenzy",      // 恶魔狂热（面板权重+）
                                "demon_execute",     // 恶魔斩首（处决加成）
                                "demon_bloodthirst"  // 恶魔嗜血（回血上限+）
                            };

                            // 过滤掉已拥有的
                            System.Collections.Generic.List<string> candidates = new System.Collections.Generic.List<string>(8);
                            for (int i = 0; i < pool.Length; i++)
                            {
                                string id = pool[i];
                                try
                                {
                                    if (!killer.hasTrait(id)) candidates.Add(id);
                                }
                                catch { /* 忽略异常，继续 */ }
                            }

                            // 随机拿一个发放
                            if (candidates.Count > 0)
                            {
                                int idx = UnityEngine.Random.Range(0, candidates.Count);
                                try { killer.addTrait(candidates[idx]); } catch { }
                            }
                        }
                    }

                    // 普通阶梯（不触发收藏）
                    EnsureTrait(killer, "first_blood", k >= 10);
                    EnsureTrait(killer, "hundred_souls", k >= 100);

                    // 1000杀与100000杀：触发收藏
                    if (EnsureTrait(killer, "thousand_kill", k >= 1000)) triggerFav = true;
                    if (EnsureTrait(killer, "incarnation_of_slaughter", k >= 100000)) triggerFav = true;

                    // 屠魔者：击杀 ascended_one，授予 godslayer 并触发收藏
                    if (killer != null && victim != null && killer.isAlive()
                        && victim.hasTrait("ascended_one") && !killer.hasTrait("ascended_one"))
                    {
                        if (EnsureTrait(killer, "godslayer", true)) triggerFav = true;
                    }

                    // 以战成道：双方皆 chosen_of_providence，授予 path_of_battle 并触发收藏
                    if (killer != null && victim != null && killer.isAlive()
                        && killer != victim
                        && killer.hasTrait("chosen_of_providence")
                        && victim.hasTrait("chosen_of_providence"))
                    {
                        if (EnsureTrait(killer, "path_of_battle", !killer.hasTrait("path_of_battle"))) triggerFav = true;
                    }

                    if (triggerFav) traitAction.TryAutoFavorite(killer);
                }
                catch (Exception) { /* 你要记日志自己加，这里不瞎吵 */ }




            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[击杀奖励异常] {__instance?.data?.name} 错误: {ex.Message}");
            }
        }
        #endregion

        #region 3. Actor.updateStats 补丁 - 增量叠加 / 仅击杀≥10 / 防覆盖护栏

        // ====== 护栏缓存：记录“我上次写入的增量”，避免覆盖其它特质 ======
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _lastBonusHp = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _lastBonusDmg = new();

        // 旧护栏（如果其他地方需要就保留；逐步可删）
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _lastAppliedKills = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _lastAppliedKills_Dmg = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _lastWrittenMaxHp = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _lastWrittenDamage = new();

        // 给 traitAction2.cs 调用。之前你报 “不包含定义”，我直接给成 public，省心。
        public static void ClearStatPatchCaches()
        {
            _lastAppliedKills.Clear();
            _lastAppliedKills_Dmg.Clear();
            _lastWrittenMaxHp.Clear();
            _lastWrittenDamage.Clear();
            _lastBonusHp.Clear();
            _lastBonusDmg.Clear();
        }

        // 本地小工具
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static int FastRoundToInt(float v) => (int)(v + (v >= 0f ? 0.5f : -0.5f));

        private struct PrevState
        {
            public int HP;
            public int MaxHP;
            public int Kills;
            public bool Eligible; // 击杀≥10 才需要后续逻辑
        }

        [HarmonyLib.HarmonyPrefix]
        [HarmonyLib.HarmonyPatch(typeof(Actor), nameof(Actor.updateStats))]
        private static void Prefix(Actor __instance, ref PrevState __state)
        {
            if (!DemonGameRules2.code.traitAction.StatPatchEnabled) { __state = default; return; }

            __state = default;

            var data = __instance?.data;
            if (data == null) return;

            // 🚫 船只不吃击杀加成
            if (IsShip(__instance)) { __state.Eligible = false; return; }

            int kills = data.kills;
            if (kills < 10) { __state.Eligible = false; return; }

            // 不再用“击杀≥10”做门槛，直接用特质是否存在
            __state.Kills = data.kills;                 // 仍然按击杀数算加成，但不做下限
            __state.HP = __instance.getHealth();
            __state.MaxHP = __instance.getMaxHealth();
            __state.Eligible = __instance.hasTrait("demon_kill_bonus"); // 拥有恶魔加成才启用本补丁
        }

        [HarmonyLib.HarmonyPostfix]
        [HarmonyLib.HarmonyPatch(typeof(Actor), nameof(Actor.updateStats))]
        [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.Last)]
        private static void Postfix(Actor __instance, PrevState __state)
        {
            if (!DemonGameRules2.code.traitAction.StatPatchEnabled) return;
            if (!__state.Eligible || __instance == null) return;

            var data = __instance.data;
            var stats = __instance.stats;
            if (data == null || stats == null) return;
            if (data.id <= 0) return;
            if (!Config.game_loaded || SmoothLoader.isLoading()) return;

            long id = data.id;

            // —— 当前总值（原版 + 其他特质/装备 + 可能存在的我上次写入）——
            int hpNowStat;
            try { hpNowStat = FastRoundToInt(stats["health"]); } catch { return; }
            if (hpNowStat <= 0) hpNowStat = 1;

            int dmgNowStat;
            try { dmgNowStat = FastRoundToInt(stats["damage"]); } catch { dmgNowStat = 0; }
            if (dmgNowStat < 0) dmgNowStat = 0;

            // —— 取出“我上次加了多少” —— 
            int lastBonusHp = _lastBonusHp.TryGetValue(id, out var lbHp) ? lbHp : 0;
            int lastBonusDmg = _lastBonusDmg.TryGetValue(id, out var lbDmg) ? lbDmg : 0;

            // —— 冷启动校准：缓存丢了但单位已带着我的加成，吸收为当前缓存，避免翻倍 —— 
            if (lastBonusHp == 0 && lastBonusDmg == 0 && __state.Kills > 0)
            {
                int inferHpBase = hpNowStat - __state.Kills * 100;
                int inferDmgBase = dmgNowStat - __state.Kills * 2;

                if (inferHpBase >= 1 && inferDmgBase >= 0)
                {
                    lastBonusHp = __state.Kills * 100;
                    lastBonusDmg = __state.Kills * 2;
                    _lastBonusHp[id] = lastBonusHp;
                    _lastBonusDmg[id] = lastBonusDmg;
                }
            }

            // ===== HP：增量叠加，不覆盖他人加成 =====
            int baseHp = hpNowStat - lastBonusHp;    // 剥离我上次的影响
            if (baseHp < 1) baseHp = 1;

            int wantBonusHp = __state.Kills * 100;   // 本帧应有加成
            int newMaxHp = baseHp + wantBonusHp;  // 新总值
            if (newMaxHp < 1) newMaxHp = 1;

            stats["health"] = newMaxHp;

            // 视觉保护：避免 HP 被拉回基线导致看起来瞬降
            const int SNAP_TOLERANCE = 50;
            int nowHp = __instance.getHealth();
            if (System.Math.Abs(nowHp - baseHp) <= SNAP_TOLERANCE && __state.HP > 0)
            {
                int restored = __state.HP;
                if (restored < 1) restored = 1;
                if (restored > newMaxHp) restored = newMaxHp;
                if (restored != nowHp) __instance.setHealth(restored);
                nowHp = restored;
            }

            // 战斗防暴毙
            bool inCombat = false;
            try { inCombat = __instance.isFighting(); } catch { }
            if (inCombat && __state.HP > 0 && nowHp <= 0)
                __instance.setHealth(1);

            _lastAppliedKills[id] = __state.Kills;
            _lastBonusHp[id] = wantBonusHp;

            // ===== DMG：增量叠加 =====
            int baseDmg = dmgNowStat - lastBonusDmg;
            if (baseDmg < 0) baseDmg = 0;

            int wantBonusDmg = __state.Kills * 2;
            int newDmg = baseDmg + wantBonusDmg;
            if (newDmg < 0) newDmg = 0;

            stats["damage"] = newDmg;

            _lastAppliedKills_Dmg[id] = __state.Kills;
            _lastBonusDmg[id] = wantBonusDmg;
        }
        #endregion








        #region 6. 恶魔防御（只保留“触发入口”，其余由恶魔系统自行处理）
        // 本地递归保护，别碰外部 internal
        private static bool _isProcessingThorns = false;
        private static bool _inDemonExchange = false; // 标记：我们自己触发的 getHit


        // 放在 patch 类字段区
        private static bool _isProcessingBlock = false;
        private const string TRAIT_DEMON_ENV_IMMUNITY = "demon_env_immunity";

        // 用 HashSet 加速包含判断
        private static readonly System.Collections.Generic.HashSet<AttackType> _blockedAttackTypes =
            new System.Collections.Generic.HashSet<AttackType>
        {
            AttackType.Poison,     // 中毒攻击
            AttackType.Eaten,      // 被吞噬攻击
            AttackType.Infection,      // 感染
            AttackType.Divine,      // 神圣
            AttackType.AshFever,      // 灰热病
            AttackType.Plague,     // 瘟疫攻击
            AttackType.Metamorphosis,     // 瘟疫攻击
            AttackType.Starvation,     // 瘟疫攻击
            AttackType.Explosion,  // 爆炸攻击
            AttackType.Infection,  // 感染攻击
            AttackType.Tumor,      // 肿瘤攻击
            AttackType.Water,      // 水属性攻击
            AttackType.Drowning,   // 溺水攻击
            AttackType.Gravity,   // 溺水攻击
            AttackType.Fire,       // 火焰攻击
            AttackType.None,       
            AttackType.Acid        // 酸液攻击
        };
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "getHit")]
        public static bool Actor_getHit_Prefix(
            Actor __instance,
            ref float pDamage,
            AttackType pAttackType,
            BaseSimObject pAttacker)
        {

            // 仅在：目标拥有“恶魔免疫” + 伤害类型在拦截表 + 没有攻击者（环境伤害）时，100% 拦截
            if (!_isProcessingBlock
                && __instance != null
                && __instance.hasHealth()
                && __instance.hasTrait(TRAIT_DEMON_ENV_IMMUNITY)     // ← 新增门槛
                && pAttacker == null
                && _blockedAttackTypes.Contains(pAttackType))
            {
                try
                {
                    _isProcessingBlock = true;
                    return false; // 直接拦截，不执行原方法
                }
                finally
                {
                    _isProcessingBlock = false;
                }
            }

            // 来自我们自己触发的 getHit，放行
            if (_inDemonExchange) return true;

            if (__instance == null || __instance.data == null)
                return true;

            // 受击/攻击者击杀数
            int victimKills = 0;
            try { victimKills = __instance.data.kills; } catch { victimKills = 0; }

            int attackerKills = 0;
            Actor attackerActor = pAttacker?.a;
            try { attackerKills = attackerActor?.data?.kills ?? 0; } catch { attackerKills = 0; }

            // 噪声过小就别浪费 CPU
            if (victimKills < 1 && attackerKills < 1)
                return true;

            // 只有任一方拥有“恶魔面具”才触发
            bool demonEligible =
                (__instance.hasTrait("demon_mask")) ||
                (attackerActor != null && attackerActor.hasTrait("demon_mask"));

            if (!demonEligible)
                return true;

            if (pAttacker != null && !_isProcessingThorns)
            {
                // 你设的是 0.7 概率跳过；要全时触发，删掉下面这行
                if (UnityEngine.Random.value < 0.7f)
                    return true;

                try
                {
                    _isProcessingThorns = true;
                    _inDemonExchange = true; // 标记本次由我们触发，避免前缀再次进来
                                            
                    traitAction.ExecuteDamageExchange(__instance, pAttacker);
                }
                finally
                {
                    _inDemonExchange = false;
                    _isProcessingThorns = false;
                }
            }

            return true; // 不拦截原始伤害流程
        }
        #endregion







        #region 7. 移速限制（原有逻辑保留）
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "updateMovement")]
        public static bool Actor_updateMovement_Prefix(Actor __instance, ref float pElapsed)
        {
            try
            {
                float currentSpeed = __instance.stats["speed"];
                if (currentSpeed > 50.0f)
                {
                    float speedRatio = 50.0f / currentSpeed;
                    pElapsed *= speedRatio;
                }
                return true;
            }
            catch (Exception ex)
            {
                return true;
            }
        }
        #endregion

        #region 8. 强制激活脑部特质逻辑
        [HarmonyPostfix, HarmonyPatch(typeof(Subspecies), "newSpecies")]
        public static void Subspecies_newSpecies_Postfix(Subspecies __instance)
        {
            try
            {
                traitAction.ApplyBrainTraitPackage(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DGR] ApplyBrainTraitPackage failed: {ex}");
            }
        }
        #endregion

        #region 9. 年龄机制
        [HarmonyPostfix, HarmonyPatch(typeof(Actor), "updateAge")]
        public static void Actor_UpdateAge_Postfix(Actor __instance) // 修正方法名，更贴合实际功能
        {
            if (__instance == null || !__instance.hasHealth())
                return;


            traitAction.TryMarkFavoriteByPower(__instance);
            traitAction.OnActorAgeTick(__instance);
            // 如果 age 是 float，稳妥点用 (age >= 1f && age < 2f)
            if (__instance.age == 1)
            {
                TryAdd(__instance, "rogue_kill_plus_one", 0.01f);
                TryAdd(__instance, "rogue_kill_lucky10", 0.01f);

                TryAdd(__instance, "rogue_starter_boost", 0.01f);
                TryAdd(__instance, "rogue_swift", 0.01f);
                TryAdd(__instance, "rogue_keen", 0.01f);
                TryAdd(__instance, "rogue_tough", 0.01f);
                TryAdd(__instance, "rogue_enduring", 0.01f);
                TryAdd(__instance, "rogue_longlife", 0.01f);
                TryAdd(__instance, "rogue_lightstrike", 0.01f);
                TryAdd(__instance, "rogue_guarded", 0.01f);
                TryAdd(__instance, "rogue_heal_on_kill", 0.01f);
            }

            // 小工具：独立概率加特质
            // 独立概率加特质（0~1）
            static void TryAdd(Actor a, string traitId, float chance01)
            {
                if (a == null || string.IsNullOrEmpty(traitId)) return;
                if (a.hasTrait(traitId)) return;

                // 用 UnityEngine.Random.value，避免 System.Random 撞名
                if (UnityEngine.Random.value < UnityEngine.Mathf.Clamp01(chance01))
                    a.addTrait(traitId);
            }



        }
        #endregion


        #region 10. 死亡日志与排行榜逻辑
        //private static float normalKillLogTimer = 0f;  // 时间窗口计时器
        //private static int normalKillLogCount = 0;     // 时间窗口内日志计数

        [HarmonyPostfix, HarmonyPatch(typeof(Actor), "checkDeath")]
        public static void Actor_checkDeath_Postfix(Actor __instance)
        {
            // 攻击者检测（注意空引用保护）
            Actor attacker = null;
            AttackType deathCause = AttackType.None;
            var lastHit = __instance.attackedBy;
            if (lastHit != null && !lastHit.isRekt() && lastHit.isActor() && lastHit != __instance)
            {
                attacker = lastHit.a;
                deathCause = __instance._last_attack_type;
            }



            // 受害者阵营信息
            Kingdom tPrevKingdom = __instance.kingdom;
            string victimKingdom = tPrevKingdom?.name ?? "无阵营/No faction";

            // 受害者基础信息
            string victimName = __instance.name;
            int victimKills = __instance.data?.kills ?? 0;
            int victimMaxHealth = __instance.getMaxHealth();

            // 攻击者信息
            int attackerCurrentHealth = 0;
            int attackerMaxHealth = 0;
            int attackerKills = 0;
            string attackerKingdom = "无阵营/No faction";

            if (attacker != null)
            {
                attackerCurrentHealth = attacker.getHealth();
                attackerMaxHealth = attacker.getMaxHealth();
                attackerKills = attacker.data?.kills ?? 0;
                attackerKingdom = attacker.kingdom?.name ?? "无阵营/No faction";
            }

            // 检查单位是否存活
            bool IsUnitAlive(string unitEntry)
            {
                return !unitEntry.StartsWith("[死亡]-");
            }

            // 更新排行榜数据
            void UpdateLeaderboard(string name, string kingdom, int kills)
            {
                
                if (kills <= 5) return;

                string targetBaseName = traitAction.GetBaseName(name);
                // 处理死亡前缀
                bool isDead = name.StartsWith("[死亡]-");
                string cleanName = traitAction.TrimDeathPrefix(name);
                string displayName = isDead ? $"[死亡]-{cleanName}({kingdom})" : $"{cleanName}({kingdom})";

                // 移除同基础名的旧条目
                traitAction.killLeaderboard.RemoveAll(entry =>
                    traitAction.GetBaseName(entry.Key.Split('(')[0]) == targetBaseName
                );

                // 添加新条目并确保存活单位优先
                traitAction.killLeaderboard.Add(new KeyValuePair<string, int>(displayName, kills));
                
            }

            // 高击杀单位（史诗击杀）逻辑
            bool isHighKillVictim = __instance.data?.favorite == true &&
                                   __instance.data != null &&
                                   !__instance.hasHealth() &&
                                   !__instance.name.StartsWith("[死亡]-");

            int worldYear = traitAction.YearNow();
            if (isHighKillVictim)
            {
                string originalName = __instance.name;
                string originalKingdom = victimKingdom;

                // 添加死亡前缀
                __instance.data.setName("[死亡]-" + originalName);
                victimName = __instance.name;

                // 更新排行榜
                traitAction.killLeaderboard.RemoveAll(x =>
                    traitAction.GetBaseName(x.Key.Split('(')[0]) == traitAction.GetBaseName(originalName));
                UpdateLeaderboard(__instance.name, originalKingdom, victimKills);

                // 计算奖励击杀数
                int bonusKills = victimKills / 2;
                int attackerKillsAfterBonus = attacker?.data?.kills ?? 0;
                if (attacker != null && attacker.data != null)
                {
                    attackerKillsAfterBonus = attacker.data.kills + bonusKills;
                }

                // 清理无效的排行榜条目
                if (traitAction.killLeaderboard.Count > 0)
                {
                    var leaderboardCopy = new List<KeyValuePair<string, int>>(traitAction.killLeaderboard);
                    foreach (var entry in leaderboardCopy)
                    {
                        int entryIndex = traitAction.killLeaderboard.IndexOf(entry);
                        if (entryIndex < 0 || entryIndex >= 3)
                            continue;

                        string rawName = entry.Key;
                        if (rawName.StartsWith("[死亡]-"))
                            continue;

                        string nameWithoutDeathTag = rawName.StartsWith("[死亡]-") ? rawName.Substring("[死亡]-".Length) : rawName;
                        string nameWithoutKingdom = nameWithoutDeathTag.Split('(')[0].Trim();
                        string entryBaseName = traitAction.GetBaseName(nameWithoutKingdom);

                        bool exists = World.world.units.Any(actor =>
                            actor != null && actor.hasHealth() && traitAction.GetBaseName(actor.name) == entryBaseName
                        );

                        if (!exists)
                        {
                            traitAction.killLeaderboard.Remove(entry);
                            Debug.Log($"<color=#FF9900>【排行榜清理】</color> 移除不存在的存活单位：{rawName}");
                            Debug.Log($"<color=#FF9900>[Ranking List Cleanup]</color> Removing non-existent surviving unit: {rawName}");
                        }
                    }
                    
                }

                // 显示史诗击杀通知
                string showMessage = $"<color=#FFFF00>{attacker?.data?.name ?? "环境Env"}</color>(<color=white>({attackerKills}→{attackerKillsAfterBonus})</color>)" +
                                     $"→→→" +
                                     $"<color=#FFFF00>{victimName}</color>(<color=white>--({victimKills})</color>)\n" +
                                     $"<color=#FF9900>[☠↔☠] +{bonusKills}</color>";

                NotificationHelper.ShowThisMessage(showMessage, 30f);

                // 详细调试日志
                string debugLog = $"\n\n<color=#666666>════════════════════════════════════════════════════════════</color>\n" +
                                 $"[世界历{worldYear}年] " +          
                                 $"【<color=#DD5555>◆ ⚔️ ◆</color>】 " +
                                 $"<color={traitAction.GetHealthColor(attackerCurrentHealth, attackerMaxHealth)}>[HP:{attackerCurrentHealth}/{attackerMaxHealth}]</color> " +
                                 $"<color=#FF9999>{attacker?.data?.name ?? "环境Env"}</color>" +
                                 $"<color={traitAction.GetKillColor(attackerKills)}>({attackerKills}→{attackerKillsAfterBonus})</color>" +
                                 $"<color=#CCCCCC>{attackerKingdom}</color>" +
                                 $"<color=#FF7777> →→→→→→→→→→→→→ </color>" +
                                 $"<color={traitAction.GetHealthColor(0, victimMaxHealth)}>[MaxHP:{victimMaxHealth}]</color> " +
                                 $"<color=#FFCC66>{victimName}</color>" +
                                 $"<color={traitAction.GetKillColor(victimKills)}>({victimKills})</color>" +
                                 $"<color=#CCCCCC>{victimKingdom}</color>\n" +
                                 $"<color=#FF9900>───────────────────────[☠↔☠] +{bonusKills}</color>\n" +
                                 $"<color=#AAAAAA>────────────────────────☠ : </color><color=#888888>{traitAction.GetAttackTypeChinese(deathCause)}</color>\n" +
                                 $"<color=#666666>════════════════════════════════════════════════════════════</color>\n\n";

                Debug.Log(debugLog);

                try
                {
    

                    string epicKillLog = $"[世界历{worldYear}年] {attacker?.data?.name ?? "环境Env"}({attackerKingdom}) -> {victimName}({victimKingdom}) +{bonusKills}\n";
                    
                    
                    DemonGameRules2.code.traitAction.WriteLeaderboardToFile(epicKillLog);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"保存史诗击杀记录失败: {ex.Message}");
                }


                // 释放死亡单位的称号
                string fullTitleToRelease = traitAction.GetFullTitle(originalName);
                traitAction.ReleaseTitle(fullTitleToRelease);

                // 击杀转移逻辑
                if (attacker != null && attacker.data != null)
                {
                    // 原攻击者存在，直接加击杀数
                    attacker.data.kills += bonusKills;
                    UpdateLeaderboard(attacker.name, attacker.kingdom?.name ?? "无阵营/No faction", attacker.data.kills);
                }
                else if (bonusKills > 0)
                {
                    bool transferred = false;

                    foreach (var unit in World.world.units)
                    {
                        if (unit == null || !unit.hasHealth() || unit.data == null || unit == __instance)
                            continue;

                        if (unit.data.kills > 30)
                        {
                            // 找到第一个高击杀单位就转移
                            unit.data.kills += bonusKills;
                            UpdateLeaderboard(unit.name, unit.kingdom?.name ?? "无阵营/No faction", unit.data.kills);

                            Debug.Log($"<color=#FF6600>【击杀转移】</color> {victimName} 被环境击杀，{bonusKills}击杀数转移给 {unit.name}");
                            Debug.Log($"<color=#FF6600>[Kill Transfer]</color> {victimName} was killed by the environment, {bonusKills} kills transferred to {unit.name}");

                            transferred = true;
                            break; // 找到一个就结束循环
                        }
                    }

                    // 如果没有找到高击杀单位，再找任意存活单位
                    if (!transferred)
                    {
                        foreach (var unit in World.world.units)
                        {
                            if (unit == null || !unit.hasHealth() || unit.data == null || unit == __instance)
                                continue;

                            unit.data.kills += bonusKills;
                            UpdateLeaderboard(unit.name, unit.kingdom?.name ?? "无阵营/No faction", unit.data.kills);

                            Debug.Log($"<color=#FF6600>【击杀转移】</color> {victimName} 被环境击杀，{bonusKills}击杀数转移给 {unit.name}");
                            Debug.Log($"<color=#FF6600>[Kill Transfer]</color> {victimName} was killed by the environment, {bonusKills} kills transferred to {unit.name}");

                            transferred = true;
                            break;
                        }
                    }

                    if (!transferred)
                    {
                        Debug.LogWarning($"【转移失败】无法找到接收击杀数的单位！{victimName} 的 {bonusKills} 击杀数丢失");
                        Debug.LogWarning($"[Transfer Failed] Unable to find a unit to receive the kills! {bonusKills} kills from {victimName} are lost");

                    }
                }

            }
            // 普通击杀日志（高击杀攻击者）
            //else if (attacker != null && attacker.data?.kills > 30 && Randy.randomChance(0.1f))
            //{
            //    // 时间窗口控制（每秒最多1条）
            //    if (Time.time - normalKillLogTimer > 2f)
            //    {
            //        normalKillLogTimer = Time.time;
            //        normalKillLogCount = 0;
            //    }

            //    if (normalKillLogCount < 1)
            //    {
            //        normalKillLogCount++;


                    

            //        int killerKills = attacker.data?.kills ?? 0;
            //        string killerKingdom = attacker.kingdom?.name ?? "无阵营/No faction";

            //        Debug.Log(
            //            $"\n<color=#666666>-----------------------------</color>\n " +
            //            $"<color=#FFFFFF>[⚔️]</color> " +
            //            $"<color={traitAction.GetHealthColor(attackerCurrentHealth, attackerMaxHealth)}>[HP:{attackerCurrentHealth}/{attackerMaxHealth}]</color> " +
            //            $"<color=#FFAAAA>{attacker.data?.name}</color>" +
            //            $"<color={traitAction.GetKillColor(killerKills)}>({killerKills})</color>" +
            //            $"<color=#BBBBBB>{killerKingdom}</color>" +
            //            $"<color=#FF7777> → </color>" +
            //            $"<color={traitAction.GetHealthColor(0, victimMaxHealth)}>[MaxHP:{victimMaxHealth}]</color> " +
            //            $"<color=#FFCC66>{victimName}</color>" +
            //            $"<color={traitAction.GetKillColor(victimKills)}>({victimKills})</color>" +
            //            $"<color=#BBBBBB>{victimKingdom}</color>" +
            //            $"\n<color=#AAAAAA>☠: </color><color=#888888>{traitAction.GetAttackTypeChinese(deathCause)}</color>"
            //        );
            //    }
            //}

            // 更新攻击者排行榜
            if (attacker != null && attacker.data != null && attacker.data.kills > 0)
            {
                UpdateLeaderboard(attacker.name, attacker.kingdom?.name ?? "无阵营/No faction", attacker.data.kills);


            }


            #region 死亡单位标记（确保排行榜条目带死亡前缀）
            if (!__instance.hasHealth() && victimKills > 10) // 只处理击杀数大于10的单位
            {
                string victimBaseName = traitAction.GetBaseName(victimName);
          

                // 遍历排行榜，补全死亡前缀（只处理匹配 baseName 且未标记死亡的单位）
                for (int i = 0; i < traitAction.killLeaderboard.Count; i++)
                {
                    var entry = traitAction.killLeaderboard[i];
                    string entryName = entry.Key;

                    // 如果已经带死亡前缀就跳过
                    if (entryName.StartsWith("[死亡]-"))
                        continue;

                    string entryBaseName = traitAction.GetBaseName(entryName.Split('(')[0].Trim());

                    if (entryBaseName == victimBaseName)
                    {
                        string cleanName = traitAction.TrimDeathPrefix(entryName); // 使用 TrimDeathPrefix
                        traitAction.killLeaderboard[i] = new KeyValuePair<string, int>($"[死亡]-{cleanName}", entry.Value);
                    }
                }

                // 高击杀单位强制标记死亡
                if (victimKills > 30)
                {
                    // 使用 TrimDeathPrefix 避免重复前缀
                    string originalName = traitAction.TrimDeathPrefix(__instance.name);
                    __instance.data.setName($"[死亡]-{originalName}");
                }

               
            }
            #endregion


        }
        #endregion


        





        

    }
}
