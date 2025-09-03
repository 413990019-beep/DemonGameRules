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
        private static ConcurrentDictionary<long, int> baseDamage = new ConcurrentDictionary<long, int>();
        private static ConcurrentDictionary<long, int> baseHealth = new ConcurrentDictionary<long, int>();
        private static HashSet<string> deadUnitsByBaseName = new HashSet<string>(); // 统一为HashSet，解决命名冲突
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
            deadUnitsByBaseName.Clear();
            baseDamage.Clear();
            baseHealth.Clear();

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
            deadUnitsByBaseName.Clear();
            baseDamage.Clear();
            baseHealth.Clear();

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
                baseDamage.TryRemove(unitId, out _);
                baseHealth.TryRemove(unitId, out _);
                _lastAppliedKills.TryRemove(unitId, out _);
                _lastWrittenMaxHp.TryRemove(unitId, out _);
                _lastAppliedKills_Dmg.TryRemove(unitId, out _);
                _lastWrittenDamage.TryRemove(unitId, out _);
            }
        }
        #endregion

        #region 5. 击杀奖励（原有逻辑保留）
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "newKillAction")]
        public static void Actor_newKillAction_Postfix(Actor __instance, Actor pDeadUnit)
        {
            try
            {
                if (!__instance.isAlive())
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

                    if (k >= 10 && !killer.hasTrait("first_blood")) killer.addTrait("first_blood");
                    if (k >= 100 && !killer.hasTrait("hundred_souls")) killer.addTrait("hundred_souls");
                    if (k >= 1000 && !killer.hasTrait("thousand_kill")) killer.addTrait("thousand_kill");
                    if (k >= 100000 && !killer.hasTrait("incarnation_of_slaughter")) killer.addTrait("incarnation_of_slaughter");

                    // 屠魔者：目标是飞升者，自己不是飞升系
                    if (victim != null && (victim.hasTrait("ascended_one") || victim.hasTrait("ascended_demon") || victim.hasTrait("daozhu")))
                    {
                        if (!killer.hasTrait("ascended_one") && !killer.hasTrait("ascended_demon") && !killer.hasTrait("daozhu"))
                            if (!killer.hasTrait("godslayer")) killer.addTrait("godslayer");
                    }

                    // 大道争锋：参赛者之间击杀，授予“以战成道”
                    if (DemonGameRules2.code.traitAction.IsGreatContestActive
                        && DemonGameRules2.code.traitAction.GreatContestants != null
                        && victim != null)
                    {
                        var list = DemonGameRules2.code.traitAction.GreatContestants;
                        if (list.Contains(killer) && list.Contains(victim) && !killer.hasTrait("path_of_battle"))
                            killer.addTrait("path_of_battle");
                    }
                }
                catch { /* 别拦战斗流程 */ }



            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[击杀奖励异常] {__instance?.data?.name} 错误: {ex.Message}");
            }
        }
        #endregion

        #region 3. Actor.updateStats 补丁 - 绝对值还原 / 仅击杀≥10 / 防“最大生命反复叠加”护栏

        // ===== 新增：给 traitAction 调用的缓存清理 =====
        internal static void ClearStatPatchCaches()
        {
            _lastAppliedKills.Clear();
            _lastWrittenMaxHp.Clear();
            _lastAppliedKills_Dmg.Clear();
            _lastWrittenDamage.Clear();
        }
        // ★ 新增：护栏用缓存（仅对击杀≥10的单位会用到）
        private static readonly ConcurrentDictionary<long, int> _lastAppliedKills = new();
        private static readonly ConcurrentDictionary<long, int> _lastWrittenMaxHp = new();
        // ★ 攻击护栏缓存
        private static readonly ConcurrentDictionary<long, int> _lastAppliedKills_Dmg = new();
        private static readonly ConcurrentDictionary<long, int> _lastWrittenDamage = new();

        private struct PrevState
        {
            public int HP;
            public int MaxHP;
            public int Kills;
            public bool Eligible; // 击杀≥10 才需要后续逻辑
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FastRoundToInt(float v) => (int)(v + (v >= 0f ? 0.5f : -0.5f));

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.updateStats))]
        private static void Prefix(Actor __instance, ref PrevState __state)
        {

            // ===== 新增：总开关关着就直接不参与这个补丁 =====
            if (!traitAction.StatPatchEnabled) { __state = default; return; }

            __state = default;
            var data = __instance?.data;
            if (data == null) return;

            // 🚫 船只不吃击杀加成
            if (IsShip(__instance)) { __state.Eligible = false; return; }

            int kills = data.kills;
            if (kills < 10) { __state.Eligible = false; return; }

            __state.Kills = kills;
            __state.HP = __instance.getHealth();
            __state.MaxHP = __instance.getMaxHealth();
            __state.Eligible = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.updateStats))]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Actor __instance, PrevState __state)
        {
            if (!traitAction.StatPatchEnabled) return;   // 冗余保险

            if (!__state.Eligible || __instance == null) return;
            var data = __instance.data;
            var stats = __instance.stats;
            if (data == null || stats == null) return;
            if (data.id <= 0) return;
            if (!Config.game_loaded || SmoothLoader.isLoading()) return;

            // 1) 读“基准上限”（原版+其他特质/装备后）
            float rawBase;
            try { rawBase = stats["health"]; } catch { return; }
            int baseHp = rawBase <= 0f ? 1 : FastRoundToInt(rawBase);

            // 2) 计算新的 MaxHP —— 带“防反复叠加护栏”
            //    如果 baseHp ≈ 我们上次写入的 MaxHP，说明基准未重算；只叠加“新增击杀”的增量
            //    否则，按完整公式（基准 + 全部击杀加成）重算
            const int BASE_EQ_TOL = 10; // 判断“base ≈ 上次写入”的容差
            long id = data.id;

            int lastKills = _lastAppliedKills.TryGetValue(id, out var lk) ? lk : 0;
            int lastMaxHp = _lastWrittenMaxHp.TryGetValue(id, out var lm) ? lm : 0;

            bool baseLooksLikeLast = lastMaxHp > 0 && Math.Abs(baseHp - lastMaxHp) <= BASE_EQ_TOL;

            int newMaxHp;
            if (baseLooksLikeLast)
            {
                // 基准未重算：baseHp 已经包含了 lastKills 的加成 → 只叠加本帧新增的击杀
                int deltaKills = __state.Kills - lastKills;
                if (deltaKills < 0) deltaKills = 0; // 极端情况：击杀数被外部下修
                newMaxHp = baseHp + deltaKills * 100;
            }
            else
            {
                // 基准已重算：按完整公式写入（基准 + 全部击杀加成）
                newMaxHp = baseHp + __state.Kills * 100;
            }


            // ✅ 血量兜底
            if (newMaxHp <= 0) newMaxHp = 1;

            stats["health"] = newMaxHp;

            // 3) 若“当前 HP”被拉回基准上限附近，则用【刷新前的绝对 HP】还原（夹在 1..newMaxHp）
            const int SNAP_TOLERANCE = 50;
            int nowHp = __instance.getHealth();
            if (Math.Abs(nowHp - baseHp) <= SNAP_TOLERANCE && __state.HP > 0)
            {
                int restored = __state.HP;
                if (restored < 1) restored = 1;
                if (restored > newMaxHp) restored = newMaxHp;
                if (restored != nowHp) __instance.setHealth(restored);
                nowHp = restored;
            }

            // 4) 战斗中防暴毙
            bool inCombat = false;
            try { inCombat = __instance.isFighting(); } catch { }
            if (inCombat && __state.HP > 0 && nowHp <= 0)
            {
                __instance.setHealth(1);
            }

            // 5) 记录本帧基线
            _lastAppliedKills[id] = __state.Kills;
            _lastWrittenMaxHp[id] = newMaxHp;


            // ===== 攻击加成：每击杀 +2（仅击杀≥10启用），带护栏 =====
            const int DMG_EQ_TOL = 2; // 基线≈上次写入的容差
            int baseDmg;
            try
            {
                float rawDmg = stats["damage"];         // 原版+特质+装备后的“基准攻击”
                baseDmg = rawDmg <= 0f ? 0 : FastRoundToInt(rawDmg);
            }
            catch
            {
                baseDmg = 0;
            }

            // 读取上次记录
            int lastKills_D = _lastAppliedKills_Dmg.TryGetValue(id, out var lkD) ? lkD : 0;
            int lastDmg = _lastWrittenDamage.TryGetValue(id, out var ldmg) ? ldmg : 0;

            // 如果 baseDmg ≈ 我们上次写入值，认为“基线没变”，只叠新增击杀；否则按完整公式重算
            bool dmgLooksLikeLast = lastDmg > 0 && Math.Abs(baseDmg - lastDmg) <= DMG_EQ_TOL;

            int newDmg;
            if (dmgLooksLikeLast)
            {
                int deltaKills = __state.Kills - lastKills_D;
                if (deltaKills < 0) deltaKills = 0;
                newDmg = baseDmg + deltaKills * 2;
            }
            else
            {
                newDmg = baseDmg + __state.Kills * 2;
            }

            // ✅ 攻击兜底
            if (newDmg < 0) newDmg = 0;


            // 写回并记录护栏
            stats["damage"] = newDmg;
            _lastAppliedKills_Dmg[id] = __state.Kills;
            _lastWrittenDamage[id] = newDmg;
        }

        #endregion







        #region 6. 恶魔防御（仅保留反击逻辑，移除拦截伤害）
        // 反入保护
        private static bool _isProcessingThorns = false;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "getHit")]
        public static bool Actor_getHit_Prefix(
            Actor __instance,
            ref float pDamage,
            AttackType pAttackType,
            BaseSimObject pAttacker)
        {
            // 守护：实例或数据缺失，放行
            if (__instance == null || __instance.data == null)
                return true;

            // 受击者击杀数（显式取）
            int victimKills = __instance.data.kills;

            // 攻击者击杀数（安全取）
            int attackerKills = 0;
            if (pAttacker != null && pAttacker.a != null && pAttacker.a.data != null)
                attackerKills = pAttacker.a.data.kills;

            // 规则：若双方击杀数都 <10，则完全跳过本前缀逻辑
            if (victimKills < 10 && attackerKills < 10)
                return true;

            // 仅当存在攻击者、且未处于反击处理中时，才考虑触发反击
            if (pAttacker != null && !_isProcessingThorns)
            {
                // 50% 概率不执行反击
                if (UnityEngine.Random.value < 0.5f)
                    return true;

                try
                {
                    _isProcessingThorns = true;
                    traitAction.ExecuteDamageExchange(__instance, pAttacker);
                }
                finally
                {
                    _isProcessingThorns = false;
                }
            }

            // 其他情况全部放行原方法
            return true;
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
                deadUnitsByBaseName.Add(victimBaseName);

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

                // 只移除当前单位的 base name
                deadUnitsByBaseName.Remove(victimBaseName);
            }
            #endregion


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

    }
}
