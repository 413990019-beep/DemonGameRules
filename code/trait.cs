using System;
using System.Collections.Generic;
using System.Reflection;
using ReflectionUtility;
using strings;

namespace DemonGameRules.code
{
    internal class traits
    {
        // ===== 图标策略：全局兜底 + 按特质覆写 =====
        // 全局开关：true=所有特质都用 FALLBACK_ICON，除非被 ICON_OVERRIDE 覆写
        private const bool FORCE_ICON_FALLBACK = true;
        private const string FALLBACK_ICON = "trait/daozhu"; // 兜底用的“道主”图标

        // 按特质覆写：填了就无视全局兜底（留给你日后逐个替换）
        // 示例：{"hundred_souls":"trait/hundred_souls"} 一旦填上就会生效
        private static readonly Dictionary<string, string> ICON_OVERRIDE =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ageless"] = "trait/ageless",
                ["daozhu"] = "trait/daozhu",
                ["ascended_demon"] = "trait/ascended_demon",
                ["ascended_one"] = "trait/ascended_one",
                ["chosen_of_providence"] = "trait/chosen_of_providence",
                ["eternal_legend"] = "trait/eternal_legend",
                ["fallen_ascension"] = "trait/fallen_ascension",
                ["first_blood"] = "trait/first_blood",
                ["flesh_of_the_divine"] = "trait/flesh_of_the_divine",
                ["godslayer"] = "trait/godslayer",
                ["hundred_souls"] = "trait/hundred_souls",
                ["incarnation_of_slaughter"] = "trait/incarnation_of_slaughter",
                ["path_of_battle"] = "trait/path_of_battle",
                ["rogue_enduring"] = "trait/rogue_enduring",
                ["rogue_guarded"] = "trait/rogue_guarded",
                ["rogue_heal_on_kill"] = "trait/rogue_heal_on_kill",
                ["rogue_keen"] = "trait/rogue_keen",
                ["rogue_kill_lucky10"] = "trait/rogue_kill_lucky10",
                ["rogue_kill_plus_one"] = "trait/rogue_kill_plus_one",
                ["rogue_lightstrike"] = "trait/rogue_lightstrike",
                ["rogue_longlife"] = "trait/rogue_longlife",
                ["rogue_starter_boost"] = "trait/rogue_starter_boost",
                ["rogue_swift"] = "trait/rogue_swift",
                ["rogue_tough"] = "trait/rogue_tough",
                ["thousand_kill"] = "trait/thousand_kill",
                ["whispers_from_the_abyss"] = "trait/whispers_from_the_abyss",
                ["world_eater"] = "trait/world_eater"
            };

        private static string ResolveIcon(string traitId, string requested)
        {
            // 1) 先看有没有按特质覆写
            if (!string.IsNullOrEmpty(traitId) && ICON_OVERRIDE.TryGetValue(traitId, out var overridePath) && !string.IsNullOrWhiteSpace(overridePath))
                return overridePath;

            // 2) 全局兜底开着：一律使用道主图标
            if (FORCE_ICON_FALLBACK)
                return FALLBACK_ICON;

            // 3) 兜底关着：优先用传入路径，否则还是给个道主不至于空
            return string.IsNullOrWhiteSpace(requested) ? FALLBACK_ICON : requested;
        }

        // —— 小工具：创建一个特质对象，默认不设数值 —— 
        private static ActorTrait CreateTrait(string id, string path_icon, string group_id)
        {
            var trait = new ActorTrait
            {
                id = id,
                path_icon = ResolveIcon(id, path_icon),  // 统一走图标策略
                group_id = group_id,
                needs_to_be_explored = false,
                base_stats = new BaseStats()
            };
            return trait;
        }

        // —— 小工具：安全设置某个基础属性 —— 
        private static void SafeSetStat(BaseStats baseStats, string statKey, float value)
        {
            baseStats[statKey] = value;
        }

