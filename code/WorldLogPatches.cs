// WorldLogPatches.cs
// 仅负责“世界消息”相关补丁。参数一律用 __0/__1，杜绝参数名不匹配。
// 需要：HarmonyLib 引用可用；DemonGameRules2.code.traitAction.WriteWorldEventSilently 存在。

using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;

namespace DemonGameRules.code
{
    #region 工具与去重

    internal static class WorldLogPatchUtil
    {
        private static readonly Regex RxTags = new Regex("<.*?>", RegexOptions.Compiled);

        public static string Clean(string s) => string.IsNullOrEmpty(s) ? s : RxTags.Replace(s, string.Empty).Trim();
        public static int YearNow() => DemonGameRules2.code.traitAction.YearNow();
        public static string Stamp(string tag, string body) => $"[世界历{YearNow()}年] [{tag}] {body}\n";

        public static string K(Kingdom k) => Clean(k?.name) ?? "未知王国";
        public static string C(City c) => Clean(c?.name) ?? "未知城市";
        public static string U(Actor a) => Clean(a?.data?.name) ?? "未知单位";

        public static void Write(string line)
        {
            try { DemonGameRules2.code.traitAction.WriteWorldEventSilently(line); }
            catch { /* 静默 */ }
        }
    }

    /// <summary>
    /// 日志卫兵：对“同一对国家”的“宣战/停战”在一个时间窗口内做去重节流。
    /// </summary>
    internal static class WorldEventGuard
    {
        // 60 秒窗口内，同一事件不再重复写入
        private const float WindowSeconds = 60f;

        // key: $"start:{year}:{minId}-{maxId}" 或 $"end:{year}:{minId}-{maxId}"
        private static readonly ConcurrentDictionary<string, float> _lastStamp = new();

        private static string PairKey(string tag, Kingdom a, Kingdom b, int year)
        {
            if (a == null || b == null) return null;
            long idA = a.id;
            long idB = b.id;
            if (idA <= 0 || idB <= 0) return null;
            if (idA > idB) { var t = idA; idA = idB; idB = t; }
            return $"{tag}:{year}:{idA}-{idB}";
        }

        private static float Now()
        {
            var w = World.world;
            if (w == null) return 0f;
            // getCurWorldTime() 是 double，这里手动转成 float 再乘
            return (float)w.getCurWorldTime() * 60f;
            // 或者：return (float)(w.getCurWorldTime() * 60.0);
        }


        public static bool ShouldLogWarStart(Kingdom a, Kingdom b, int year)
        {
            var key = PairKey("start", a, b, year);
            if (key == null) return false;
            var now = Now();
            if (_lastStamp.TryGetValue(key, out var t) && now - t < WindowSeconds) return false;
            _lastStamp[key] = now;
            return true;
        }

        public static bool ShouldLogWarEnd(Kingdom a, Kingdom b, int year)
        {
            var key = PairKey("end", a, b, year);
            if (key == null) return false;
            var now = Now();
            if (_lastStamp.TryGetValue(key, out var t) && now - t < WindowSeconds) return false;
            _lastStamp[key] = now;
            return true;
        }
    }

    #endregion

    #region 显式入口：建城/毁城/叛乱/建国/亡国/宣战(WorldLog 形态)

    //// 建城
    //[HarmonyPatch]
    //internal static class WL_logNewCity_Post
    //{
    //    static bool Prepare() =>
    //        AccessTools.Method(typeof(WorldLog), nameof(WorldLog.logNewCity), new Type[] { typeof(City) }) != null;

    //    [HarmonyPostfix]
    //    [HarmonyPatch(typeof(WorldLog), nameof(WorldLog.logNewCity), new Type[] { typeof(City) })]
    //    static void Postfix(City __0)
    //    {
    //        var pCity = __0;
    //        if (pCity == null) return;
    //        WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("建城", $"{WorldLogPatchUtil.C(pCity)} @ {WorldLogPatchUtil.K(pCity.kingdom)}"));
    //    }
    //}

    //// 毁城
    //[HarmonyPatch]
    //internal static class WL_logCityDestroyed_Post
    //{
    //    static bool Prepare() =>
    //        AccessTools.Method(typeof(WorldLog), nameof(WorldLog.logCityDestroyed), new Type[] { typeof(City) }) != null;

