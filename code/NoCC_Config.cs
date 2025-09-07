using HarmonyLib;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace DemonGameRules.Patches
{
    // 配置：要拦的状态
    static class NoCC_Config
    {
        public static readonly HashSet<string> Blocked =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "stunned", "frozen" };
        public const string T_DEMON_MASK = "demon_mask";
    }

    /* =========================
     * 根 API：只打 BaseSimObject.addStatusEffect
     * ========================= */

    [HarmonyPatch(typeof(BaseSimObject), nameof(BaseSimObject.addStatusEffect),
        new[] { typeof(string), typeof(float), typeof(bool) })]
    static class Patch_Base_AddStatusEffect_DemonImmunity
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(BaseSimObject __instance, ref string __0, ref bool __result)
        {
            // 只处理我们关心的控制状态
            if (!NoCC_Config.Blocked.Contains(__0))
                return true; // 其它状态放行

            try
            {
                // 只有“恶魔面具”免疫；其余人放行
                if (__instance.isActor() && __instance.a != null && __instance.a.hasTrait(NoCC_Config.T_DEMON_MASK))
                {
                    __result = false;   // 告诉调用者“没加上”
                    return false;       // 跳过原方法
                }
            }
            catch { /* 忽略异常，继续放行 */ }

            return true; // 非恶魔：照常加上控制
        }
    }

    /* =========================================
     * 工具函数：通杀 addStunned/FrozenEffectOnTarget*
     * （可选，但建议留着，防将来新增变体）
     * ========================================= */

    [HarmonyPatch]
    static class Patch_ActionLibrary_BlockAllStunFreezeHelpers
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var t = typeof(ActionLibrary);
            return t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .Where(m =>
                        m.Name.StartsWith("addStunnedEffectOnTarget", StringComparison.OrdinalIgnoreCase) ||
                        m.Name.StartsWith("addFrozenEffectOnTarget", StringComparison.OrdinalIgnoreCase));
        }

        [HarmonyPrefix]
        static bool Prefix(BaseSimObject pSelf, BaseSimObject pTarget)
        {
            // 只有恶魔免疫：如果目标是恶魔，就短路；否则放行
            try
            {
                if (pTarget != null && pTarget.isActor() && pTarget.a != null && pTarget.a.hasTrait(NoCC_Config.T_DEMON_MASK))
                    return false; // 恶魔：不加控制
            }
            catch { }
            return true; // 其他：走原逻辑
        }
    }

    /* =========================
     * makeStunned：按需打开
     * =========================
     * 如果你想“所有人都不吃 makeStunned”，保留当前逻辑；
     * 如果只想“恶魔免疫”，就改成条件判断后放行。
     */

    [HarmonyPatch(typeof(Actor), nameof(Actor.makeStunned))]
    static class Patch_Actor_MakeStunned_Optional
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Actor __instance, ref float pTime)
        {
            // 方案A：只有恶魔免疫
            try
            {
                if (__instance != null && __instance.hasTrait(NoCC_Config.T_DEMON_MASK))
                    return false; // 恶魔：跳过
            }
            catch { }
            return true; // 其他：保留原生眩晕

            // 方案B（全局禁用）：直接 return false;
        }
    }

    /* ===========================================================
     * 闪电：保持放行，别再去跳过 checkLightningAction
     * =========================================================== */

    [HarmonyPatch(typeof(MapAction), nameof(MapAction.checkLightningAction))]
    static class Patch_MapAction_CheckLightningAction_LeaveIt
    {
        [HarmonyPrefix]
        static bool Prefix(Vector2Int pPos, int pRad)
        {
            return true; // 放行，避免打断FX/成就；控制免疫由上面拦
        }
    }
}