        // —— 规则1：禁止手动给予（UI“+特质”不显示/不可点）——
        private static void ForbidManualGive(ActorTrait t)
        {
            t.can_be_given = false;
            t.is_mutation_box_allowed = false; // 若也想禁突变盒，放开
            // t.unlocked_with_achievement = true; t.achievement_id = "_mod_lock_only_";
        }


        // —— Rarity 兼容解析：不关心是 common/Common/R0_Common —— 
        private static object ParseRarityValue(Type rarityType, string key)
        {
            string k = key.ToLowerInvariant();

            string[] tries = k switch
            {
                "common" => new[] { "common", "R0_Common" },
                "uncommon" => new[] { "uncommon", "R1_Uncommon" },
                "rare" => new[] { "rare", "R2_Rare" },
                "epic" => new[] { "epic", "R3_Epic" },
                "legendary" => new[] { "legendary", "R4_Legendary", "R3_Legendary" },
                "mythical" => new[] { "mythical", "mythic", "R5_Mythical" },
                "mythic" => new[] { "mythic", "mythical", "R5_Mythical" },
                _ => new[] { key }
            };

            foreach (var name in tries)
            {
                try { return Enum.Parse(rarityType, name, true); } catch { }
            }

            int fallback = k switch
            {
                "common" => 0,
                "uncommon" => 1,
                "rare" => 2,
                "epic" => 3,
                "legendary" => 4,
                "mythical" or "mythic" => 5,
                _ => 0
            };
            return Enum.ToObject(rarityType, fallback);
        }

        private static void SetRarityCompat(ActorTrait trait, string rarityName)
        {
            var t = typeof(ActorTrait);

            var f = t.GetField("rarity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                var val = ParseRarityValue(f.FieldType, rarityName);
                f.SetValue(trait, val);
                return;
            }

            var p = t.GetProperty("rarity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                var val = ParseRarityValue(p.PropertyType, rarityName);
                p.SetValue(trait, val, null);
                return;
            }

            throw new MissingMemberException("ActorTrait 上未找到 rarity 字段或属性。");
        }

