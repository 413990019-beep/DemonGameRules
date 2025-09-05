using System;
using System.Linq;
using System.Threading.Tasks;
using ai;
using UnityEngine;
using DemonGameRules.code;
using DemonGameRules;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib; // 为 Traverse 反射取字段
using System.Reflection;
using System.Collections;


namespace DemonGameRules2.code
{
    public static partial class traitAction
    {

        // ===== DemonGameRules2.code.traitAction 内新增 =====

        // ====== 摄像机观战（不依赖 UI 类）======
        #region 观战管理（摄像机跟随版；每世界年仅触发一次）

        private static bool _anchorActive = false; // 是否正在锚定尸体现场
        private static Vector3 _anchorPos;         // 锚定坐标（尸体/最后坐标）
        private static float _stickUntil = 5f;     // 在此时间点之前不允许切换

        // 放到本文件顶部“常量/工具”区
        private const string T_DEMON_MASK = "demon_mask";

        private static void EnsureDemonMask(Actor a)
        {
            try
            {
                if (a != null && a.hasHealth() && !a.hasTrait("demon_mask"))
                    a.addTrait("demon_mask");
            }
            catch
            {
                // 别吵，失败就算了
            }
        }

        static void ArmDeathAnchor(Actor a, Transform camXform)
        {
            try
            {
                float z = camXform.position.z;
                Vector3 pos;
                try { var p = a.current_position; pos = new Vector3(p.x, p.y, z); }
                catch { var t = a?.current_tile; pos = (t != null) ? new Vector3(t.pos.x, t.pos.y, z) : camXform.position; }
                _anchorPos = pos;
            }
            catch { _anchorPos = camXform.position; }

            _anchorActive = true;
            // 一切切换都要受 _minStickSeconds 约束
            _stickUntil = Mathf.Max(_lastSwitchTime + _minStickSeconds, Time.unscaledTime);
        }


        // 连续不在战斗多久后才允许切换（秒）
        private static float _switchAfterNotFighting = 10.0f;
        // 记录目标从“开始不在战斗”起的时间戳
        private static float _notFightingSince = -1f;

        // 切换的最小停留时间，避免过快切换（秒）
        private static float _minStickSeconds = 10.0f;
        // 最近一次完成切换的时间戳
        private static float _lastSwitchTime = -999f;

        // ====== 受击自动观察总开关（已接 default_config.json/回调）======
        private static bool _spectateOnHitEnabled = false;
        public static bool SpectateOnHitEnabled => _spectateOnHitEnabled;
        public static void OnSpectateSwitchChanged(bool enabled)
        {
            _spectateOnHitEnabled = enabled;
            UnityEngine.Debug.Log($" SpectateOnHit/受击自动观察 => {(_spectateOnHitEnabled ? "开启ON" : "关闭OFF")}");
         
        }

        // 最近一次受击事件的双方与时间戳
        private static Actor _lastHitAttacker;
        private static Actor _lastHitVictim;
        private static float _lastHitStamp;

        public static void TrySpectateOnGetHit(Actor victim, BaseSimObject pAttacker)
        {


            // 记录最近一次战斗双方
            try { _lastHitVictim = victim; } catch { _lastHitVictim = null; }
            try { _lastHitAttacker = pAttacker?.a; } catch { _lastHitAttacker = null; }
            _lastHitStamp = Time.unscaledTime;


            if (!_spectateOnHitEnabled) return;
            if (!SpectateAllowedThisYear()) return;

            

            Actor attacker = null;
            try { attacker = pAttacker?.a; } catch { }

            // 优先级：1) demon_mask 的攻击者 2) demon_mask 的受害者 3) 攻击者 4) 受害者
            Actor pick = null;
            try
            {
                if (attacker != null && attacker.hasHealth() && attacker.hasTrait("demon_mask")) pick = attacker;
                else if (victim != null && victim.hasHealth() && victim.hasTrait("demon_mask")) pick = victim;
                else if (attacker != null && attacker.hasHealth()) pick = attacker;
                else if (victim != null && victim.hasHealth()) pick = victim;
            }
            catch { /* 没空吵架 */ }

            if (pick == null) return;

            long id = -1;
            try { id = pick.data?.id ?? -1; } catch { }

            // 同一年已经盯过同一个人了，就别抖腿
            if (id > 0 && id == _lastSpectateTargetId && YearNow() == _lastSpectateYear) return;

            // 同一年同一人就不抖腿，最后改为：
            StartFollow(pick, toast: true, markYear: true);  // ← 带 markYear
        }

        public static void StartFollow(Actor a, bool toast = true, bool markYear = true)
        {
            if (a == null || !a.hasHealth() || World.world == null) return;
            try { if (a.asset != null && a.asset.is_boat) return; } catch { }

            EnsureDemonMask(a);

            _anchorActive = false;                // ← 新增：开始跟随就取消尸体锚
            _followTarget = a;
            _followWorld = World.world;
            _notFightingSince = -1f;
            _lastSwitchTime = Time.unscaledTime;
            if (markYear) MarkSpectated(a);
        }


        public static void StopFollow()
        {
            _followTarget = null;
            _followWorld = null;
            _camVel = Vector3.zero;
            _anchorActive = false;   // ← 新增
        }
        // 年冷却 & 目标记忆
        private static int _lastSpectateYear = -1;
        private static long _lastSpectateTargetId = -1;

        private static bool SpectateAllowedThisYear()
        {
            if (!Config.game_loaded || SmoothLoader.isLoading() || World.world == null) return false;

            // 世界实例换了（读档/新局），允许立刻触发一次并复位年标记
            if (!ReferenceEquals(_followWorld, World.world))
            {
                _followWorld = World.world;
                _lastSpectateYear = YearNow() - 1;
                _lastSpectateTargetId = -1;
            }
            return YearNow() > _lastSpectateYear;
        }

        private static void MarkSpectated(Actor a)
        {
            _lastSpectateYear = YearNow();
            try { _lastSpectateTargetId = a?.data?.id ?? -1; } catch { _lastSpectateTargetId = -1; }
        }


        // 2) traitAction 内的跟随逻辑（只贴变化点）
        // 状态
        private static Actor _followTarget;
        private static MapBox _followWorld;
        private static Vector3 _camVel = Vector3.zero;
        private const float _followSmooth = 0.18f;
        private const float _moveMinDist = 0.25f;        // 小于这距离就不动，防抖
        private static float _manualOverrideUntil = 0f;  // 玩家手动相机的让路时间戳

