using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace DemonGameRules2.code
{
    /// <summary>
    /// UGUI 版血条叠加层：单批次绘制所有血条，支持 sortingOrder 压层
    /// </summary>
    public sealed class DGR_HpOverlayUGUI : MaskableGraphic
    {
        // ======== 静态控制接口（给外部调用）========
        private static DGR_HpOverlayUGUI _inst;
        private static Canvas _canvas;
        private static bool _enabled = true;

        // 样式/阈值（可在外部 SetStyle/SetCullThresholds 覆盖）
        private static float _barWidth = 36f;
        private static float _barHeight = 3f;
        private static float _worldYOffset = 3.5f;

        private static float _hideAboveOrthoSize = 24f;
        private static float _minPixelsPerWorldUnit = 3.0f;

        private static bool _perfProfile = true;

        /// <summary>创建或复用 Canvas + 叠加组件；sortingOrder 越小越靠后</summary>
        public static void Ensure(int sortingOrder = -200)
        {
            if (_inst != null && _canvas != null)
            {
                _canvas.sortingOrder = sortingOrder;
                _canvas.overrideSorting = true;
                return;
            }

            var host = GameObject.Find("DGR_HpCanvas");
            if (!host)
            {
                host = new GameObject("DGR_HpCanvas");
                DontDestroyOnLoad(host);
            }

            _canvas = host.GetComponent<Canvas>();
            if (!_canvas) _canvas = host.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;
            _canvas.overrideSorting = true;

            // 可选：让它确实在 UI 图层（没有也没关系）
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) host.layer = uiLayer;

            var go = GameObject.Find("DGR_HpOverlayUGUI");
            if (!go)
            {
                go = new GameObject("DGR_HpOverlayUGUI");
                go.transform.SetParent(host.transform, false);
            }

            _inst = go.GetComponent<DGR_HpOverlayUGUI>();
            if (!_inst) _inst = go.AddComponent<DGR_HpOverlayUGUI>();

            // 让绘制区域覆盖整屏，且以屏幕左下角为 (0,0) 方便用像素坐标
            var rt = _inst.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _inst.raycastTarget = false; // 不拦截点击
            _inst.enabled = _enabled;
            _inst.SetAllDirty();
        }

        public static void SetEnabled(bool on)
        {
            _enabled = on;
            if (_inst) _inst.enabled = on;
        }

        public static void SetStyle(float width, float height, float yOffset)
        {
            _barWidth = width;
            _barHeight = height;
            _worldYOffset = yOffset;
            if (_inst) _inst.SetVerticesDirty();
        }

        public static void SetCullThresholds(float orthoSize, float pixelsPerUnit)
        {
            _hideAboveOrthoSize = orthoSize;
            _minPixelsPerWorldUnit = pixelsPerUnit;
            if (_inst) _inst.SetVerticesDirty();
        }

        public static void SetPerfProfile(bool on)
        {
            _perfProfile = on;
            if (_inst) _inst.SetVerticesDirty();
        }

        // ======== 实现区 ========
        struct ViewItem
        {
            public Vector2 pos;   // 屏幕像素坐标（左下为 0,0）
            public float d2;      // 与屏幕中心的距离平方（排序用）
            public float ratio;   // 血量比例
        }

        readonly List<ViewItem> _pri = new List<ViewItem>(512);
        readonly List<ViewItem> _sec = new List<ViewItem>(1024);
        int _secFrameOffset = 0;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (!_enabled) return;
            if (!traitAction.AlwaysHpBarsEnabled) return;
            if (!Config.game_loaded || SmoothLoader.isLoading() || World.world == null) return;

            var cam = Camera.main;
            if (!cam) return;

            // ——鸟瞰裁剪——
            if (cam.orthographic)
            {
                if (cam.orthographicSize >= _hideAboveOrthoSize) return;
            }
            else
            {
                float ppu = (cam.WorldToScreenPoint(new Vector3(1, 0, 0)) - cam.WorldToScreenPoint(Vector3.zero)).magnitude;
                if (ppu < _minPixelsPerWorldUnit) return;
            }

            _pri.Clear(); _sec.Clear();

            int sw = Screen.width, sh = Screen.height;
            foreach (var a in World.world.units)
            {
                if (a == null) continue;

                try
                {
                    if (a.isRekt() || !a.hasHealth()) continue;

                    // 跳过船（你之前的诉求）
                    if (a.asset != null && a.asset.is_boat) continue;

                    int hp = a.getHealth();
                    int maxhp = a.getMaxHealth();
                    if (maxhp <= 0) continue;

                    var pos = a.current_position;
                    var w = new Vector3(pos.x, pos.y + _worldYOffset, 0f);
                    var s = cam.WorldToScreenPoint(w);
                    if (s.z < 0f) continue;

                    // 条左下角像素坐标（本 Graphic 以左下为 0,0）
                    float gx = s.x - _barWidth * 0.5f;
                    float gy = s.y - _barHeight * 0.5f;
                    if (gx + _barWidth < 0 || gx > sw || gy + _barHeight < 0 || gy > sh) continue;

                    float dx = s.x - sw * 0.5f;
                    float dy = s.y - sh * 0.5f;
                    float d2 = dx * dx + dy * dy;

                    float ratio = Mathf.Clamp01(hp / (float)maxhp);

                    bool priority = false;
                    try { if (a.isFighting()) priority = true; } catch { }
                    if (!priority) { try { if (a.hasTrait("demon_mask")) priority = true; } catch { } }
                    if (!priority) { try { if (a.data != null && a.data.favorite) priority = true; } catch { } }

                    var item = new ViewItem { pos = new Vector2(gx, gy), d2 = d2, ratio = ratio };
                    if (priority) _pri.Add(item); else _sec.Add(item);
                }
                catch { }
            }

            if (_pri.Count > 1) _pri.Sort((x, y) => x.d2.CompareTo(y.d2));

            int budget = ComputeDynamicBudget();
            int drawn = 0;

            for (int i = 0; i < _pri.Count && drawn < budget; i++)
            {
                AddBar(vh, _pri[i]);
                drawn++;
            }

            if (drawn < budget && _sec.Count > 0)
            {
                if (_perfProfile && _sec.Count > budget)
                {
                    int remain = budget - drawn;
                    int step = Mathf.Max(1, _sec.Count / remain);
                    int start = _secFrameOffset % Mathf.Min(step, _sec.Count);
                    for (int i = start; i < _sec.Count && drawn < budget; i += step)
                    {
                        AddBar(vh, _sec[i]); drawn++;
                    }
                    _secFrameOffset++;
                }
                else
                {
                    if (_sec.Count > 1) _sec.Sort((x, y) => x.d2.CompareTo(y.d2));
                    for (int i = 0; i < _sec.Count && drawn < budget; i++)
                    {
                        AddBar(vh, _sec[i]); drawn++;
                    }
                }
            }
        }

        static int ComputeDynamicBudget()
        {
            int px = Screen.width * Screen.height;
            int baseCap = 120 + px / 15000; // 1080p≈258
            return Mathf.Clamp(baseCap, 120, 450);
        }

        // ——把一个矩形（像素坐标）以一个四边形加入 UGUI 顶点缓冲——
        static void AddQuad(VertexHelper vh, Rect r, Color col)
        {
            int start = vh.currentVertCount;
            var v = UIVertex.simpleVert; v.color = col;

            v.position = new Vector3(r.xMin, r.yMin); vh.AddVert(v);
            v.position = new Vector3(r.xMin, r.yMax); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMax); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMin); vh.AddVert(v);

            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        void AddBar(VertexHelper vh, ViewItem v)
        {
            // 背景
            AddQuad(vh, new Rect(v.pos.x - 1, v.pos.y - 1, _barWidth + 2, _barHeight + 2), new Color(0, 0, 0, 0.5f));

            // 填充
            float w = _barWidth * v.ratio;
            var fill = Color.Lerp(new Color(0.9f, 0.1f, 0.1f, 0.95f), new Color(0.1f, 0.9f, 0.1f, 0.95f), v.ratio);
            AddQuad(vh, new Rect(v.pos.x, v.pos.y, w, _barHeight), fill);

            // 边框（细线）
            var black = Color.black;
            AddQuad(vh, new Rect(v.pos.x - 1, v.pos.y - 1, _barWidth + 2, 1), black);
            AddQuad(vh, new Rect(v.pos.x - 1, v.pos.y + _barHeight, _barWidth + 2, 1), black);
            AddQuad(vh, new Rect(v.pos.x - 1, v.pos.y - 1, 1, _barHeight + 2), black);
            AddQuad(vh, new Rect(v.pos.x + _barWidth, v.pos.y - 1, 1, _barHeight + 2), black);
        }

        // 任何会影响顶点的外部变化，都触发重建
        void LateUpdate()
        {
            if (!_enabled) return;
            if (!traitAction.AlwaysHpBarsEnabled) return;
            // 每帧重建一次网格（跟原先 IMGUI 每帧画一致）
            SetVerticesDirty();
        }
    }
}
