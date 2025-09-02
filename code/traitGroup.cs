using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DemonGameRules.code
{
    internal class traitGroup
    {
        // 初始化：注册自定义特质组
        public static void Init()
        {
            // ====== 道主特质组 ======
            ActorTraitGroupAsset daozhuGroup = new ActorTraitGroupAsset();
            daozhuGroup.id = "daozhu_group";           // 组 ID
            daozhuGroup.name = "DAOZHU_GROUP";         // 本地化键
            daozhuGroup.color = "#FFD700";             // 金色
            AssetManager.trait_groups.add(daozhuGroup);

            // ====== 飞升恶魔特质组 ======
            ActorTraitGroupAsset demonGroup = new ActorTraitGroupAsset();
            demonGroup.id = "ascended_demon_group";    // 组 ID
            demonGroup.name = "ASCENDED_DEMON_GROUP";  // 本地化键
            demonGroup.color = "#8B0000";              // 暗红色
            AssetManager.trait_groups.add(demonGroup);
        }
    }
}