        public static void Init()
        {
            // ====== 道主特质 ======
            var daozhu = CreateTrait("daozhu", "trait/daozhu", "daozhu_group");
            SetRarityCompat(daozhu, "legendary");

            SafeSetStat(daozhu.base_stats, strings.S.health, 50000f); // 生命上限 +5000：高阈值，持久作战更稳
            SafeSetStat(daozhu.base_stats, strings.S.damage, 500f);  // 伤害 +500：稳定高输出
            SafeSetStat(daozhu.base_stats, strings.S.armor, 25f);  // 护甲 +25：减伤更强
            SafeSetStat(daozhu.base_stats, strings.S.critical_chance, 0.15f); // 暴击率 +15%：中等爆发
            SafeSetStat(daozhu.base_stats, strings.S.speed, 10f);  // 移速 +10：机动/拉扯
            SafeSetStat(daozhu.base_stats, strings.S.stamina, 500f);  // 体力 +500：续航稳定
            SafeSetStat(daozhu.base_stats, strings.S.lifespan, 500f);  // 寿命 +500：长寿（若系统生效）


            ForbidManualGive(daozhu);
            AssetManager.traits.add(daozhu);

            // ====== 飞升恶魔特质 ======
            var ascended_demon = CreateTrait("ascended_demon", "trait/ascended_demon", "ascended_demon_group");
            SetRarityCompat(ascended_demon, "legendary");

            SafeSetStat(ascended_demon.base_stats, strings.S.health, 38000f); // 生命 +3800：坦度略低于道主
            SafeSetStat(ascended_demon.base_stats, strings.S.damage, 800f); // 伤害 +800：爆发更强
            SafeSetStat(ascended_demon.base_stats, strings.S.armor, 20f); // 护甲 +20：适度减伤
            SafeSetStat(ascended_demon.base_stats, strings.S.critical_chance, 0.25f); // 暴击率 +25%：高爆发
            SafeSetStat(ascended_demon.base_stats, strings.S.speed, 15f);  // 移速 +15：追击滚雪球
            SafeSetStat(ascended_demon.base_stats, strings.S.stamina, 600f);  // 体力 +600：强续航
            SafeSetStat(ascended_demon.base_stats, strings.S.lifespan, 600f);  // 寿命 +600
            SafeSetStat(ascended_demon.base_stats, strings.S.area_of_effect, 2f);  // AOE +2：群战强（谨慎再加）

            ForbidManualGive(ascended_demon);
            AssetManager.traits.add(ascended_demon);

            // ====== 凡躯砺炼（以击杀门槛推进强度） ======

            // 10杀：入门增益（第一梯队-小幅）
            var first_blood = CreateTrait("first_blood", "trait/first_blood", "mortal_coil_group");
            SetRarityCompat(first_blood, "common");
            SafeSetStat(first_blood.base_stats, strings.S.health, 5000f);  // 小幅生命
            SafeSetStat(first_blood.base_stats, strings.S.damage, 50f);  // 小幅伤害
            SafeSetStat(first_blood.base_stats, strings.S.armor, 5f);  // 少量护甲
            SafeSetStat(first_blood.base_stats, strings.S.critical_chance, 0.03f); // 暴击 +3%
            SafeSetStat(first_blood.base_stats, strings.S.speed, 2f);  // 移速 +2
            SafeSetStat(first_blood.base_stats, strings.S.stamina, 100f); // 体力 +100
            SafeSetStat(first_blood.base_stats, strings.S.lifespan, 50f); // 寿命 +50
            ForbidManualGive(first_blood);
            AssetManager.traits.add(first_blood);

            // 100杀：进阶增益（介于第一/第二梯队）
            var hundred_souls = CreateTrait("hundred_souls", "trait/hundred_souls", "mortal_coil_group");
            SetRarityCompat(hundred_souls, "uncommon");
            SafeSetStat(hundred_souls.base_stats, strings.S.health, 20000f);
            SafeSetStat(hundred_souls.base_stats, strings.S.damage, 200f);
            SafeSetStat(hundred_souls.base_stats, strings.S.armor, 12f);
            SafeSetStat(hundred_souls.base_stats, strings.S.critical_chance, 0.10f);
            SafeSetStat(hundred_souls.base_stats, strings.S.speed, 6f);
            SafeSetStat(hundred_souls.base_stats, strings.S.stamina, 250f);
            SafeSetStat(hundred_souls.base_stats, strings.S.lifespan, 200f);
            ForbidManualGive(hundred_souls);
            AssetManager.traits.add(hundred_souls);

            // 1000杀：强力增益（第三梯队）
            var thousand_kill = CreateTrait("thousand_kill", "trait/thousand_kill", "mortal_coil_group");
            SetRarityCompat(thousand_kill, "legendary");
            SafeSetStat(thousand_kill.base_stats, strings.S.health, 50000f);
            SafeSetStat(thousand_kill.base_stats, strings.S.damage, 800f);
            SafeSetStat(thousand_kill.base_stats, strings.S.armor, 35f);
            SafeSetStat(thousand_kill.base_stats, strings.S.critical_chance, 0.20f);
            SafeSetStat(thousand_kill.base_stats, strings.S.speed, 12f);
            SafeSetStat(thousand_kill.base_stats, strings.S.stamina, 700f);
            SafeSetStat(thousand_kill.base_stats, strings.S.lifespan, 700f);
            SafeSetStat(thousand_kill.base_stats, strings.S.area_of_effect, 1f); // 适度群攻
            ForbidManualGive(thousand_kill);
            AssetManager.traits.add(thousand_kill);


            // ====== 天命昭示（选中&以战成道） ======

            // 被天命选中：小~中幅底子（第一梯队偏上）
            var chosen_of_providence = CreateTrait("chosen_of_providence", "trait/chosen_of_providence", "destiny_group");
            SetRarityCompat(chosen_of_providence, "legendary");
            SafeSetStat(chosen_of_providence.base_stats, strings.S.health, 1000f);
            SafeSetStat(chosen_of_providence.base_stats, strings.S.damage, 100f);
            SafeSetStat(chosen_of_providence.base_stats, strings.S.armor, 8f);
            SafeSetStat(chosen_of_providence.base_stats, strings.S.critical_chance, 0.05f);
            SafeSetStat(chosen_of_providence.base_stats, strings.S.speed, 5f);
            SafeSetStat(chosen_of_providence.base_stats, strings.S.stamina, 150f);
            SafeSetStat(chosen_of_providence.base_stats, strings.S.lifespan, 100f);
            ForbidManualGive(chosen_of_providence);
            AssetManager.traits.add(chosen_of_providence);

            // 深渊低语：很小的通用加成（第一梯队-轻量；区域批量发时避免过猛）
            var whispers_from_the_abyss = CreateTrait("whispers_from_the_abyss", "trait/whispers_from_the_abyss", "destiny_group");
            SetRarityCompat(whispers_from_the_abyss, "legendary");
            SafeSetStat(whispers_from_the_abyss.base_stats, strings.S.health, 50000f);
            SafeSetStat(whispers_from_the_abyss.base_stats, strings.S.damage, 500f);
            SafeSetStat(whispers_from_the_abyss.base_stats, strings.S.speed, 2f);
            SafeSetStat(whispers_from_the_abyss.base_stats, strings.S.stamina, 100f);
            ForbidManualGive(whispers_from_the_abyss);
            AssetManager.traits.add(whispers_from_the_abyss);

            // 以战成道：靠击杀进位的强力（第二梯队）
            var path_of_battle = CreateTrait("path_of_battle", "trait/path_of_battle", "destiny_group");
            SetRarityCompat(path_of_battle, "legendary");
            SafeSetStat(path_of_battle.base_stats, strings.S.health, 40000f);
            SafeSetStat(path_of_battle.base_stats, strings.S.damage, 400f);
            SafeSetStat(path_of_battle.base_stats, strings.S.armor, 20f);
            SafeSetStat(path_of_battle.base_stats, strings.S.critical_chance, 0.15f);
            SafeSetStat(path_of_battle.base_stats, strings.S.speed, 10f);
            SafeSetStat(path_of_battle.base_stats, strings.S.stamina, 400f);
            SafeSetStat(path_of_battle.base_stats, strings.S.lifespan, 400f);
            ForbidManualGive(path_of_battle);
            AssetManager.traits.add(path_of_battle);


            // ====== 非人之境（更高稀有 => 更高强度） ======

            // 不朽：偏防御与寿命（第三梯队）
            var ageless = CreateTrait("ageless", "trait/ageless", "beyond_mortality_group");
            SetRarityCompat(ageless, "legendary");
            SafeSetStat(ageless.base_stats, strings.S.health, 60000f);
            SafeSetStat(ageless.base_stats, strings.S.damage, 200f);
            SafeSetStat(ageless.base_stats, strings.S.armor, 40f);
            SafeSetStat(ageless.base_stats, strings.S.speed, 5f);
            SafeSetStat(ageless.base_stats, strings.S.stamina, 400f);
            SafeSetStat(ageless.base_stats, strings.S.lifespan, 2000f); // 超长寿命向
            ForbidManualGive(ageless);
            AssetManager.traits.add(ageless);

            // 肉身成圣：极稀有高阈值（第四梯队）
            var flesh_of_the_divine = CreateTrait("flesh_of_the_divine", "trait/flesh_of_the_divine", "beyond_mortality_group");
            SetRarityCompat(flesh_of_the_divine, "legendary");
            SafeSetStat(flesh_of_the_divine.base_stats, strings.S.health, 120000f);
            SafeSetStat(flesh_of_the_divine.base_stats, strings.S.damage, 1200f);
            SafeSetStat(flesh_of_the_divine.base_stats, strings.S.armor, 50f);
            SafeSetStat(flesh_of_the_divine.base_stats, strings.S.critical_chance, 0.30f);
            SafeSetStat(flesh_of_the_divine.base_stats, strings.S.speed, 15f);
            SafeSetStat(flesh_of_the_divine.base_stats, strings.S.stamina, 1200f);
            SafeSetStat(flesh_of_the_divine.base_stats, strings.S.lifespan, 3000f);
            SafeSetStat(flesh_of_the_divine.base_stats, strings.S.area_of_effect, 2f);
            ForbidManualGive(flesh_of_the_divine);
            AssetManager.traits.add(flesh_of_the_divine);

            // 杀戮化身：极稀有（第四梯队，偏进攻）
            var incarnation_of_slaughter = CreateTrait("incarnation_of_slaughter", "trait/incarnation_of_slaughter", "beyond_mortality_group");
            SetRarityCompat(incarnation_of_slaughter, "legendary");
            SafeSetStat(incarnation_of_slaughter.base_stats, strings.S.health, 90000f);
            SafeSetStat(incarnation_of_slaughter.base_stats, strings.S.damage, 1500f); // 输出更猛
            SafeSetStat(incarnation_of_slaughter.base_stats, strings.S.armor, 35f);
            SafeSetStat(incarnation_of_slaughter.base_stats, strings.S.critical_chance, 0.35f);
            SafeSetStat(incarnation_of_slaughter.base_stats, strings.S.speed, 15f);
            SafeSetStat(incarnation_of_slaughter.base_stats, strings.S.stamina, 1000f);
            SafeSetStat(incarnation_of_slaughter.base_stats, strings.S.lifespan, 1000f);
            SafeSetStat(incarnation_of_slaughter.base_stats, strings.S.area_of_effect, 2f);
            ForbidManualGive(incarnation_of_slaughter);
            AssetManager.traits.add(incarnation_of_slaughter);

            // 蚀界者：顶级联动（第五梯队；强度天花板，慎重）
            var world_eater = CreateTrait("world_eater", "trait/world_eater", "beyond_mortality_group");
            SetRarityCompat(world_eater, "legendary");
            SafeSetStat(world_eater.base_stats, strings.S.health, 200000f);
            SafeSetStat(world_eater.base_stats, strings.S.damage, 2500f);
            SafeSetStat(world_eater.base_stats, strings.S.armor, 60f);
            SafeSetStat(world_eater.base_stats, strings.S.critical_chance, 0.40f);
            SafeSetStat(world_eater.base_stats, strings.S.speed, 18f);
            SafeSetStat(world_eater.base_stats, strings.S.stamina, 1500f);
            SafeSetStat(world_eater.base_stats, strings.S.lifespan, 5000f);
            SafeSetStat(world_eater.base_stats, strings.S.area_of_effect, 3f); // 很容易失衡，慎重再加
            ForbidManualGive(world_eater);
            AssetManager.traits.add(world_eater);


            // ====== 终焉归途（标记类为主，数值慎重） ======

            // 飞升标记：为避免与“道主/飞升恶魔”叠爆，默认不给数值（你要也可加）
            var ascended_one = CreateTrait("ascended_one", "trait/ascended_one", "end_path_group");
            SetRarityCompat(ascended_one, "legendary");
            SafeSetStat(ascended_one.base_stats, strings.S.health, 10000f); // 生命 +3800：坦度略低于道主
            SafeSetStat(ascended_one.base_stats, strings.S.damage, 300f); // 伤害 +800：爆发更强
            SafeSetStat(ascended_one.base_stats, strings.S.armor, 20f); // 护甲 +20：适度减伤
            SafeSetStat(ascended_one.base_stats, strings.S.stamina, 300f);  // 体力 +600：强续航
            SafeSetStat(ascended_one.base_stats, strings.S.lifespan, 300f);  // 寿命 +600

            ForbidManualGive(ascended_one);
            AssetManager.traits.add(ascended_one);

            // 墮落飞升：一般不加；你也可给惩罚或微调
            var fallen_ascension = CreateTrait("fallen_ascension", "trait/fallen_ascension", "end_path_group");
            SetRarityCompat(fallen_ascension, "legendary");
            // （留空）
            SafeSetStat(fallen_ascension.base_stats, strings.S.health, 5000f); // 生命 +3800：坦度略低于道主
            SafeSetStat(fallen_ascension.base_stats, strings.S.damage, 600f); // 伤害 +800：爆发更强
            SafeSetStat(fallen_ascension.base_stats, strings.S.stamina, 300f);  // 体力 +600：强续航
            SafeSetStat(fallen_ascension.base_stats, strings.S.lifespan, 300f);  // 寿命 +600
            ForbidManualGive(fallen_ascension);
            AssetManager.traits.add(fallen_ascension);

            // 屠神者：极稀有（第四梯队；靠击杀飞升者）
            var godslayer = CreateTrait("godslayer", "trait/godslayer", "end_path_group");
            SetRarityCompat(godslayer, "legendary");
            SafeSetStat(godslayer.base_stats, strings.S.health, 9000f);
            SafeSetStat(godslayer.base_stats, strings.S.damage, 1200f);
            SafeSetStat(godslayer.base_stats, strings.S.armor, 45f);
            SafeSetStat(godslayer.base_stats, strings.S.critical_chance, 0.30f);
            SafeSetStat(godslayer.base_stats, strings.S.speed, 12f);
            SafeSetStat(godslayer.base_stats, strings.S.stamina, 900f);
            SafeSetStat(godslayer.base_stats, strings.S.lifespan, 1000f);
            ForbidManualGive(godslayer);
            AssetManager.traits.add(godslayer);

            // 永恒传奇：全图第一但非飞升（第三~三点五梯队）
            var eternal_legend = CreateTrait("eternal_legend", "trait/eternal_legend", "end_path_group");
            SetRarityCompat(eternal_legend, "legendary");
            SafeSetStat(eternal_legend.base_stats, strings.S.health, 9999f);
            SafeSetStat(eternal_legend.base_stats, strings.S.damage, 999f);
            SafeSetStat(eternal_legend.base_stats, strings.S.armor, 9f);
            SafeSetStat(eternal_legend.base_stats, strings.S.critical_chance, 0.09f);
            SafeSetStat(eternal_legend.base_stats, strings.S.speed, 9f);
            SafeSetStat(eternal_legend.base_stats, strings.S.stamina, 999f);
            SafeSetStat(eternal_legend.base_stats, strings.S.lifespan, 999f);
            ForbidManualGive(eternal_legend);
            AssetManager.traits.add(eternal_legend);

            // ====== 轻量“肉鸽”特质（出生低概率自带；强度克制） ======

            // 统一说明：
            // EnableRandomGain(trait, rateBirth, rateGrow, allowMutationBox)
            // - rateBirth：出生概率（单位依版本不同，建议先用 1~3 测试，感觉太低就往上加）
            // - rateGrow：成长获得概率（我们这里设为 0，确保只有出生时才带上）
            // - allowMutationBox：是否允许突变盒获取（这里默认 false）
            //
            // 这些特质全部 ForbidManualGive()，避免在 UI 手动添加；定位是“随机轻量种子”。

            // A. 击杀额外 +1 击杀数（需要在击杀回调里配合几行代码，见下方 Patch）
            //   − 没有数值面板属性，只是一个“规则标签”

            // A. 战场收割者（每次击杀时，额外 +1 击杀数；无面板数值改动）
            var tKillPlusOne = CreateTrait("rogue_kill_plus_one", "trait/daozhu", "rogue_light_group");
            SetRarityCompat(tKillPlusOne, "uncommon");

       
            AssetManager.traits.add(tKillPlusOne);

            // B. 幸运劫掠（击杀时 1% 几率额外 +10 击杀数；无面板数值改动）
            var tLucky10 = CreateTrait("rogue_kill_lucky10", "trait/daozhu", "rogue_light_group");
            SetRarityCompat(tLucky10, "uncommon");

  
            AssetManager.traits.add(tLucky10);

            // C. 起步加成（攻击 +30，生命 +500）
            var tStarter = CreateTrait("rogue_starter_boost", "trait/daozhu", "rogue_light_group");
            SetRarityCompat(tStarter, "uncommon");
            SafeSetStat(tStarter.base_stats, strings.S.damage, 60f);   // 低额外伤害
            SafeSetStat(tStarter.base_stats, strings.S.health, 1500f);  // 低额外生命
     
            AssetManager.traits.add(tStarter);

            // D. 迅捷（移动速度 +3）
            var tSwift = CreateTrait("rogue_swift", "trait/daozhu", "rogue_light_group");
            SetRarityCompat(tSwift, "uncommon");
            SafeSetStat(tSwift.base_stats, strings.S.speed, 15f); // 机动小提升

            AssetManager.traits.add(tSwift);

            // E. 灵锐（暴击率 +3%）
            var tKeen = CreateTrait("rogue_keen", "trait/daozhu", "rogue_light_group");
            SetRarityCompat(tKeen, "uncommon");
            SafeSetStat(tKeen.base_stats, strings.S.critical_chance, 0.3f); // 3% 暴击

            AssetManager.traits.add(tKeen);

            // F. 坚毅（护甲 +8）
            var tTough = CreateTrait("rogue_tough", "trait/daozhu", "rogue_light_group");
            SetRarityCompat(tTough, "uncommon");
            SafeSetStat(tTough.base_stats, strings.S.armor, 20f); // 小护甲

            AssetManager.traits.add(tTough);

            // G. 持久（体力 +150）
            var tEnduring = CreateTrait("rogue_enduring", "trait/daozhu", "rogue_light_group");
            SetRarityCompat(tEnduring, "uncommon");
            SafeSetStat(tEnduring.base_stats, strings.S.stamina, 150f); // 续航小提升

            AssetManager.traits.add(tEnduring);

            // H. 长寿（寿命 +200）
            var tLonglife = CreateTrait("rogue_longlife", "trait/daozhu", "rogue_light_group");
            SetRarityCompat(tLonglife, "uncommon");
            SafeSetStat(tLonglife.base_stats, strings.S.lifespan, 200f); // 有老化系统时起效

            AssetManager.traits.add(tLonglife);

            // I. 轻斩（伤害 +80）
            var tLightstrike = CreateTrait("rogue_lightstrike", "trait/daozhu", "rogue_light_group");
            SetRarityCompat(tLightstrike, "uncommon");
            SafeSetStat(tLightstrike.base_stats, strings.S.damage, 150f); // 稍强于 C，但仍属轻量

            AssetManager.traits.add(tLightstrike);

            // J. 厚实（生命 +1000）
            var tGuarded = CreateTrait("rogue_guarded", "trait/daozhu", "rogue_light_group");
            SetRarityCompat(tGuarded, "uncommon");
            SafeSetStat(tGuarded.base_stats, strings.S.health, 5000f); // 小坦克向

            AssetManager.traits.add(tGuarded);

            // K. 击杀回春（击杀后恢复少量生命；逻辑在击杀回调里）
            var tHealOnKill = CreateTrait("rogue_heal_on_kill", "trait/daozhu", "rogue_light_group");
            SetRarityCompat(tHealOnKill, "uncommon");

            AssetManager.traits.add(tHealOnKill);




        }
    }
}
