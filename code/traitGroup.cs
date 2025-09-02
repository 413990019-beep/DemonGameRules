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

            // ====== 成就篇章特质组 ======
            ActorTraitGroupAsset mortalCoilGroup = new ActorTraitGroupAsset();
            mortalCoilGroup.id = "mortal_coil_group";
            mortalCoilGroup.name = "MORTAL_COIL_GROUP";
            mortalCoilGroup.color = "#CD7F32"; // bronze
            AssetManager.trait_groups.add(mortalCoilGroup);

            ActorTraitGroupAsset destinyGroup = new ActorTraitGroupAsset();
            destinyGroup.id = "destiny_group";
            destinyGroup.name = "DESTINY_GROUP";
            destinyGroup.color = "#4169E1"; // royal blue
            AssetManager.trait_groups.add(destinyGroup);

            ActorTraitGroupAsset beyondMortalityGroup = new ActorTraitGroupAsset();
            beyondMortalityGroup.id = "beyond_mortality_group";
            beyondMortalityGroup.name = "BEYOND_MORTALITY_GROUP";
            beyondMortalityGroup.color = "#4B0082"; // indigo
            AssetManager.trait_groups.add(beyondMortalityGroup);

            ActorTraitGroupAsset endPathGroup = new ActorTraitGroupAsset();
            endPathGroup.id = "end_path_group";
            endPathGroup.name = "END_PATH_GROUP";
            endPathGroup.color = "#FF4500"; // orange red
            AssetManager.trait_groups.add(endPathGroup);
        }
    }
}
