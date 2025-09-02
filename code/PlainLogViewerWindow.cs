using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using NeoModLoader.General.UI.Window;
using DemonGameRules2.code; // 直接从 traitAction 拿内存数据

namespace DemonGameRules.UI
{
    // 只用 ModLoader 的 WindowCreator/ScrollWindow，内部用 UnityEngine.UI 搭 UI
    public class PlainLogViewerWindow : MonoBehaviour
    {
        private ScrollWindow _sw;
        private Transform _contentRoot;

        private Transform _listContainer;
        private Font _font;

        // UI 文本分块限制，防止 Text 顶点超限
        private const int ChunkCharLimit = 12000;
        private const int MaxTotalChars = 300_000;

        // 窗口尺寸自适应部分已去除
        private void OnRectTransformDimensionsChange()
        {
            // 去除自适应，保持固定布局
            if (_listContainer == null) return;
            float w = 400f;  // 固定宽度
            for (int i = 0; i < _listContainer.childCount; i++)
            {
                var le = _listContainer.GetChild(i).GetComponent<LayoutElement>();
                if (le != null) le.preferredWidth = w;
            }
        }

        private void ResizeWindow(int w, int h)
        {
            var winRT = _sw ? _sw.transform as RectTransform : null;
            if (winRT == null) return;
            winRT.sizeDelta = new Vector2(w, h);
        }

        public static void CreateAndInit(string windowId, string titleText)
        {
            var sw = WindowCreator.CreateEmptyWindow(windowId, titleText);
            sw.gameObject.SetActive(false);
            sw.transform_scrollRect.gameObject.SetActive(true);

            var winRT = sw.transform as RectTransform;
            if (winRT != null) winRT.sizeDelta = new Vector2(720, 820);

//            var sr = sw.transform_scrollRect ? sw.transform_scrollRect.GetComponent<ScrollRect>() : null;
//            if (sr)
//            {
//                sr.vertical = true;
//                sr.horizontal = true;
//                sr.movementType = ScrollRect.MovementType.Elastic;
//                sr.inertia = true;
//#if UNITY_2021_2_OR_NEWER
//                sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
//#endif
//            }

            var comp = sw.transform_content.gameObject.AddComponent<PlainLogViewerWindow>();
            comp._sw = sw;
            comp._contentRoot = sw.transform_content;
        }

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildUI();
        }

        private void OnEnable()
        {
            ShowLeaderboard();
        }

        // 构建 UI
        private void BuildUI()
        {
            var root = _contentRoot.gameObject;
            var vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, -100, 0, 0);
            vlg.spacing = 4;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = true;
            vlg.childForceExpandWidth = true;

            // 内容列表容器
            var list = CreateVGroup(_contentRoot, 1, 2, new RectOffset(25, -100, 80, 0));
            var bg = list.AddComponent<Image>();
            var slice = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
            if (slice != null)
            {
                bg.sprite = slice;
                bg.type = Image.Type.Sliced;
                bg.color = new Color(0f, 0f, 0f, 0f);
            }
            else
            {
                bg.color = new Color(0f, 0f, 0f, 0f);
            }

            var logLayout = list.GetComponent<VerticalLayoutGroup>();
            logLayout.spacing = 2;
            logLayout.childControlHeight = true;
            logLayout.childControlWidth = true;
            logLayout.childForceExpandHeight = true;
            logLayout.childForceExpandWidth = true;

            _listContainer = list.transform;
        }

        private float GetScrollWidth()
        {
            var rt = _sw != null ? _sw.transform_scrollRect as RectTransform : null;
            return (rt && rt.rect.width > 0) ? rt.rect.width : 400f;  // 固定宽度
        }

        // —— 核心：从 traitAction 取实时富文本并渲染 —— 
        private void ShowLeaderboard()
        {
            if (_listContainer == null) return;

            // 清空旧内容
            for (int i = _listContainer.childCount - 1; i >= 0; i--)
                Destroy(_listContainer.GetChild(i).gameObject);

            string content = null;
            try
            {
                content = traitAction.BuildLeaderboardRichText();
            }
            catch (Exception ex)
            {
                content = $"<color=#FF6666>生成榜单失败：</color>{ex.Message}";
            }

            if (string.IsNullOrEmpty(content))
                content = "<color=#AAAAAA>暂无数据。世界还没加载，或者你一个像样的单位都没有。</color>";

            if (content.Length > MaxTotalChars)
                content = content.Substring(content.Length - MaxTotalChars);

            // 按行分块渲染，避免 Text 顶点爆炸
            var sb = new System.Text.StringBuilder(ChunkCharLimit + 256);
            using (var sr = new StringReader(content))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (sb.Length + line.Length + 1 > ChunkCharLimit)
                    {
                        CreateChunkText(sb.ToString());
                        sb.Length = 0;
                    }
                    sb.AppendLine(line);
                }
                if (sb.Length > 0) CreateChunkText(sb.ToString());
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_listContainer as RectTransform);
            var srComp = _sw.transform_scrollRect ? _sw.transform_scrollRect.GetComponent<ScrollRect>() : null;
            if (srComp)
            {
                Canvas.ForceUpdateCanvases();
                srComp.verticalNormalizedPosition = 0f;
            }
        }

        private void CreateChunkText(string content)
        {
            var go = new GameObject("LogChunk", typeof(RectTransform));
            go.transform.SetParent(_listContainer, false);

            var txt = go.AddComponent<Text>();
            txt.text = content;
            txt.font = _font;
            txt.fontSize = 8;  // 保持固定字体大小
            txt.alignment = TextAnchor.UpperLeft;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.supportRichText = true;
            txt.raycastTarget = false;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = Mathf.Max(200f, GetScrollWidth() - 24f);
            le.flexibleWidth = 1;
        }

        private GameObject CreateVGroup(Transform parent, int h, int spacing, RectOffset padding)
        {
            var go = new GameObject("VGroup", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = h;
            return go;
        }
    }

    public static class LogViewerUI
    {
        private static bool _inited;
        private static PowersTab _tab;

        private static Sprite TrySprite(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                var s = SpriteTextureLoader.getSprite(k);
                if (s != null) return s;
            }
            return null;
        }

        public static void Init()
        {
            if (_inited) return;
            _inited = true;



            PlainLogViewerWindow.CreateAndInit("dgr_log_window", "BiaoTi");

            var tabIcon = TrySprite("icons/iconTab", "ui/icons/iconBook");
            _tab = TabManager.CreateTab("dgr_tab", "BiaoTi", "", tabIcon);
            _tab.SetLayout(new List<string> { "tools" });

            var btnIcon = TrySprite("icons/iconCreatureTop", "icons/iconTab", null);
            var btn = PowerButtonCreator.CreateWindowButton("dgr_open_log_window", "dgr_log_window", btnIcon);
            _tab.AddPowerButton("tools", btn);
            _tab.UpdateLayout();
        }
    }
}
