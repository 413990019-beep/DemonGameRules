using System;
using System.Collections.Generic;
using ReflectionUtility;
using strings;

namespace DemonGameRules.code
{
    internal class traits
    {
        // —— 小工具：创建一个特质对象，默认不设数值 —— 
        private static ActorTrait CreateTrait(string id, string path_icon, string group_id)
        {
            var trait = new ActorTrait
            {
                id = id,                 // 特质 ID（本地化键：trait_<id>）
                path_icon = path_icon,   // 图标路径
                group_id = group_id,     // 所属特质组
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
            t.can_be_given = false;  // 锁住手动添加
            // t.is_mutation_box_allowed = false; // 如果也想禁止突变盒，放开这行
            // t.unlocked_with_achievement = true; t.achievement_id = "_mod_lock_only_"; // 某些版本 UI 保险
        }

        // —— 规则2：允许随机获得（出生/成长概率），与手动添加解耦 —— 
        private static void EnableRandomGain(ActorTrait t, int rateBirth = 0, int rateGrow = 0, bool allowMutationBox = false)
        {
            t.rate_birth = rateBirth;
            t.rate_acquire_grow_up = rateGrow;
            t.is_mutation_box_allowed = allowMutationBox;
        }

        public static void Init()
        {
            // ====== 道主特质 ======
            var daozhu = CreateTrait("daozhu", "trait/daozhu", "daozhu_group");
            daozhu.rarity = Rarity.R3_Legendary;

            // 数值
            SafeSetStat(daozhu.base_stats, strings.S.health, 500f);             // 生命值 +500
            SafeSetStat(daozhu.base_stats, strings.S.damage, 50f);              // 攻击力 +50
            SafeSetStat(daozhu.base_stats, strings.S.armor, 25f);               // 防御力 +25
            SafeSetStat(daozhu.base_stats, strings.S.critical_chance, 0.15f);   // 暴击率 +15%
            SafeSetStat(daozhu.base_stats, strings.S.speed, 10f);               // 移动速度 +10
            SafeSetStat(daozhu.base_stats, strings.S.stamina, 100f);            // 耐力 +100
            SafeSetStat(daozhu.base_stats, strings.S.lifespan, 100f);           // 寿命 +100年

            ForbidManualGive(daozhu);
            // EnableRandomGain(daozhu, rateBirth: 3, rateGrow: 1, allowMutationBox: false);
            AssetManager.traits.add(daozhu);

            // ====== 飞升恶魔特质 ======
            var ascended_demon = CreateTrait("ascended_demon", "trait/ascended_demon", "ascended_demon_group");
            ascended_demon.rarity = Rarity.R3_Legendary;

            SafeSetStat(ascended_demon.base_stats, strings.S.health, 300f);
            SafeSetStat(ascended_demon.base_stats, strings.S.damage, 100f);
            SafeSetStat(ascended_demon.base_stats, strings.S.armor, 15f);
            SafeSetStat(ascended_demon.base_stats, strings.S.critical_chance, 0.25f);
            SafeSetStat(ascended_demon.base_stats, strings.S.speed, 15f);
            SafeSetStat(ascended_demon.base_stats, strings.S.stamina, 150f);
            SafeSetStat(ascended_demon.base_stats, strings.S.lifespan, 200f);
            SafeSetStat(ascended_demon.base_stats, strings.S.area_of_effect, 2f);

            ForbidManualGive(ascended_demon);
            AssetManager.traits.add(ascended_demon);

            // ====== 凡躯砺炼 ======
            var first_blood = CreateTrait("first_blood", "trait/first_blood", "mortal_coil_group");
            first_blood.rarity = Rarity.R2_Epic;         // 原来写 common，改成别人那套前缀
            ForbidManualGive(first_blood);
            AssetManager.traits.add(first_blood);

            var hundred_souls = CreateTrait("hundred_souls", "trait/hundred_souls", "mortal_coil_group");
            hundred_souls.rarity = Rarity.R2_Epic;       // 原来写 uncommon，统一改
            ForbidManualGive(hundred_souls);
            AssetManager.traits.add(hundred_souls);

            var thousand_kill = CreateTrait("thousand_kill", "trait/thousand_kill", "mortal_coil_group");
            thousand_kill.rarity = Rarity.R3_Legendary;
            ForbidManualGive(thousand_kill);
            AssetManager.traits.add(thousand_kill);

            // ====== 天命昭示 ======
            var chosen_of_providence = CreateTrait("chosen_of_providence", "trait/chosen_of_providence", "destiny_group");
            chosen_of_providence.rarity = Rarity.R3_Legendary;
            ForbidManualGive(chosen_of_providence);
            AssetManager.traits.add(chosen_of_providence);

            var whispers_from_the_abyss = CreateTrait("whispers_from_the_abyss", "trait/whispers_from_the_abyss", "destiny_group");
            whispers_from_the_abyss.rarity = Rarity.R3_Legendary;
            ForbidManualGive(whispers_from_the_abyss);
            AssetManager.traits.add(whispers_from_the_abyss);

            var path_of_battle = CreateTrait("path_of_battle", "trait/path_of_battle", "destiny_group");
            path_of_battle.rarity = Rarity.R3_Legendary;
            ForbidManualGive(path_of_battle);
            AssetManager.traits.add(path_of_battle);

            // ====== 非人之境 ======
            var ageless = CreateTrait("ageless", "trait/ageless", "beyond_mortality_group");
            ageless.rarity = Rarity.R3_Legendary;
            ForbidManualGive(ageless);
            AssetManager.traits.add(ageless);

            var flesh_of_the_divine = CreateTrait("flesh_of_the_divine", "trait/flesh_of_the_divine", "beyond_mortality_group");
            flesh_of_the_divine.rarity = Rarity.R3_Legendary;
            ForbidManualGive(flesh_of_the_divine);
            AssetManager.traits.add(flesh_of_the_divine);

            var incarnation_of_slaughter = CreateTrait("incarnation_of_slaughter", "trait/incarnation_of_slaughter", "beyond_mortality_group");
            incarnation_of_slaughter.rarity = Rarity.R3_Legendary;
            ForbidManualGive(incarnation_of_slaughter);
            AssetManager.traits.add(incarnation_of_slaughter);

            var world_eater = CreateTrait("world_eater", "trait/world_eater", "beyond_mortality_group");
            world_eater.rarity = Rarity.R3_Legendary;
            ForbidManualGive(world_eater);
            AssetManager.traits.add(world_eater);

            // ====== 终焉归途 ======
            var ascended_one = CreateTrait("ascended_one", "trait/ascended_one", "end_path_group");
            ascended_one.rarity = Rarity.R3_Legendary;
            ForbidManualGive(ascended_one);
            AssetManager.traits.add(ascended_one);

            var fallen_ascension = CreateTrait("fallen_ascension", "trait/fallen_ascension", "end_path_group");
            fallen_ascension.rarity = Rarity.R3_Legendary;
            ForbidManualGive(fallen_ascension);
            AssetManager.traits.add(fallen_ascension);

            var godslayer = CreateTrait("godslayer", "trait/godslayer", "end_path_group");
            godslayer.rarity = Rarity.R3_Legendary;
            ForbidManualGive(godslayer);
            AssetManager.traits.add(godslayer);

            var eternal_legend = CreateTrait("eternal_legend", "trait/eternal_legend", "end_path_group");
            eternal_legend.rarity = Rarity.R3_Legendary;
            ForbidManualGive(eternal_legend);
            AssetManager.traits.add(eternal_legend);
        }
    }
}