        // 手动相机检测：有输入就让路 0.5 秒
        static bool PlayerIsControllingCamera()
        {
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2)) return true;
            if (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f) return true;
            // 兼容键盘移动（如果有绑定）
            if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f) return true;
            if (Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f) return true;
            return false;
        }

        // 每帧推进：用 LateUpdate 调，不和原生相机争执行时机
        public static void UpdateSpectatorTick(MoveCamera mover)
        {
            if (!_spectateOnHitEnabled) return;
            if (mover == null) return;
            if (!Config.game_loaded || SmoothLoader.isLoading() || World.world == null) return;

            // 世界切换或读档：停止跟随
            if (!ReferenceEquals(_followWorld, World.world)) { StopFollow(); }

            // 玩家手动操作时让路
            if (PlayerIsControllingCamera()) _manualOverrideUntil = Time.unscaledTime + 0.5f;
            if (Time.unscaledTime < _manualOverrideUntil) return;


            // 无目标或目标挂了：不立刻跳人，按 _minStickSeconds 黏住现场
            if (_followTarget == null || _followTarget.isRekt() || !_followTarget.hasHealth())
            {
                Transform camXform = GetCameraTransform(mover) ?? mover.transform;

                // 首次检测到死亡/无效时，武装锚点
                if (!_anchorActive) ArmDeathAnchor(_followTarget, camXform);

                // 相机继续黏到锚点位置
                Vector3 cur = camXform.position;
                if ((_anchorPos - cur).sqrMagnitude >= _moveMinDist * _moveMinDist)
                    camXform.position = Vector3.SmoothDamp(cur, _anchorPos, ref _camVel, _followSmooth);

                // 在达成最小停留时间之前，绝不切换
                if (Time.unscaledTime < _stickUntil) return;

                // 允许切换了：挑替换目标（保持你原有优先级与冷却风格）
                var replace = PickReplacementTarget(camXform);
                if (replace != null)
                {
                    _anchorActive = false;
                    StartFollow(replace, toast: true, markYear: false);
                    _switchCooldownUntil = Time.unscaledTime + 2.0f;
                }
                else
                {
                    // 没找到合适的，过会儿再扫
                    _nextScanTime = Time.unscaledTime + 0.75f;
                }
                return;
            }


            // 目标是否在战斗
            bool isFighting = IsFightingSafe(_followTarget);

            // 如果在战斗：清掉“不在战斗计时”，不允许任何换人
            if (isFighting)
            {
                _notFightingSince = -1f;
            }
            else
            {
                // 第一次发现不在战斗，开始计时
                if (_notFightingSince < 0f) _notFightingSince = Time.unscaledTime;

                // 只有满足“连续不在战斗满2秒”且各种冷却满足才允许尝试换人
                bool cooledScan = Time.unscaledTime >= _nextScanTime;
                bool cooledSwitch = Time.unscaledTime >= _switchCooldownUntil;
                bool stayedLong = (Time.unscaledTime - _lastSwitchTime) >= _minStickSeconds;
                bool enoughIdle = (Time.unscaledTime - _notFightingSince) >= _switchAfterNotFighting;

                // 如果当前跟随对象正好是最近受击双方之一，给它 2 秒面子再考虑换人
                bool followIsLastHitGuy = ReferenceEquals(_followTarget, _lastHitAttacker) || ReferenceEquals(_followTarget, _lastHitVictim);
                bool recentHitGrace = followIsLastHitGuy && (Time.unscaledTime - _lastHitStamp) < 2.0f;

                if (enoughIdle && cooledScan && cooledSwitch && stayedLong && !recentHitGrace)
                {
                    Transform camXformTemp = GetCameraTransform(mover) ?? mover.transform;
                    var replace = PickReplacementTarget(camXformTemp);  // 仍然优先 demon_mask 且在战斗
                    if (replace != null && replace != _followTarget)
                    {
                        StartFollow(replace, toast: true, markYear: false);  // 不占用“每年一次”
                        _switchCooldownUntil = Time.unscaledTime + 2.0f;     // 短冷却
                    }
                    _nextScanTime = Time.unscaledTime + 0.75f; // 稍微放慢扫描频率
                }
            }


            // 选一个可用的相机 Transform（优先 mover.camera，再退回 mover.transform）
            Transform camXform = GetCameraTransform(mover);
            if (camXform == null) camXform = mover.transform;

            // 目标坐标（多层兜底，拿不到就别动）
            Vector3 targetPos = default;
            bool gotTarget = false;

            try
            {
                Vector2 p = _followTarget.current_position;
                targetPos = new Vector3(p.x, p.y, camXform.position.z);
                gotTarget = true;
            }
            catch { }

            if (!gotTarget)
            {
                try
                {
                    var t = _followTarget.current_tile;
                    if (t != null)
                    {
                        targetPos = new Vector3(t.pos.x, t.pos.y, camXform.position.z);
                        gotTarget = true;
                    }
                }
                catch { }
            }

            if (!gotTarget) return; // ← 防止 CS0165：拿不到就不动

            // 距离很近就不动，免抖
            Vector3 cur = camXform.position;
            const float moveMinDist = _moveMinDist; // 你上面定义的 0.25f
            if ((targetPos - cur).sqrMagnitude < moveMinDist * moveMinDist) return;

            // 平滑跟随
            camXform.position = Vector3.SmoothDamp(cur, targetPos, ref _camVel, _followSmooth);
        }

        private static float _nextScanTime;          // 下次允许扫描时间点
        private static float _switchCooldownUntil;   // 切换后的冷却，避免频繁跳人

        static bool IsFightingSafe(Actor a)
        {
            if (a == null) return false;
            try
            {
                if (a.isRekt() || !a.hasHealth()) return false;
                return a.isFighting();
            }
            catch
            {
                return true; // 出异常就当在战斗，防止误切
            }
        }

        static bool IsBoat(Actor a)
        {
            try { return a != null && a.asset != null && a.asset.is_boat; } catch { return false; }
        }


        static Actor PickReplacementTarget(Transform camXform)
        {
            // 1) 最近 getHit 的双方，优先 demon_mask 且在打架
            try
            {
                if (IsFightingSafe(_lastHitAttacker) && !IsBoat(_lastHitAttacker) && _lastHitAttacker.hasTrait("demon_mask")) return _lastHitAttacker;
                if (IsFightingSafe(_lastHitVictim) && !IsBoat(_lastHitVictim) && _lastHitVictim.hasTrait("demon_mask")) return _lastHitVictim;
                if (IsFightingSafe(_lastHitAttacker) && !IsBoat(_lastHitAttacker)) return _lastHitAttacker;
                if (IsFightingSafe(_lastHitVictim) && !IsBoat(_lastHitVictim)) return _lastHitVictim;
            }
            catch { }


            // 2) 在相机附近找正在打的 demon_mask
            const float RADIUS = 80f;
            Actor best = null;
            float bestDist2 = float.MaxValue;

            var mgr = World.world?.units; // ActorManager，可枚举但不可下标
            if (mgr != null)
            {
                Vector3 c = camXform.position;

                foreach (var u in mgr) // ← 改成 foreach，别再用 [i]
                {

                    // 🚫 候选里跳过船
                    try { if (u != null && u.asset != null && u.asset.is_boat) continue; } catch { }

                    if (!IsFightingSafe(u)) continue;

                    float dx, dy;
                    try
                    {
                        var p = u.current_position; dx = p.x - c.x; dy = p.y - c.y;
                    }
                    catch
                    {
                        var t = u.current_tile; if (t == null) continue;
                        dx = t.pos.x - c.x; dy = t.pos.y - c.y;
                    }

                    float d2 = dx * dx + dy * dy;
                    if (d2 > RADIUS * RADIUS) continue;

                    bool demon = false; try { demon = u.hasTrait("demon_mask"); } catch { }

                    if (best == null || (demon && !(HasDemonMask(best))) || d2 < bestDist2)
                    {
                        best = u; bestDist2 = d2;
                        if (demon && d2 < 9f) break; // 足够近且是恶魔，直接收工
                    }
                }
            }

            if (best != null) return best;

            // 3) 兜底：相机附近任意在打的
            if (mgr != null)
            {
                Vector3 c = camXform.position;
                float best2 = float.MaxValue;
                foreach (var u in mgr) // ← 同理，foreach
                {
                    try { if (IsBoat(u)) continue; } catch { }   // ⬅️ 新增

                    if (!IsFightingSafe(u)) continue;

                    float dx, dy;
                    try { var p = u.current_position; dx = p.x - c.x; dy = p.y - c.y; }
                    catch { var t = u.current_tile; if (t == null) continue; dx = t.pos.x - c.x; dy = t.pos.y - c.y; }

                    float d2 = dx * dx + dy * dy;
                    if (d2 < best2) { best2 = d2; best = u; }
                }
            }

            return best;
        }

        static bool HasDemonMask(Actor a)
        {
            try { return a != null && a.hasTrait("demon_mask"); } catch { return false; }
        }



        // 小工具：尽量拿到真正的相机 Transform
        // 放字段区
        private static Transform _cachedCamXform;
        private static MoveCamera _cachedMover;

        static Transform GetCameraTransform(MoveCamera mover)
        {
            if (mover == null) return null;
            if (_cachedMover == mover && _cachedCamXform != null) return _cachedCamXform;

            try
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var prop = mover.GetType().GetProperty("camera", F);
                var cam = prop?.GetValue(mover) as UnityEngine.Camera;
                if (cam == null)
                {
                    var fld = mover.GetType().GetField("camera", F) ?? mover.GetType().GetField("cam", F);
                    cam = fld?.GetValue(mover) as UnityEngine.Camera;
                }
                _cachedCamXform = (cam != null ? cam.transform : mover.transform);
                _cachedMover = mover;
                return _cachedCamXform;
            }
            catch { return mover.transform; }
        }




        //可选：被你“新世界复位”流程调用
        public static void ResetSpectatorForNewWorld()
        {
            StopFollow();
            _lastSpectateYear = -1;
            _lastSpectateTargetId = -1;
            _followWorld = null;
            _anchorActive = false;   // ← 新增
        }

        #endregion

        #region  血条

        // 开关状态
        private static bool _alwaysHpBars = true;
        public static bool AlwaysHpBarsEnabled => _alwaysHpBars;

        // 面板/配置回调
        public static void OnAlwaysHpBarChanged(bool enabled)
        {
            _alwaysHpBars = enabled;
            UnityEngine.Debug.Log($" Always HP Bars/血量常驻显示 => {(_alwaysHpBars ? "开启ON" : "关闭OFF")}");

            // 确保 UGUI 画布存在，并设定排在其它 Overlay UI 下面
            DemonGameRules2.code.DGR_HpOverlayUGUI.Ensure(sortingOrder: -200);

            // 开/关
            DemonGameRules2.code.DGR_HpOverlayUGUI.SetEnabled(enabled);

            // （可选）统一你的样式 & 裁剪阈值
            // DemonGameRules2.code.DGR_HpOverlayUGUI.SetStyle(width: 36f, height: 3f, yOffset: 3.5f);
            // DemonGameRules2.code.DGR_HpOverlayUGUI.SetCullThresholds(orthoSize: 24f, pixelsPerUnit: 3.0f);
        }

        // 兼容你原来的“确保存在”调用点（比如在导播开关处同步开启血条）
        internal static void EnsureHpOverlayExists()
        {
            DemonGameRules2.code.DGR_HpOverlayUGUI.Ensure(sortingOrder: -200);
            DemonGameRules2.code.DGR_HpOverlayUGUI.SetEnabled(true);

            // （可选）性能模式：分片绘制次级目标
            // DemonGameRules2.code.DGR_HpOverlayUGUI.SetPerfProfile(true);
        }

        #endregion

        #region  亚种特质
        // —— 配置区：黑名单、核心包、附加包 ——
        private static readonly string[] _traitsToRemove = new string[]
        {
            "fire_elemental_form", "fenix_born", "metamorphosis_crab","aquatic",
            "metamorphosis_chicken", "metamorphosis_wolf", "metamorphosis_butterfly",
            "death_grow_tree", "death_grow_plant", "death_grow_mythril", "metamorphosis_sword"
        };

        private static readonly string[] _coreBrainTraits = new string[]
        {
            "amygdala", "advanced_hippocampus", "prefrontal_cortex", "wernicke_area"
        };

        private static readonly string[] _fullTraitGroup = new string[]
        {
            "high_fecundity", "long_lifespan"
        };

        // 避免和 UnityEngine.Random 撞名，老老实实用 System.Random
        private static readonly System.Random _rng = new System.Random();

        /// <summary>
        /// 强制激活脑部特质逻辑（供 Subspecies.newSpecies Postfix 调用）
        /// </summary>
        public static void ApplyBrainTraitPackage(Subspecies s)
        {
            if (s == null) return;


            if (!_autoSpawnEnabled) return; // 新增：没开就不生成

            // 世界法则：必须打开“自动生成动物”，否则不处理
            if (WorldLawLibrary.world_law_animals_spawn == null ||
                !WorldLawLibrary.world_law_animals_spawn.isEnabled())
            {
                return;
            }



            // 1) 先清理会搅局的变形/重生类特质
            foreach (var t in _traitsToRemove)
            {
                if (s.hasTrait(t))
                    s.removeTrait(t);
            }

            // 2) 如果已经有 prefrontal_cortex，则补齐四件套并加完整属性组
            if (s.hasTrait("prefrontal_cortex"))
            {
                foreach (var core in _coreBrainTraits)
                {
                    if (!s.hasTrait(core))
                        s.addTrait(core, true);
                }
                foreach (var extra in _fullTraitGroup)
                {
                    s.addTrait(extra, true);
                }
                return;
            }

            // 3) 否则走概率：90% 只给 population_minimal；10% 尝试装四件套
            double roll = _rng.NextDouble();

            if (roll < 0.90)
            {
                // 90%：仅极简人口特质
                s.addTrait("population_minimal", true);
                return;
            }

            // 10%：尝试塞入核心脑部特质
            bool cortexAdded = false;
            s.addTrait("population_minimal", true);

            foreach (var core in _coreBrainTraits)
            {
                if (!s.hasTrait(core))
                {
                    s.addTrait(core, true);
                    if (core == "prefrontal_cortex" && s.hasTrait("prefrontal_cortex"))
                        cortexAdded = true;
                }
            }

            if (cortexAdded)
            {
                // 成功拿到前额叶，解锁完整属性组
                foreach (var extra in _fullTraitGroup)
                {
                    s.addTrait(extra, true);
                }
            }
            else
            {
                // 没装上前额叶，维持极简人口特质即可（重复 addTrait 引擎会自己去重）
                s.addTrait("population_minimal", true);
            }
        }

        #endregion

        #region 模式切换




        // ===== 新增：updateStats 补丁总开关（默认 false）=====
        public static bool _statPatchEnabled = false;

        // 供外部只读（可选）
        public static bool StatPatchEnabled => _statPatchEnabled;

        // 设置面板/配置文件回调：切换总开关
        public static void OnStatPatchSwitchChanged(bool enabled)
        {
            _statPatchEnabled = enabled;
            UnityEngine.Debug.Log($"[恶魔规则] updateStats击杀成长补丁 => {(enabled ? "开启/ON" : "关闭/OFF")}");

            // 关掉时清空护栏缓存，避免旧值干扰
            if (!enabled)
            {
                try { DemonGameRules.code.patch.ClearStatPatchCaches(); } catch { }
            }
        }

        // ====== 新增：自动生成生物总开关 ======
        private static bool _autoSpawnEnabled = false;

        // 配置面板勾选时由 ModLoader 调用
        public static void OnAutoSpawnChanged(bool enabled)
        {
            _autoSpawnEnabled = enabled;
            UnityEngine.Debug.Log($" AutoSpawn/自动生成生物 => {(_autoSpawnEnabled ? "开启ON" : "关闭OFF")}");
        }
        // 这个方法会在开关被切换时调用
        // 切换模式的方法
        public static void OnModeSwitchChanged(bool isDemonModeEnabled)
        {
            isDemonMode = isDemonModeEnabled;

            if (isDemonMode)
            {
                // 激活恶魔模式
                ActivateDemonMode();
            }
            else
            {
                // 切换到正常模式
                ActivateNormalMode();
            }
        }


        private static void ActivateDemonMode()
        {
            // 恶魔模式的逻辑
            UnityEngine.Debug.Log("已切换到恶魔模式 (Switched to Demon Mode)");

            // 恶魔模式下保持现有逻辑
            // 恶魔飞升、强制宣战、恶魔使徒等功能将继续存在
            // 这里的代码保持不变，确保这些功能继续生效
        }

        private static void ActivateNormalMode()
        {
            // 正常模式的逻辑
            UnityEngine.Debug.Log("已切换到正常模式 (Switched to Normal Mode)");

            // 禁用恶魔模式的功能
            DisableDemonModeEvents();
            // 替换为武道大会风格的文本
        }

        private static void DisableDemonModeEvents()
        {

            // 禁用恶魔使徒事件
            _lastEvilRedMageYear = 0;
            EvilRedMage = null;
            EvilRedMageRevertYear = -1;

            // 其他与恶魔模式相关的禁用代码
        }

        // ====== 新增：自动收藏总开关 ======
        private static bool _autoFavoriteEnabled = true;

        // 配置面板勾选时由 ModLoader 调用
        public static void OnAutoFavoriteChanged(bool enabled)
        {
            _autoFavoriteEnabled = enabled;
            UnityEngine.Debug.Log($" AutoFavorite/自动收藏 => {(_autoFavoriteEnabled ? "开启ON" : "关闭OFF")}");

        }

        public static void OnWarIntervalChanged(float yearsFloat)
        {
            int years = Mathf.Clamp(Mathf.RoundToInt(yearsFloat), 5, 2000);
            _warIntervalYears = years;
            UnityEngine.Debug.Log($"强制宣战间隔 => {years} 年 (War Interval)");

            // 重对齐
            var w = World.world;
            if (w != null)
            {
                int curYear = YearNow();
                lastWarYear = curYear - (curYear % _warIntervalYears);
            }
        }

        public static void OnGreatContestIntervalChanged(float yearsFloat)
        {
            int years = Mathf.Clamp(Mathf.RoundToInt(yearsFloat), 60, 2000);
            _greatContestIntervalYears = years;
            UnityEngine.Debug.Log($"大道争锋间隔 => {years} 年 (Great Contest Interval)");

            var w = World.world;
            if (w != null)
            {
                int curYear = YearNow();
                _lastGreatContestYear = curYear - (curYear % _greatContestIntervalYears);
            }
        }



        #endregion

        #region 日志总开关

        // ===== TXT 总开关（默认开）=====
        private static bool _txtLogEnabled = true;
        // ===== 详细日志开关（默认关）=====
        private static bool _txtLogVerboseEnabled = false;

        // 只读公开（给别处判断用）
        public static bool TxtLogEnabled => _txtLogEnabled;
        public static bool TxtLogVerboseEnabled => _txtLogVerboseEnabled;

        // 设置面板回调：TXT 总开关
        public static void OnTxtLogSwitchChanged(bool enabled)
        {
            _txtLogEnabled = enabled;
            UnityEngine.Debug.Log($"[恶魔规则] TXT日志 => {(enabled ? "开启/ON" : "关闭/OFF")}");
        }

        // 设置面板回调：详细日志开关
        public static void OnTxtLogDetailSwitchChanged(bool enabled)
        {
            _txtLogVerboseEnabled = enabled;
            UnityEngine.Debug.Log($"[恶魔规则] 详细日志 => {(enabled ? "开启/ON" : "关闭/OFF")}");
        }

        /// <summary>
        /// 供你在任何写日志前统一判断的“闸门”方法：
        /// 只要返回 false 就别写文件了；true 再写。
        /// </summary>

        // ======= traitAction 里（DemonGameRules2.code.traitAction）追加 =======

        public static void WriteWarCountSummary(string reasonTag = null)
        {
            if (!TxtLogEnabled || !TxtLogVerboseEnabled) return;
            try
            {
                var w = World.world;
                var wm = w?.wars;
                int active = 0;
                int parties = 0;
                if (wm != null)
                {
                    var list = wm.getActiveWars(); // IEnumerable<War>
                    foreach (var war in list)
                    {
                        if (war == null || war.hasEnded()) continue;
                        active++;
                        try
                        {
                            var a = war.main_attacker ?? war.getMainAttacker();
                            var d = war.main_defender ?? war.getMainDefender();
                            if (a != null) parties++;
                            if (d != null) parties++;
                        }
                        catch { }
                    }
                }
                var tag = string.IsNullOrEmpty(reasonTag) ? "战争数量" : $"战争数量/{reasonTag}";
                DemonGameRules.code.WorldLogPatchUtil.Write(
                    $"[世界历{YearNow()}年] [{tag}] 活跃战争: {active} 场，参战方(粗计): {parties}\n"
                );
            }
            catch { }
        }

        /// <summary>
        /// 亡国时“保守猜测”胜者：从仍在进行的战争里找该亡国的对手；
        /// 没找到就返回 null。只是日志参考，不干预逻辑。
        /// </summary>
        public static Kingdom TryGuessVictor(Kingdom dead)
        {
            try
            {
                var wm = World.world?.wars;
                if (dead == null || wm == null) return null;

                foreach (var war in wm.getActiveWars())
                {
                    if (war == null) continue;
                    Kingdom a = null, d = null;
                    try { a = war.main_attacker ?? war.getMainAttacker(); } catch { }
                    try { d = war.main_defender ?? war.getMainDefender(); } catch { }
                    if (a == null || d == null) continue;

                    if (a == dead && d != null && d.isAlive()) return d;
                    if (d == dead && a != null && a.isAlive()) return a;
                }
            }
            catch { }
            return null;
        }



        #endregion


        #region 地图重置

        // —— 标记文件路径 ——
        // 放在 logs 根下放一个“全局上膛”标记；每个槽位自己的目录里放一个“本槽已执行”标记
        private static string GetArmFlagPath()
            => System.IO.Path.Combine(LogDirectory, "RESET_ARM.flag"); // 上膛标记（全局只有一个）

        private static string GetDoneFlagPathForCurrentSlot()
        {
            if (string.IsNullOrEmpty(_currentSessionDir))
            {
                // 兜底：尝试从当前槽位推导目录
                string slotName = "Slot?";
                try { int cur = SaveManager.getCurrentSlot(); if (cur >= 0) slotName = "Slot" + cur; } catch { }
                _currentSessionDir = System.IO.Path.Combine(LogDirectory, $"slot_{slotName}");
                if (!System.IO.Directory.Exists(_currentSessionDir)) System.IO.Directory.CreateDirectory(_currentSessionDir);
            }
            return System.IO.Path.Combine(_currentSessionDir, "RESET_DONE.flag");
        }


        // 兼容：旧布尔仍保留，但只用于日志
        private static bool _yearZeroPurgePending = false;

        // ========== 设置面板回调：只负责“上膛/卸膛” ==========
        // ON：创建全局上膛标记文件（下一次读档时触发一次）
        // OFF：删除上膛标记，同时清掉所有槽位的“已执行”标记，方便下次再 ON 能再次触发
        public static void OnResetLoopChanged(bool enabled)
        {
            _yearZeroPurgePending = enabled;
            try
            {
                var arm = GetArmFlagPath();
                if (enabled)
                {
                    // 仅(重)写 ARM（更新时间戳，用于“再次上膛”判定）
                    System.IO.File.WriteAllText(arm, $"armed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    Debug.Log("[DGR] 世界归零与清图/World Reset 已上膛（标记已写入）/ ARMED (flag written).");
                }
                else
                {
                    // 关：删 ARM，并清理所有槽位 DONE
                    if (System.IO.File.Exists(arm)) System.IO.File.Delete(arm);
                    if (System.IO.Directory.Exists(LogDirectory))
                    {
                        foreach (var dir in System.IO.Directory.GetDirectories(LogDirectory, "slot_*"))
                        {
                            var done = System.IO.Path.Combine(dir, "RESET_DONE.flag");
                            if (System.IO.File.Exists(done)) System.IO.File.Delete(done);
                        }
                    }
                    Debug.Log("[DGR] 世界归零与清图/World Reset 已卸膛；已清理所有槽位 DONE 标记 / DISARMED; all DONE flags cleared.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DGR] 世界归零与清图/World Reset 回调异常 / OnResetLoopChanged exception: {ex.Message}");
            }
            Debug.Log($"[DGR] 世界归零与清图/World Reset => {enabled}（开=ON / 关=OFF）");
        }


        // ========== 读档/新世界就绪时调用：若“上膛”且本槽未执行过，则执行一次 ==========
        // 你现在在 BeginNewGameSession(slotName) 里调用了 ApplyYearZero... —— 把那行改成调用本方法
        public static void ApplyYearZeroAndPurge_ArmedFileOnce()
        {
            if (!Config.game_loaded || World.world == null) return;

            var arm = GetArmFlagPath();
            var done = GetDoneFlagPathForCurrentSlot();

            if (!System.IO.File.Exists(arm)) return; // 未上膛，不执行

            // 若已做过且 ARM 没有“更新上膛”，就不重复
            if (System.IO.File.Exists(done))
            {
                var tArm = System.IO.File.GetLastWriteTimeUtc(arm);
                var tDone = System.IO.File.GetLastWriteTimeUtc(done);
                if (tArm <= tDone)
                {
                    Debug.Log("[DGR] 世界归零与清图/World Reset 已上膛，但本槽已执行（未重新上膛）/ armed but already done for this slot (no rearm).");
                    return;
                }
            }

            try
            {
                ForceWorldYearToZero();
                PurgeAllUnits_TryKill();
                Debug.Log("[DGR] 年归零与清图已执行（标记触发，已刷新时间戳）/ Year-Zero & Purge executed (armed-file, timestamp rearmed).");
                DemonGameRules.code.patch.ClearStatPatchCaches();

                // 标记本槽已执行（更新时间戳）
                System.IO.File.WriteAllText(done, $"done at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                // 注意：不再删除 ARM，让它保持“开启状态”，下次如果你再次“开”(重写ARM)即可再触发
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DGR] 年归零与清图失败 / Year-Zero & Purge failed: {ex.Message}");
            }
        }

        // ========== 把世界年归零 ==========
        private static void ForceWorldYearToZero()
        {
            var mb = MapBox.instance;
            if (mb == null) return;

            var fiMapStats = mb.GetType().GetField("map_stats", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var mapStats = fiMapStats?.GetValue(mb);
            if (mapStats == null) return;

            var fiWorldTime = mapStats.GetType().GetField("world_time", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            fiWorldTime?.SetValue(mapStats, 0.0d);

            var fiHist = mapStats.GetType().GetField("history_current_year", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            fiHist?.SetValue(mapStats, 0);

            var gameStats = mb.game_stats;
            var fiData = gameStats?.GetType().GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
            var dataObj = fiData?.GetValue(gameStats);
            var fiGameTime = dataObj?.GetType().GetField("gameTime", BindingFlags.Public | BindingFlags.Instance);
            fiGameTime?.SetValue(dataObj, 0.0d);
        }

        // ========== 清图（用你自己的 TryKill） ==========
        private static void PurgeAllUnits_TryKill()
        {
            var mb = MapBox.instance;
            if (mb?.units == null) return;

            var list = mb.units.getSimpleList();
            if (list == null || list.Count == 0) return;

            foreach (var a in list.ToArray())
            {
                if (a == null) continue;
                TryKill(a);
            }
        }

        #endregion

        #region    野生单位与超级生命清理
        // ====== 新增：野生单位与“超级生命”清理总开关（默认关闭）======
        private static bool _wildCleanupEnabled = false;

        // 配置面板勾选时调用
        public static void OnWildCleanupChanged(bool enabled)
        {
            _wildCleanupEnabled = enabled;
            Debug.Log($" WildCleanup/野生单位与超级生命清理 => {(_wildCleanupEnabled ? "开启ON" : "关闭OFF")}");
        }

        /// <summary>
        /// 给 Actor.updateAge Postfix 调用的统一入口。
        /// 这里面做：按战力尝试自动收藏、清理 super_health、50% 处死野生阵营单位。
        /// </summary>
        /// 
        // ====== 只清理指定小生物（蜜蜂、蝴蝶、螃蟹、蚂蚱）======
        // 注意：这里匹配的是 UnitAsset.id（如 "bee","butterfly","crab","grasshopper"）
        private static readonly HashSet<string> _crittersToCull =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "bee", "butterfly", "crab", "grasshopper" };

        // 可读性：判断一个单位是否在“可清理小生物”列表内
        private static bool IsCullTarget(Actor a)
        {
            // 保险起见做空判断
            var id = a?.asset?.id;
            if (string.IsNullOrEmpty(id)) return false;
            return _crittersToCull.Contains(id);
        }
        public static void OnActorAgeTick(Actor a)
        {
            if (a == null || !a.hasHealth())
                return;


            if (!_wildCleanupEnabled)
                return;

            // 2) 清除“超级生命”buff
            if (a.hasTrait("super_health"))
            {
                a.removeTrait("super_health");
            }

            // 2) 只清理指定的小生物（90% 概率，按你原先节奏）
            if (IsCullTarget(a))
            {
                // 显式限定 UnityEngine.Random，避免与 System.Random 冲突
                if (UnityEngine.Random.value < 0.90f)
                {
                    TryKill(a); // 复用你已有的封装
                    return;
                }
            }
        }
        #endregion

        #region   恶魔伤害系统（特质化）


        // === 恶魔系特质 ID（别改名，和上面 traits.Init 一致） ===
        //private const string T_DEMON_MASK = "demon_mask";
        private const string T_DEMON_EVASION = "demon_evasion";
        private const string T_DEMON_REGEN = "demon_regen";
        private const string T_DEMON_AMPLIFY = "demon_amplify";
        private const string T_DEMON_ATTACK = "demon_attack";
        private const string T_DEMON_BULWARK = "demon_bulwark";
        private const string T_DEMON_FRENZY = "demon_frenzy";
        private const string T_DEMON_EXECUTE = "demon_execute";
        private const string T_DEMON_BLOODTHIRST = "demon_bloodthirst";

        private static bool Has(Actor a, string tid) => a != null && a.hasTrait(tid);

        // 标志位
        private static bool _isProcessingExchange = false; // 交换过程中不重入
        private static bool _isProcessingHit = false;      // 包裹 getHit，避免前缀递归

        // 参数
        private const float BASE_DMG_WEIGHT = 3f;     // 面板伤基础权重（无“狂热”）
        private const float FRENZY_EXTRA_WEIGHT = 1.5f;   // 恶魔狂热额外权重
        private const float KILL_RATE_PER_100_DEMON = 0.05f;  // 恶魔增幅：每 100 杀 +5%
        private const float KILL_RATE_CAP = 5.00f;  // 倍率封顶 +500%
        private const float TARGET_HP_HARD_CAP = 0.15f;  // 单击上限（受击者有“壁障”才启用）
        private const float HEAL_MAX_FRACTION_BASE = 0.05f;  // 基础回血上限 5%
        private const float HEAL_MAX_FRACTION_BLOOD = 0.08f;  // 嗜血：回血上限 8%
        private const float DEMON_EVADE_CHANCE = 0.20f;  // 恶魔闪避 20%
        private const int MIN_DAMAGE = 1;

        // ===== 恶魔AOE配置与实现（只伤单位，不改地形） =====
        private const int DEMON_AOE_RADIUS_TILES = 15;    // 半径（格）
        private const bool DEMON_AOE_HIT_FLYERS = true; // 是否命中飞行单位
        private const bool DEMON_AOE_SHOW_FX = true; // 是否播放闪电FX
        private static readonly TerraformOptions _demonAoeOpts = new TerraformOptions
        {
            applies_to_high_flyers = DEMON_AOE_HIT_FLYERS,
            attack_type = AttackType.Other           // 攻击类型：通用，不影响地形
        };

        // 只对“单位”造成伤害的范围AOE，不改地形，不击退
        private static void DemonAoeHit(WorldTile center, int radiusTiles, int damage, BaseSimObject byWho)
        {
            if (center == null || radiusTiles <= 0 || damage <= 0) return;

            // 表现：仅FX，不调用任何 MapAction.damageWorld
            if (DEMON_AOE_SHOW_FX)
            {
                string fx = radiusTiles >= 16 ? "fx_lightning_big" : (radiusTiles >= 10 ? "fx_lightning_medium" : "fx_lightning_small");
                EffectsLibrary.spawnAtTile(fx, center, 0.35f);
            }

            var mb = MapBox.instance;
            if (mb == null) return;

            // 确保选项最新（可热改）
            _demonAoeOpts.applies_to_high_flyers = DEMON_AOE_HIT_FLYERS;
            _demonAoeOpts.attack_type = AttackType.Other;

            // 不击退，只伤害；最后一个参数 false 表示不做额外地形处理
            const float forceAmount = 0f;
            const bool forceOut = true; // force=0 时无效
            mb.applyForceOnTile(center, radiusTiles, forceAmount, forceOut, damage, null, byWho, _demonAoeOpts, false);
        }


        // —— 统一入口：被 getHit 前缀调用 ——
        // 触发条件：双方任一拥有 demon_mask
        public static void ExecuteDamageExchange(BaseSimObject source, BaseSimObject target)
        {
            if (_isProcessingExchange) return;

            try
            {
                if (source == null || !source.isActor() || !source.hasHealth()) return;
                if (target == null || !target.isActor() || !target.hasHealth()) return;

                Actor B = source.a; // A 视为“源”，先吃对手伤害，再反打
                Actor A = target.a;

                if (A == null || B == null) return;

                // 没有恶魔面具就不启用本系统
                bool demonOn = Has(A, T_DEMON_MASK) || Has(B, T_DEMON_MASK);
                if (!demonOn) return;

                int Ak = A.data?.kills ?? 0;
                int Bk = B.data?.kills ?? 0;

                _isProcessingExchange = true;

                // ===== A 先吃一发来自 B 的伤害（可被 A 的“闪避”闪掉）=====

                int dmgToA = CalculateDemonDamage(B, A, Bk);

                bool aEvaded = Has(A, T_DEMON_EVASION) && UnityEngine.Random.value < DEMON_EVADE_CHANCE;
                if (!aEvaded)
                {
                    DemonAoeHit(A.current_tile, DEMON_AOE_RADIUS_TILES, dmgToA, target); // 范围伤害：以 A 所在地块为中心，伤害来自 B
                    try 
                    {
                        _isProcessingHit = true;
                        
                        A.getHit(dmgToA, true, AttackType.Other, target, false, false, false); 
                    }
                    finally { _isProcessingHit = false; }

                }

                if (A.getHealth() <= 0) return;

                // ===== A 回血（仅 A 拥有“恶魔回血”才回） =====
                if (Has(A, T_DEMON_REGEN) && A.getHealth() > 100 && A.getHealth() < A.getMaxHealth())
                {
                    int heal = Mathf.Max(1, Ak / 2);
                    float capFrac = Has(A, T_DEMON_BLOODTHIRST) ? HEAL_MAX_FRACTION_BLOOD : HEAL_MAX_FRACTION_BASE;
                    int cap = Mathf.Max(1, Mathf.RoundToInt(A.getMaxHealth() * capFrac));
                    heal = Mathf.Clamp(heal, 1, cap);

                    int room = Mathf.Max(0, A.getMaxHealth() - A.getHealth() - 1);
                    if (room > 0) A.restoreHealth(Mathf.Min(heal, room));
                }

                // ===== A 反打 B（可被 B 的“闪避”闪掉） =====
                int dmgToB = CalculateDemonDamage(A, B, Ak);

                bool bEvaded = Has(B, T_DEMON_EVASION) && UnityEngine.Random.value < DEMON_EVADE_CHANCE;
                if (!bEvaded)
                {
                    DemonAoeHit(B.current_tile, DEMON_AOE_RADIUS_TILES, dmgToB, source);// 范围伤害：以 B 所在地块为中心，伤害来自 A
                    try 
                    { 
                        _isProcessingHit = true;
                        
                        B.getHit(dmgToB, true, AttackType.Other, source, false, false, false); 
                    }

                    finally { _isProcessingHit = false; }
                }

                // ===== B 回血（仅 B 拥有“恶魔回血”才回） =====
                if (Has(B, T_DEMON_REGEN) && B.getHealth() > 100 && B.getHealth() < B.getMaxHealth())
                {
                    int heal = Mathf.Max(1, Bk / 2);
                    float capFrac = Has(B, T_DEMON_BLOODTHIRST) ? HEAL_MAX_FRACTION_BLOOD : HEAL_MAX_FRACTION_BASE;
                    int cap = Mathf.Max(1, Mathf.RoundToInt(B.getMaxHealth() * capFrac));
                    heal = Mathf.Clamp(heal, 1, cap);

                    int room = Mathf.Max(0, B.getMaxHealth() - B.getHealth() - 1);
                    if (room > 0) B.restoreHealth(Mathf.Min(heal, room));
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[恶魔伤害交换异常] {ex.Message}");
            }
            finally
            {
                _isProcessingExchange = false;
            }
        }

        // 计算“恶魔伤害系统”的伤害（完全特质化）
        private static int CalculateDemonDamage(Actor from, Actor to, int fromKills)
        {
            if (from == null) return MIN_DAMAGE;

            // 1) 面板伤害读取
            int panelDmg = 0;
            try
            {
                float raw = from.stats["damage"];
                panelDmg = raw > 0f ? Mathf.RoundToInt(raw) : 0;
            }
            catch { panelDmg = 0; }

            // 2) 面板权重：有“恶魔狂热”则提高权重
            float weight = BASE_DMG_WEIGHT + (Has(from, T_DEMON_FRENZY) ? FRENZY_EXTRA_WEIGHT : 0f);

            // 3) 基础伤害：面板*权重 + 击杀直加
            // 3) 基础伤害：面板*权重 + 击杀直加 + 固定额外200
            int baseDamage = Mathf.Max(
                MIN_DAMAGE,
                Mathf.RoundToInt(panelDmg * weight) + Mathf.Max(0, fromKills) + 200
            );

            // 4) 击杀倍率：持有“恶魔增幅”才启用，每 100 杀 +5%，封顶 +500%
            float mult = 1f;
            if (Has(from, T_DEMON_AMPLIFY))
            {
                float killRate = Mathf.Min((fromKills / 100f) * KILL_RATE_PER_100_DEMON, KILL_RATE_CAP);
                mult += killRate;
            }

            int finalDamage = Mathf.Max(MIN_DAMAGE, Mathf.RoundToInt(baseDamage * mult));

            // 5) 保底：持有“恶魔攻击”才启用 ≥ 击杀数 的保底
            if (Has(from, T_DEMON_ATTACK))
                finalDamage = Mathf.Max(finalDamage, Mathf.Max(0, fromKills));

            // 6) 斩杀：当目标血量 ≤20%，且攻击者有“恶魔斩首”时 ×1.25
            if (to != null && Has(from, T_DEMON_EXECUTE))
            {
                int toMax = 0, toCur = 0;
                try { toMax = Mathf.Max(1, to.getMaxHealth()); toCur = Mathf.Max(0, to.getHealth()); } catch { }
                if (toMax > 0 && (toCur <= toMax * 0.2f))
                    finalDamage = Mathf.Max(MIN_DAMAGE, Mathf.RoundToInt(finalDamage * 1.25f));
            }

            // 7) 单击硬上限：仅当“受击者”拥有“恶魔壁障”时启用
            if (to != null && Has(to, T_DEMON_BULWARK))
            {
                int toMax = 0;
                try { toMax = Mathf.Max(0, to.getMaxHealth()); } catch { }
                if (toMax > 0)
                {
                    int cap = Mathf.Max(MIN_DAMAGE, Mathf.RoundToInt(toMax * TARGET_HP_HARD_CAP));
                    if (finalDamage > cap) finalDamage = cap;
                }
            }

            return finalDamage;
        }

        #endregion


        #region 强杀机制（无尸体 / 不计数 / 不日志）
        public static void TryKill(Actor a)
        {
            if (a == null) return;

            // 先尝试调用 Actor 的私有 die(bool, AttackType, bool, bool)
            try
            {
                // 优先用 Harmony 的 AccessTools，参数签名要精确匹配
                var mi = HarmonyLib.AccessTools.Method(
                    typeof(Actor),
                    "die",
                    new System.Type[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) }
                );

                if (mi != null)
                {
                    // pDestroy=true（不留尸体），pType=Other（中性类型），pCountDeath=false（不计数），pLogFavorite=false（不写收藏日志）
                    mi.Invoke(a, new object[] { true, AttackType.Other, false, false });
                    return;
                }
            }
            catch
            {
                // 反射失败就走兜底
            }

            // 兜底：如果还活着，就直接移除（不走死亡流水线）
            try
            {
                if (a != null && !a.isRekt() && a.hasHealth())
                {
                    ActionLibrary.removeUnit(a);
                }
            }
            catch { /* 忽略 */ }
        }
        #endregion


        #region 城市叛乱独立机制
        public static void TryRandomWar()
        {
            if (World.world == null || World.world.wars == null) return;

            List<Kingdom> kingdoms = World.world.kingdoms.list;
            if (kingdoms == null || kingdoms.Count == 0) return;

            int y = YearNow(); // 年份，一次拿够

            // ========= 新增：仅剩一个王国时，改为强制内部叛乱 =========
            if (kingdoms.Count == 1)
            {
                Kingdom solo = kingdoms[0];
                if (solo != null && solo.isAlive())
                {
                    int cityCount = solo.cities != null ? solo.cities.Count : 0;
                    if (cityCount >= 2)
                    {
                        UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][单国局面] 世界只剩 {solo.data.name}，改为触发内部叛乱。");
                        WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-叛乱", $"世界仅剩 {WorldLogPatchUtil.K(solo)}，强制触发内部叛乱"));
                        TriggerCityRebellion(solo);
                    }
                    else
                    {
                        UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][单国局面] 仅剩 {solo.data.name} 且城市数={cityCount}，无法触发叛乱（至少需要2座城）。");
                    }
                }
                return;
            }
            // =====================================================

            // 2个及以上王国的正常分支（保留你的既有行为）
            List<Kingdom> validAttackers = kingdoms
                .Where(k => k != null && k.isAlive() && k.cities != null && k.cities.Count >= 2)
                .ToList();
            if (validAttackers.Count == 0)
                validAttackers = kingdoms.Where(k => k != null && k.isAlive()).ToList();

            if (validAttackers.Count == 0) return;

            WarManager warManager = World.world.wars;
            Kingdom attacker = validAttackers[Randy.randomInt(0, validAttackers.Count)];

            // 找目标
            Kingdom defender = FindSuitableWarTarget(attacker, kingdoms, warManager);
            WarTypeAsset warType = GetDefaultWarType();

            // 没有任何目标或拿不到战争类型 —— 直接叛乱
            if (defender == null || warType == null)
            {
                UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][无宣战目标/类型] {attacker.data.name} 无法宣战，改为触发叛乱。");
                WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-叛乱", $"{WorldLogPatchUtil.K(attacker)} 无法找到宣战目标，触发内部叛乱"));

                if (attacker.cities != null && attacker.cities.Count >= 2)
                    TriggerCityRebellion(attacker);
                else
                    UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][叛乱跳过] {attacker.data.name} 城市不足，避免叛乱导致异常。");
                return;
            }

            // 有目标就尝试宣战
            War newWar = warManager.newWar(attacker, defender, warType);
            if (newWar != null)
            {
                UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉]{attacker.data.name} 强制向 {defender.data.name} 宣战！战争名称: {newWar.data.name}");
                WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-宣战", $"{WorldLogPatchUtil.K(attacker)} 强制向 {WorldLogPatchUtil.K(defender)} 宣战"));
                return;
            }

            // 宣战失败则直接叛乱
            UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][宣战失败] {attacker.data.name} 无法对 {defender.data.name} 宣战，改为触发叛乱。");
            WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-叛乱", $"{WorldLogPatchUtil.K(attacker)} 对 {WorldLogPatchUtil.K(defender)} 宣战失败，触发内部叛乱"));

            if (attacker.cities != null && attacker.cities.Count >= 2)
                TriggerCityRebellion(attacker);
            else
                UnityEngine.Debug.Log($"[世界历{y}年][恶魔干涉][叛乱跳过] {attacker.data.name} 城市不足，避免叛乱导致异常。");
        }



        private static void TriggerCityRebellion(Kingdom targetKingdom)
        {
            int y = YearNow();
            if (targetKingdom == null || targetKingdom.cities == null || targetKingdom.cities.Count == 0)
            {
                UnityEngine.Debug.Log($"[世界历{y}年]{targetKingdom?.data.name} 没有城市，无法触发叛乱。");
                return;
            }

            List<City> rebelliousCities = targetKingdom.cities
                .Where(c => c.getLoyalty() < 30)
                .ToList();

            if (rebelliousCities.Count == 0)
                rebelliousCities = new List<City>(targetKingdom.cities);

            int rebellionCount = Randy.randomInt(1, Mathf.Min(20, rebelliousCities.Count) + 1);
            rebelliousCities.Shuffle();
            List<City> citiesToRebel = rebelliousCities.Take(rebellionCount).ToList();

            UnityEngine.Debug.Log($"[世界历{y}年]{targetKingdom.data.name} 发生叛乱！{rebellionCount}个城市宣布独立。");
            WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-叛乱", $"{WorldLogPatchUtil.K(targetKingdom)} 发生叛乱，{rebellionCount}个城市宣布独立"));

            foreach (City rebelCity in citiesToRebel)
            {
                Actor leader = FindCityLeader(rebelCity);
                if (leader != null)
                {
                    Kingdom newKingdom = rebelCity.makeOwnKingdom(leader, true, false);
                    if (newKingdom != null)
                    {
                        UnityEngine.Debug.Log($"[世界历{y}年]{rebelCity.data.name} 成功独立，成立 {newKingdom.data.name}，领导者: {leader.name}");
                        WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-建国/建制", $"{WorldLogPatchUtil.C(rebelCity)} 独立为 {WorldLogPatchUtil.K(newKingdom)}，领导者: {WorldLogPatchUtil.U(leader)}"));

                        WarTypeAsset warType = GetDefaultWarType();
                        if (warType != null)
                        {
                            World.world.wars.newWar(newKingdom, targetKingdom, warType);
                            WorldLogPatchUtil.Write(WorldLogPatchUtil.Stamp("恶魔干涉-宣战", $"{WorldLogPatchUtil.K(newKingdom)} 独立后立即向 {WorldLogPatchUtil.K(targetKingdom)} 宣战"));
                        }
                    }
                }
                else
                {
                    UnityEngine.Debug.Log($"[世界历{y}年]{rebelCity.data.name} 没有合适的领导者，叛乱失败。");
                }
            }
        }

        // 叛军领袖：优先城主；否则城内最能打且活着的单位
        private static Actor FindCityLeader(City city)
        {
            if (city == null) return null;

            try
            {
                if (city.leader != null && city.leader.isAlive())
                    return city.leader;

                if (city.units != null && city.units.Count > 0)
                {
                    return city.units
                        .Where(a => a != null && a.isAlive())
                        .OrderByDescending(a =>
                        {
                            try { return a.stats != null ? a.stats["health"] : 0f; }
                            catch { return 0f; }
                        })
                        .FirstOrDefault();
                }
            }
            catch { /* 静默 */ }

            return null;
        }

        // 默认战争类型：先拿 WarTypeLibrary.normal；兜底从资源库取 "normal"
        private static WarTypeAsset GetDefaultWarType()
        {
            try
            {
                if (WarTypeLibrary.normal != null)
                    return WarTypeLibrary.normal;

                if (AssetManager.war_types_library != null)
                    return AssetManager.war_types_library.get("normal");
            }
            catch { /* 静默 */ }

            return null;
        }

        // 选宣战目标：排除自己/已在交战/已亡；随机一个可打的
        private static Kingdom FindSuitableWarTarget(Kingdom attacker, List<Kingdom> kingdoms, WarManager warManager)
        {
            if (attacker == null || kingdoms == null || warManager == null) return null;

            try
            {
                var possibleTargets = kingdoms.Where(k =>
                    k != null &&
                    k != attacker &&
                    k.isAlive() &&
                    !warManager.isInWarWith(attacker, k)
                ).ToList();

                if (possibleTargets.Count == 0) return null;
                return possibleTargets[Randy.randomInt(0, possibleTargets.Count)];
            }
            catch { /* 静默 */ }

            return null;
        }


        /// <summary>
        /// 定期把当前 WarManager 里正进行的战争抓出来，与上一次快照对比：
        /// 新增 => “宣战”；丢失 => “停战/战争结束”
        /// 完全不依赖具体方法名，适配性最好。
        /// </summary>
        /// <summary>
        /// 快照对比（配对键 + 活跃状态判定）
        /// - 当前活跃集合：按【两国ID排序后的 pairKey】收集（例如 "12-34"）
        /// - 旧快照：上一轮活跃 pairKey 集合
        /// - 新出现的 pairKey => 宣战
        /// - 旧有但当前不活跃的 pairKey => 停战/战争结束
        /// </summary>
        private static readonly HashSet<string> _warPairsLive = new HashSet<string>();

        private static void ScanWarDiffs()
        {
            try
            {
                var w = World.world;
                if (w == null || w.wars == null) return;

                float nowSec = (float)(w.getCurWorldTime() * 60.0);
                if (nowSec < _lastWarScanWorldSeconds + WAR_SCAN_INTERVAL_SECONDS) return;
                _lastWarScanWorldSeconds = nowSec;

                // 1) 当前活跃战争 -> pairKey 集合
                var currentActivePairs = new HashSet<string>();
                foreach (var war in GetAllAliveWars())
                {
                    if (war == null || war.hasEnded()) continue;

                    var k1 = war.main_attacker ?? war.getMainAttacker();
                    var k2 = war.main_defender ?? war.getMainDefender();
                    if (k1 == null || k2 == null) continue;

                    long idA = k1.id, idB = k2.id;
                    if (idA <= 0 || idB <= 0) continue;
                    if (idA > idB) { var t = idA; idA = idB; idB = t; }
                    currentActivePairs.Add($"{idA}-{idB}");
                }

                // 2) 首帧只建快照
                if (!_warScanPrimed)
                {
                    _warPairsLive.Clear();
                    foreach (var p in currentActivePairs) _warPairsLive.Add(p);
                    _warScanPrimed = true;
                    return;
                }

                int year = YearNow();

                // 3) 新增 => 宣战
                foreach (var pair in currentActivePairs)
                {
                    if (_warPairsLive.Contains(pair)) continue;

                    if (TryResolvePair(pair, w, out var ka, out var kb) &&
                        WorldEventGuard.ShouldLogWarStart(ka, kb, year))
                    {
                        WriteWorldEventSilently($"[世界历{year}年] [宣战] {DemonGameRules.code.WorldLogPatchUtil.K(ka)} vs {DemonGameRules.code.WorldLogPatchUtil.K(kb)}\n");
                    }
                }

                // 4) 旧有但消失 => 停战/结束
                foreach (var pair in _warPairsLive)
                {
                    if (currentActivePairs.Contains(pair)) continue;

                    if (TryResolvePair(pair, w, out var ka, out var kb) &&
                        WorldEventGuard.ShouldLogWarEnd(ka, kb, year))
                    {
                        WriteWorldEventSilently($"[世界历{year}年] [停战/战争结束] {DemonGameRules.code.WorldLogPatchUtil.K(ka)} vs {DemonGameRules.code.WorldLogPatchUtil.K(kb)}\n");
                    }
                }

                // 5) 覆盖快照
                _warPairsLive.Clear();
                foreach (var p in currentActivePairs) _warPairsLive.Add(p);
            }
            catch { /* 静默 */ }
        }






        /// <summary>从 WarManager 里尽量取出“正在进行”的 War 列表（字段名多版本兼容）。</summary>
        private static List<War> GetAllAliveWars()
        {
            var wm = World.world?.wars;
            if (wm == null) return new List<War>();
            // 直接用官方的活跃枚举
            return wm.getActiveWars().ToList(); // IEnumerable<War> -> List<War>
        }


        /// <summary>多重兜底拿 War 的唯一 ID。</summary>
        // 修正后（参数类型用 MapBox；调用处传入的 w 本来就是 MapBox）
        private static bool TryResolvePair(string pair, MapBox w, out Kingdom a, out Kingdom b)
        {
            a = null; b = null;
            var parts = pair.Split('-');
            if (parts.Length != 2) return false;
            if (!long.TryParse(parts[0], out var ida)) return false;
            if (!long.TryParse(parts[1], out var idb)) return false;

            foreach (var k in w.kingdoms.list)
            {
                if (k == null) continue;
                if (k.id == ida) a = k;
                else if (k.id == idb) b = k;
            }
            return a != null && b != null;
        }


        #endregion



        #region 随机生成
        private static readonly List<string> _majorRaces = new List<string>
        {
            "human", "elf", "orc", "dwarf"
        };

        private static readonly List<string> _otherUnits = new List<string>
        {
            "sheep","skeleton","white_mage","alien","necromancer","snowman","lil_pumpkin",
            "bandit",
            "civ_cat","civ_dog","civ_chicken","civ_rabbit","civ_monkey","civ_fox","civ_sheep","civ_cow","civ_armadillo","civ_wolf",
            "civ_bear","civ_rhino","civ_buffalo","civ_hyena","civ_rat","civ_alpaca","civ_capybara","civ_goat","civ_scorpion","civ_crab",
            "civ_penguin","civ_turtle","civ_crocodile","civ_snake","civ_frog","civ_liliar","civ_garlic_man","civ_lemon_man",
            "civ_acid_gentleman","civ_crystal_golem","civ_candy_man","civ_beetle",
            "bear","bee","beetle","buffalo","butterfly","smore","cat","chicken","cow","crab","crocodile",
            "druid","fairy","fire_skull","fly","flower_bud","lemon_snail","garl","fox","frog","ghost",
            "grasshopper","hyena","jumpy_skull","monkey","penguin","plague_doctor","rabbit","raccoon","seal","ostrich",
            "unicorn","rat","rhino","snake","turtle","wolf"
        };

        public static void SpawnRandomUnit()
        {

            if (!_autoSpawnEnabled) return; // 新增：没开就不生成

            string tID;
            if (UnityEngine.Random.value < 0.9f)
            {
                int randomMajorIndex = UnityEngine.Random.Range(0, _majorRaces.Count);
                tID = _majorRaces[randomMajorIndex];
            }
            else
            {
                int randomOtherIndex = UnityEngine.Random.Range(0, _otherUnits.Count);
                tID = _otherUnits[randomOtherIndex];
            }

            var hillTiles = TileLibrary.hills.getCurrentTiles();
            WorldTile tTile = null;
            if (hillTiles != null && hillTiles.Count > 0)
            {
                int randomTileIndex = UnityEngine.Random.Range(0, hillTiles.Count);
                tTile = hillTiles[randomTileIndex];
            }

            bool tMiracleSpawn = UnityEngine.Random.value < 0.1f;

            if (tTile != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    World.world.units.spawnNewUnit(tID, tTile, false, tMiracleSpawn, 6f, null, false);
                }
            }
        }
        #endregion

        #region UI榜单

        public static string BuildLeaderboardRichText()
        {
            if (World.world == null)
                return "<color=#AAAAAA>世界还没加载，想看榜单也得有世界。</color>";

            // 先确保内存里的两份榜是最新的
            EnsureLivingFirstPlace();
            UpdatePowerLeaderboard();

            int year = YearNow();
            int living = World.world.units?.Count(a => a != null && a.data != null && a.data.id > 0 && a.hasHealth()) ?? 0;

            var sb = new StringBuilder();
            sb.AppendLine($"<b>【世界历{year}年】</b>");
            sb.AppendLine($"存活单位数量: {living}\n");

            // —— 战力榜 ——
            sb.AppendLine("<color=#FF9900><b>【战力排行榜/Power Leaderboard】</b></color>");
            if (powerLeaderboard != null && powerLeaderboard.Count > 0)
            {
                for (int i = 0; i < Mathf.Min(powerLeaderboard.Count, 10); i++)
                {
                    var e = powerLeaderboard[i];
                    string rankColor = i == 0 ? "#FFD700" : i == 1 ? "#00CED1" : i == 2 ? "#CD7F32" : "#DDDDDD";
                    string ageInfo = GetUnitAgeInfo(e.Key);
                    sb.AppendLine($"{i + 1}. <color={rankColor}>{e.Key}</color> - <color=#FFCC00>{e.Value}</color><color=#AAAAAA>{ageInfo}</color>");
                }
            }
            else sb.AppendLine("<color=#AAAAAA>暂无上榜者/No rankers yet</color>");

            sb.AppendLine("<color=#666666>────────────────────</color>");

            // —— 击杀榜 ——
            sb.AppendLine("<color=#66AAFF><b>【杀戮排行榜/Kills Leaderboard】</b></color>");
            if (killLeaderboard != null && killLeaderboard.Count > 0)
            {
                for (int i = 0; i < Mathf.Min(killLeaderboard.Count, 10); i++)
                {
                    var e = killLeaderboard[i];
                    string rankColor = i == 0 ? "#FFD700" : i == 1 ? "#00CED1" : i == 2 ? "#CD7F32" : (IsUnitAlive(e.Key) ? "#DDDDDD" : "#AAAAAA");
                    string ageInfo = GetUnitAgeInfo(e.Key);
                    sb.AppendLine($"{i + 1}. <color={rankColor}>{e.Key}</color> - <color={GetKillColor(e.Value)}>{e.Value}</color><color=#AAAAAA>{ageInfo}</color>");
                }
            }
            else sb.AppendLine("<color=#AAAAAA>暂无上榜者/No rankers yet</color>");

            return sb.ToString();
        }

        #endregion




    }
}