    //    [HarmonyPostfix]
    //    [HarmonyPatch(typeof(WorldLog), nameof(WorldLog.logCityDestroyed), new Type[] { typeof(City) })]
    //    static void Postfix(City __0)
    //    {
    //        var c = __0;
    //        if (c == null) return;
    //        WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("毁城", $"{WorldLogPatchUtil.C(c)} @ {WorldLogPatchUtil.K(c.kingdom)}"));
    //    }
    //}

    // 叛乱
    [HarmonyPatch]
    internal static class WL_logCityRevolt_Post
    {
        static bool Prepare() =>
            AccessTools.Method(typeof(WorldLog), nameof(WorldLog.logCityRevolt), new Type[] { typeof(City) }) != null;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WorldLog), nameof(WorldLog.logCityRevolt), new Type[] { typeof(City) })]
        static void Postfix(City __0)
        {
            var pCity = __0;
            if (pCity == null) return;
            WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("叛乱", $"{WorldLogPatchUtil.C(pCity)} 叛离 {WorldLogPatchUtil.K(pCity.kingdom)}"));
        }
    }

    // 建国/建制
    [HarmonyPatch]
    internal static class WL_logNewKingdom_Post
    {
        static bool Prepare() =>
            AccessTools.Method(typeof(WorldLog), nameof(WorldLog.logNewKingdom), new Type[] { typeof(Kingdom) }) != null;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WorldLog), nameof(WorldLog.logNewKingdom), new Type[] { typeof(Kingdom) })]
        static void Postfix(Kingdom __0)
        {
            var k = __0;
            if (k == null) return;
            WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("建国/建制", WorldLogPatchUtil.K(k)));
        }
    }

    // 亡国
    [HarmonyPatch]
    internal static class WL_logKingdomDestroyed_Post
    {
        static bool Prepare() =>
            AccessTools.Method(typeof(WorldLog), nameof(WorldLog.logKingdomDestroyed), new Type[] { typeof(Kingdom) }) != null;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WorldLog), nameof(WorldLog.logKingdomDestroyed), new Type[] { typeof(Kingdom) })]
        static void Postfix(Kingdom __0)
        {
            var k = __0;
            if (k == null) return;
            WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("亡国", WorldLogPatchUtil.K(k)));
        }
    }

    // 宣战（如果本版本确实存在 Kingdom,Kingdom 形态）
    [HarmonyPatch]
    internal static class WL_logNewWar_Post
    {
        static bool Prepare() =>
            AccessTools.Method(typeof(WorldLog), nameof(WorldLog.logNewWar), new Type[] { typeof(Kingdom), typeof(Kingdom) }) != null;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WorldLog), nameof(WorldLog.logNewWar), new Type[] { typeof(Kingdom), typeof(Kingdom) })]
        static void Postfix(Kingdom __0, Kingdom __1)
        {
            var a = __0; var b = __1;
            if (a == null || b == null) return;
            int year = WorldLogPatchUtil.YearNow();
            if (!WorldEventGuard.ShouldLogWarStart(a, b, year)) return;
            WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("宣战", $"{WorldLogPatchUtil.K(a)} vs {WorldLogPatchUtil.K(b)}"));
        }
    }

    #endregion

    #region 自适配：WorldLog.*(War) 形态的宣战

    // 任何方法名包含 "war" 且只有一个参数、该参数类型为/继承自 War
    [HarmonyPatch]
    internal static class WL_WarLikeMethods_Post
    {
        static MethodBase[] TargetMethods()
        {
            var t = typeof(WorldLog);
            var list = new List<MethodBase>();
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                var name = m.Name.ToLower();
                if (!name.Contains("war")) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1) continue;
                if (typeof(War).IsAssignableFrom(ps[0].ParameterType))
                    list.Add(m);
            }
            return list.ToArray();
        }

        static bool Prepare() => TargetMethods().Length > 0;

        [HarmonyPostfix]
        static void Postfix(object __0)
        {
            try
            {
                if (__0 == null) return;
                var tr = Traverse.Create(__0);
                Kingdom k1 = null, k2 = null;
                try { k1 = tr.Field("kingdom_1").GetValue<Kingdom>(); } catch { }
                try { k2 = tr.Field("kingdom_2").GetValue<Kingdom>(); } catch { }
                if (k1 == null || k2 == null) return;

                int year = WorldLogPatchUtil.YearNow();
                if (!WorldEventGuard.ShouldLogWarStart(k1, k2, year)) return;

                WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("宣战", $"{WorldLogPatchUtil.K(k1)} vs {WorldLogPatchUtil.K(k2)}"));
            }
            catch { }
        }
    }

    #endregion

    #region WarManager 层：创建战争入口自适配

    // 钩住 WarManager.*，名字含 war 且含 new/start/declare，参数里出现两个 Kingdom
    [HarmonyPatch]
    internal static class WarManager_CreateWar_Post
    {
        static MethodBase[] TargetMethods()
        {
            var list = new List<MethodBase>();
            var t = AccessTools.TypeByName("WarManager");
            if (t == null) return list.ToArray();

            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                var name = m.Name.ToLower();
                if (!(name.Contains("war") && (name.Contains("new") || name.Contains("start") || name.Contains("declare"))))
                    continue;

                var ps = m.GetParameters();
                if (ps.Length < 2) continue;

                // 找到两个连续参数是 Kingdom 的
                for (int i = 0; i < ps.Length - 1; i++)
                {
                    if (ps[i].ParameterType == typeof(Kingdom) && ps[i + 1].ParameterType == typeof(Kingdom))
                    {
                        list.Add(m);
                        break;
                    }
                }
            }
            return list.ToArray();
        }

        static bool Prepare() => TargetMethods().Length > 0;

        [HarmonyPostfix]
        static void Postfix(object __0, object __1)
        {
            try
            {
                var k1 = __0 as Kingdom;
                var k2 = __1 as Kingdom;
                if (k1 == null || k2 == null) return;

                int year = WorldLogPatchUtil.YearNow();
                if (!WorldEventGuard.ShouldLogWarStart(k1, k2, year)) return;

                WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("宣战", $"{WorldLogPatchUtil.K(k1)} vs {WorldLogPatchUtil.K(k2)}"));
            }
            catch { }
        }
    }

    #endregion

    #region 结束/停战：WorldLog 与 War 实例双重兜底

    // WorldLog 侧：任何名里包含 war 且包含 end/finish/peace 的方法；
    // 兼容参数：1) War 单参；2) 两个 Kingdom；3) 其他混合形态里带 War 或带两国。
    [HarmonyPatch]
    internal static class WorldLog_WarEnd_Post
    {
        static MethodBase[] TargetMethods()
        {
            var list = new List<MethodBase>();
            var t = typeof(WorldLog);
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                var name = m.Name.ToLower();
                if (!name.Contains("war")) continue;
                if (!(name.Contains("end") || name.Contains("finish") || name.Contains("peace") || name.Contains("stop") || name.Contains("remove")))
                    continue;

                // 以前只收单参，这里放宽：任何参数个数皆可
                list.Add(m);
            }
            return list.ToArray();
        }

        static bool Prepare() => TargetMethods().Length > 0;

        [HarmonyPostfix]
        static void Postfix(params object[] __args)
        {
            try
            {
                if (__args == null || __args.Length == 0) return;

                if (!TryExtractPair(__args, out var k1, out var k2)) return;

                int year = WorldLogPatchUtil.YearNow();
                if (!WorldEventGuard.ShouldLogWarEnd(k1, k2, year)) return;

                WorldLogPatchUtil.Write(
                    WorldLogPatchUtil.Stamp("停战/战争结束", $"{WorldLogPatchUtil.K(k1)} vs {WorldLogPatchUtil.K(k2)}")
                );
            }
            catch { }
        }

        // 从参数里尽最大可能挖出双方王国
        private static bool TryExtractPair(object[] args, out Kingdom k1, out Kingdom k2)
        {
            k1 = null; k2 = null;

            // 1) 任何参数如果是 War，就从字段抠
            foreach (var a in args)
            {
                if (a is War w)
                {
                    var tr = Traverse.Create(w);
                    try { k1 ??= tr.Field("kingdom_1").GetValue<Kingdom>(); } catch { }
                    try { k2 ??= tr.Field("kingdom_2").GetValue<Kingdom>(); } catch { }
                }
            }
            if (k1 != null && k2 != null) return true;

            // 2) 若参数列表中直接出现了两个 Kingdom
            foreach (var a in args)
            {
                if (a is Kingdom kk)
                {
                    if (k1 == null) k1 = kk;
                    else if (k2 == null) { k2 = kk; break; }
                }
            }
            if (k1 != null && k2 != null) return true;

            // 3) 某些“结束消息对象”可能带字段
            foreach (var a in args)
            {
                if (a == null) continue;
                var tr = Traverse.Create(a);
                try { k1 ??= tr.Field("kingdom_1").GetValue<Kingdom>(); } catch { }
                try { k2 ??= tr.Field("kingdom_2").GetValue<Kingdom>(); } catch { }
                if (k1 == null) foreach (var n in new[] { "kingdom1", "k1", "attacker", "a", "from" }) { try { k1 = tr.Field(n).GetValue<Kingdom>(); if (k1 != null) break; } catch { } }
                if (k2 == null) foreach (var n in new[] { "kingdom2", "k2", "defender", "b", "to" }) { try { k2 = tr.Field(n).GetValue<Kingdom>(); if (k2 != null) break; } catch { } }
                if (k1 != null && k2 != null) return true;
            }

            return false;
        }
    }


    // War 实例：任何名里包含 end/finish/peace/stop/remove 的实例方法
    [HarmonyPatch]
    internal static class War_InstanceEnd_Post
    {
        static MethodBase[] TargetMethods()
        {
            var list = new List<MethodBase>();
            var t = typeof(War);
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var name = m.Name.ToLower();
                if (name.Contains("end") || name.Contains("finish") || name.Contains("peace") || name.Contains("stop") || name.Contains("remove"))
                    list.Add(m);
            }
            return list.ToArray();
        }

        static bool Prepare() => TargetMethods().Length > 0;

        [HarmonyPostfix]
        static void Postfix(War __instance)
        {
            try
            {
                if (__instance == null) return;
                var tr = Traverse.Create(__instance);
                Kingdom k1 = null, k2 = null;
                try { k1 = tr.Field("kingdom_1").GetValue<Kingdom>(); } catch { }
                try { k2 = tr.Field("kingdom_2").GetValue<Kingdom>(); } catch { }
                if (k1 == null || k2 == null) return;

                int year = WorldLogPatchUtil.YearNow();
                if (!WorldEventGuard.ShouldLogWarEnd(k1, k2, year)) return;

                WorldLogPatchUtil.Write(
                    WorldLogPatchUtil.Stamp("停战/战争结束", $"{WorldLogPatchUtil.K(k1)} vs {WorldLogPatchUtil.K(k2)}")
                );
            }
            catch { }
        }

        // WarManager 层：任何名里含 war 且含 end/finish/peace/stop/remove 的方法，
        // 参数里如果能拿到 War 或出现两个 Kingdom，就当作“战争结束/停战”
        [HarmonyPatch]
        internal static class WarManager_EndWar_Post
        {
            static MethodBase[] TargetMethods()
            {
                var list = new List<MethodBase>();
                var t = AccessTools.TypeByName("WarManager");
                if (t == null) return list.ToArray();

                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    var name = m.Name.ToLower();
                    if (!(name.Contains("war") && (name.Contains("end") || name.Contains("finish") || name.Contains("peace") || name.Contains("stop") || name.Contains("remove"))))
                        continue;

                    list.Add(m);
                }
                return list.ToArray();
            }

            static bool Prepare() => TargetMethods().Length > 0;

            [HarmonyPostfix]
            static void Postfix(params object[] __args)
            {
                try
                {
                    if (__args == null || __args.Length == 0) return;

                    if (!TryExtractPair(__args, out var k1, out var k2)) return;

                    int year = WorldLogPatchUtil.YearNow();
                    if (!WorldEventGuard.ShouldLogWarEnd(k1, k2, year)) return;

                    WorldLogPatchUtil.Write(
                        WorldLogPatchUtil.Stamp("停战/战争结束", $"{WorldLogPatchUtil.K(k1)} vs {WorldLogPatchUtil.K(k2)}")
                    );
                }
                catch { }
            }

            private static bool TryExtractPair(object[] args, out Kingdom k1, out Kingdom k2)
            {
                k1 = null; k2 = null;

                foreach (var a in args)
                {
                    if (a is War w)
                    {
                        var tr = Traverse.Create(w);
                        try { k1 ??= tr.Field("kingdom_1").GetValue<Kingdom>(); } catch { }
                        try { k2 ??= tr.Field("kingdom_2").GetValue<Kingdom>(); } catch { }
                    }
                }
                if (k1 != null && k2 != null) return true;

                foreach (var a in args)
                {
                    if (a is Kingdom kk)
                    {
                        if (k1 == null) k1 = kk;
                        else if (k2 == null) { k2 = kk; break; }
                    }
                }
                if (k1 != null && k2 != null) return true;

                foreach (var a in args)
                {
                    if (a == null) continue;
                    var tr = Traverse.Create(a);
                    try { k1 ??= tr.Field("kingdom_1").GetValue<Kingdom>(); } catch { }
                    try { k2 ??= tr.Field("kingdom_2").GetValue<Kingdom>(); } catch { }
                    if (k1 == null) foreach (var n in new[] { "kingdom1", "k1", "attacker", "a", "from" }) { try { k1 = tr.Field(n).GetValue<Kingdom>(); if (k1 != null) break; } catch { } }
                    if (k2 == null) foreach (var n in new[] { "kingdom2", "k2", "defender", "b", "to" }) { try { k2 = tr.Field(n).GetValue<Kingdom>(); if (k2 != null) break; } catch { } }
                    if (k1 != null && k2 != null) return true;
                }

                return false;
            }
        }

    }


    #endregion

    #region 兜底：只读监听 WorldLog.addMessage，不拦截，不影响 UI

    [HarmonyPatch]
    internal static class WL_addMessage_Post
    {
        // 尽量兼容：从所有重载里挑出接收 1 个参数且类型是/继承自 WorldLogMessage 的版本
        static MethodBase TargetMethod()
        {
            var t = typeof(WorldLog);
            var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var m in methods)
            {
                if (m.Name != "addMessage") continue;
                var ps = m.GetParameters();
                if (ps.Length == 1 && typeof(WorldLogMessage).IsAssignableFrom(ps[0].ParameterType))
                    return m;
            }
            return null;
        }

        static bool Prepare() => TargetMethod() != null;

        // 注意：这里用 object __0 接收，避免命名空间差异；再用 Traverse 读字段
        [HarmonyPostfix]
        static void Postfix(object __0)
        {
            try
            {
                var pMessage = __0;
                if (pMessage == null) return;

                var tr = Traverse.Create(pMessage);

                string s1 = null, s2 = null, s3 = null;
                try { s1 = (string)tr.Method("getSpecial", 1).GetValue(); } catch { }
                try { s2 = (string)tr.Method("getSpecial", 2).GetValue(); } catch { }
                try { s3 = (string)tr.Method("getSpecial", 3).GetValue(); } catch { }

                string id = null;
                try
                {
                    id = tr.Field("id").GetValue<string>()
                      ?? tr.Field("_id").GetValue<string>()
                      ?? tr.Field("message").GetValue<string>()
                      ?? tr.Property("id").GetValue<string>();
                }
                catch { }
                id = WorldLogPatchUtil.Clean(id);

                Kingdom k = null; City c = null; Actor u = null;
                try
                {
                    k = tr.Field("kingdom").GetValue<Kingdom>() ?? tr.Property("kingdom").GetValue<Kingdom>();
                    c = tr.Field("city").GetValue<City>() ?? tr.Property("city").GetValue<City>();
                    u = tr.Field("unit").GetValue<Actor>() ?? tr.Property("unit").GetValue<Actor>();
                }
                catch { }

                s1 = WorldLogPatchUtil.Clean(s1);
                s2 = WorldLogPatchUtil.Clean(s2);
                s3 = WorldLogPatchUtil.Clean(s3);

                // 轻量归类
                if (c != null && ((!string.IsNullOrEmpty(id) && id.ToLower().Contains("city")) ||
                                  (!string.IsNullOrEmpty(s1) && s1 == WorldLogPatchUtil.C(c))))
                {
                    WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("建城", $"{WorldLogPatchUtil.C(c)} @ {WorldLogPatchUtil.K(k ?? c.kingdom)}"));
                    return;
                }

                if (k != null && c == null && u == null &&
                    ((!string.IsNullOrEmpty(id) && id.ToLower().Contains("kingdom")) ||
                     (!string.IsNullOrEmpty(s1) && s1 == WorldLogPatchUtil.K(k))))
                {
                    WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("建国/建制", WorldLogPatchUtil.K(k)));
                    return;
                }

                if (u != null && string.IsNullOrEmpty(s2))
                {
                    WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("人物", $"{WorldLogPatchUtil.U(u)} @ {WorldLogPatchUtil.K(u.kingdom ?? k)}"));
                    return;
                }

                var parts = new List<string>();
                if (!string.IsNullOrEmpty(s1)) parts.Add(s1);
                if (!string.IsNullOrEmpty(s2)) parts.Add(s2);
                if (!string.IsNullOrEmpty(s3)) parts.Add(s3);
                string body = parts.Count > 0 ? string.Join(" | ", parts) : (id ?? "事件");

                WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("世界事件", body));
            }
            catch { /* 静默，别拖累加载 */ }
        }
    }

    #endregion
}
