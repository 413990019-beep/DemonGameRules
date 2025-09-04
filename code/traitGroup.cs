using System;
using System.Reflection;

namespace DemonGameRules.code
{
    internal static class traitGroup
    {
        private static bool _inited;

        // 入口：注册自定义特质组（幂等）
        public static void Init()
        {
            if (_inited) return;
            _inited = true;
            // ★ 新增：轻量肉鸽分组（你给的 10 个轻量特质都挂在这组）
            AddOrUpdateGroup("rogue_light_group", "ROGUE_LIGHT_GROUP", "#2ECC71");    // 翠绿
            
            AddOrUpdateGroup("mortal_coil_group", "MORTAL_COIL_GROUP", "#CD7F32"); // 古铜
            AddOrUpdateGroup("destiny_group", "DESTINY_GROUP", "#4169E1"); // 宝蓝
            //AddOrUpdateGroup("beyond_mortality_group", "BEYOND_MORTALITY_GROUP", "#4B0082"); // 靛蓝
            AddOrUpdateGroup("end_path_group", "END_PATH_GROUP", "#FF4500"); // 橙红
            //AddOrUpdateGroup("daozhu_group", "DAOZHU_GROUP", "#FFD700"); // 金色
            //AddOrUpdateGroup("ascended_demon_group", "ASCENDED_DEMON_GROUP", "#8B0000"); // 暗红

        }

        // 已存在则更新，不存在则创建并添加
        private static void AddOrUpdateGroup(string id, string nameKey, string hex)
        {
            // 读库里是否已有同 id 的组
            ActorTraitGroupAsset group = AssetManager.trait_groups.get(id);
            if (group == null)
            {
                group = new ActorTraitGroupAsset();
                group.id = id;
                group.name = nameKey;
                SetColorCompat(group, hex);
                AssetManager.trait_groups.add(group);
                return;
            }

            // 已存在则只更新显示名与颜色，避免重复 add 报错
            group.name = nameKey;
            SetColorCompat(group, hex);
        }

        // 兼容设置 color：支持 string / UnityEngine.Color / UnityEngine.Color32
        private static void SetColorCompat(ActorTraitGroupAsset group, string hex)
        {
            var t = typeof(ActorTraitGroupAsset);
            var f = t.GetField("color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var p = t.GetProperty("color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (f != null)
            {
                var v = ConvertColorValue(f.FieldType, hex);
                if (v != null) f.SetValue(group, v);
                return;
            }
            if (p != null)
            {
                var v = ConvertColorValue(p.PropertyType, hex);
                if (v != null) p.SetValue(group, v, null);
            }
        }

        // 把 #RRGGBB 或 #RRGGBBAA 转为目标类型实例
        private static object ConvertColorValue(Type targetType, string hex)
        {
            if (targetType == typeof(string))
                return NormalizeHex(hex);

            // 运行期反射 Unity 类型，避免编译期强依赖
            var colorType = Type.GetType("UnityEngine.Color, UnityEngine");
            var color32Type = Type.GetType("UnityEngine.Color32, UnityEngine");

            (byte r, byte g, byte b, byte a) = ParseHex(NormalizeHex(hex));

            if (color32Type != null && targetType == color32Type)
            {
                // Color32(byte r, byte g, byte b, byte a)
                return Activator.CreateInstance(color32Type, new object[] { r, g, b, a });
            }
            if (colorType != null && targetType == colorType)
            {
                // Color(float r, float g, float b, float a)
                float rf = r / 255f, gf = g / 255f, bf = b / 255f, af = a / 255f;
                return Activator.CreateInstance(colorType, new object[] { rf, gf, bf, af });
            }

            // 其他奇怪类型就不瞎配了
            return null;
        }

        private static string NormalizeHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return "#FFFFFFFF";
            string s = hex.Trim().TrimStart('#');
            if (s.Length == 6) s += "FF";
            if (s.Length != 8) s = "FFFFFFFF";
            return "#" + s.ToUpperInvariant();
        }

        private static (byte r, byte g, byte b, byte a) ParseHex(string normalized)
        {
            string s = normalized.TrimStart('#');
            try
            {
                byte r = Convert.ToByte(s.Substring(0, 2), 16);
                byte g = Convert.ToByte(s.Substring(2, 2), 16);
                byte b = Convert.ToByte(s.Substring(4, 2), 16);
                byte a = Convert.ToByte(s.Substring(6, 2), 16);
                return (r, g, b, a);
            }
            catch
            {
                return (255, 255, 255, 255);
            }
        }
    }
}
