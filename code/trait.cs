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
        // 思路同“madness”：can_be_given = false。只影响手动添加，不影响代码 addTrait。
        private static void ForbidManualGive(ActorTrait t)
        {
            // 禁止在角色编辑器里被手动点选
            t.can_be_given = false;  // 关键字段：和 madness 一样的锁法（见反编译）
            // 可选：不允许在“变异盒/突变”出现（如果你也想禁掉该来源就开）
            // t.is_mutation_box_allowed = false;

            // —— 保险后路（可选）：把它标成“成就解锁型”，不给成就就不会出现在“已解锁”区 —— 
            // 这一步用于某些 UI 版本仍然把 can_be_given=false 的条目列出来的情况
            // 不想用就注释掉
            // t.unlocked_with_achievement = true;
            // t.achievement_id = "_mod_lock_only_";
        }

        // —— 规则2：允许随机获得（出生/成长概率），与手动添加解耦 —— 
        // 只负责把它们放进随机池；是否出现在“突变盒”由 is_mutation_box_allowed 控制
        private static void EnableRandomGain(ActorTrait t, int rateBirth = 0, int rateGrow = 0, bool allowMutationBox = false)
        {
            // 出生时的随机权重（等价于 ActorTrait.rate_birth）
            t.rate_birth = rateBirth;

            // 成长时的随机权重（等价于 ActorTrait.rate_acquire_grow_up）
            t.rate_acquire_grow_up = rateGrow;

            // 是否允许在“变异盒/突变”系统出现
            t.is_mutation_box_allowed = allowMutationBox;

            // 说明：游戏在链接资源时会把有 rate_* 的特质塞入 pot_traits_birth / pot_traits_growup 池
            // 见反编译的 linkAssets() 和随机分配逻辑（把 rate_* 的 trait 加入池再随机抽取）。
            // 也就是说：只要设了 rate，就能随机获得，不依赖 can_be_given。手点是另一条路。
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

            
            ForbidManualGive(daozhu);                  // ← 一定要在 add() 之前设！
            // 禁止手动加，但允许随机掉落（举例：出生权重3，成长权重1，不进突变盒）
            //EnableRandomGain(daozhu, rateBirth: 3, rateGrow: 1, allowMutationBox: false);

            AssetManager.traits.add(daozhu);

            // ====== 飞升恶魔特质 ======
            var ascended_demon = CreateTrait("ascended_demon", "trait/ascended_demon", "ascended_demon_group");
            ascended_demon.rarity = Rarity.R3_Legendary;

            SafeSetStat(ascended_demon.base_stats, strings.S.health, 300f);           // 生命值 +300
            SafeSetStat(ascended_demon.base_stats, strings.S.damage, 100f);           // 攻击力 +100
            SafeSetStat(ascended_demon.base_stats, strings.S.armor, 15f);             // 防御力 +15
            SafeSetStat(ascended_demon.base_stats, strings.S.critical_chance, 0.25f); // 暴击率 +25%
            SafeSetStat(ascended_demon.base_stats, strings.S.speed, 15f);             // 移动速度 +15
            SafeSetStat(ascended_demon.base_stats, strings.S.stamina, 150f);          // 耐力 +150
            SafeSetStat(ascended_demon.base_stats, strings.S.lifespan, 200f);         // 寿命 +200年
            SafeSetStat(ascended_demon.base_stats, strings.S.area_of_effect, 2f);     // 攻击范围 +2

            // 这个我们弄成只禁手动、不随机（纯剧情或任务奖励用）
            ForbidManualGive(ascended_demon);
            // 不调用 EnableRandomGain => 不会进随机池，但脚本 addTrait 仍然可用

            AssetManager.traits.add(ascended_demon);
        }
    }
}
