using System;
using System.Reflection;
using HarmonyLib;
using DemonGameRules.code;
using NeoModLoader.api;
using ai; // ← 别再漏分号
using System.Reflection;


namespace DemonGameRules
{
    internal class DemonGameRulesClass : BasicMod<DemonGameRulesClass>
    {
        public static string id = "worldbox.mod.DemonGameRules";

        protected override void OnModLoad()
        {
            try
            {
                UnityEngine.Debug.Log("[DGR] Step1: Harmony patch begin");

                var harmony = new Harmony(id);
                // 1) 扫整个程序集
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                // 2) 兼容：补旧 patch 类
                harmony.PatchAll(typeof(DemonGameRules.code.patch));

                UnityEngine.Debug.Log("[DGR] Step2: Harmony patch done");

                // 4) 初始化日志查看 UI（只一次）
                DemonGameRules.UI.LogViewerUI.Init();
                UnityEngine.Debug.Log("[DGR] Step3: UI inited");

                traitGroup.Init();
                traits.Init();



            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[DemonGameRules] Error during mod loading:\n{ex}");
            }
        }

      
    }
}
