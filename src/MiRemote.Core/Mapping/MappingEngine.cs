using System;
using System.Collections.Generic;
using System.Linq;

namespace MiRemote.Core
{
    /// <summary>
    /// TapEngine/按键路由读取的分流快照。Windows 版与 macOS 版不同：
    /// 方向键不是「系统原生直通」，而是由我们自己在按下瞬间合成 keydown、
    /// 松开时合成 keyup，并自己实现自动重复（Raw Input 拦截后系统不再给重复）。
    /// </summary>
    public struct TapRoute
    {
        /// <summary>生效层（0=基础层）。</summary>
        public int EffectiveLayer;
        /// <summary>ok 键是否物理按住（手势前置条件）。</summary>
        public bool OkDown;
        /// <summary>「纯原生」的方向键集合：基础文字输入态。</summary>
        public HashSet<RemoteKey> NativeDirections;
        /// <summary>浮层捕获态：所有键都喂引擎（转发给浮层）。</summary>
        public bool UiCapture;
        /// <summary>鼠标模式捕获态。</summary>
        public bool MouseCapture;
        /// <summary>任务视图（Task View）态：左右切虚拟桌面。</summary>
        public bool SystemUiCapture;

        /// <summary>触发时补充最新状态（鼠标模式/任务视图是实时态，不在引擎快照里维护）。</summary>
        public TapRoute WithRuntimeState()
        {
            var r = this;
            r.MouseCapture = MouseMode.Instance.IsActive;
            r.SystemUiCapture = TransientSystemUI.IsTaskViewActive();
            return r;
        }
    }

    /// <summary>任务视图（Win+Tab）进入/退出的瞬时状态，与 macOS 版 TransientSystemUI 同职责。</summary>
    public static class TransientSystemUI
    {
        private static readonly object Gate = new();
        private static bool _entered;
        private static long _enteredAtMs;
        private const long WindowMs = 20_000;

        public static bool IsTaskViewActive()
        {
            lock (Gate)
            {
                if (!_entered) return false;
                if (Environment.TickCount64 - _enteredAtMs > WindowMs) { _entered = false; return false; }
                return true;
            }
        }

        public static void MarkEntered()
        {
            lock (Gate) { _entered = true; _enteredAtMs = Environment.TickCount64; }
        }

        /// <summary>消费一次「退出」语义：只在任务视图态里返回 true 一次。</summary>
        public static bool ConsumeExit()
        {
            lock (Gate)
            {
                if (!_entered) return false;
                _entered = false;
                return true;
            }
        }

        public static void Clear()
        {
            lock (Gate) _entered = false;
        }
    }

    public interface IMappingEngineDelegate
    {
        void MappingLog(string message);
        void LayerChanged(int layer);
    }

    /// <summary>
    /// 按键映射状态机（移植 macOS 版 MappingEngine，语义逐条对齐）。
    /// 触发模型：tap / hold / double / gesture(OK+方向) / layer。
    /// </summary>
    public sealed class MappingEngine
    {
        private enum Phase { Idle, Down, WaitingDouble, Consumed }

        private struct KeyState
        {
            public Phase Phase;
            public int Seq;          // 定时器令牌：任何状态迁移都自增以作废在途回调
            public bool HoldFired;
            public bool SuppressTap;
        }

        private readonly IActionRunner _runner;
        private readonly IMappingEngineDelegate? _delegate;
        private readonly IScheduler _scheduler;
        private readonly object _gate = new();

        private MappingConfig _config;
        private Dictionary<string, KeyBinding> _globalProfile;
        private Dictionary<string, KeyBinding>? _activeOverlay;
        private string? _activeAppKey;

        private readonly Dictionary<RemoteKey, KeyState> _states = new();

        private int _lockedLayer;
        private int? _momentaryLayer;
        private RemoteKey? _momentaryOwner;
        private int _lastNotifiedLayer;

        /// <summary>锁定层长时无操作自动回基础层，防止用户忘记退出。</summary>
        public const int LockedLayerIdleMs = 20_000;
        private int _lockedLayerIdleSeq;

        private int EffectiveLayer => _momentaryLayer ?? _lockedLayer;

        private bool _okPhysicallyDown;
        private Action<ButtonEvent>? _overlayHandler;

        /// <summary>长按菜单键 1.5s = 逃生键：清所有层/模式/浮层，回基础状态（硬编码，不受配置影响）。</summary>
        private const int EscapeHoldMs = 1500;
        private int _escapeSeq;

        /// <summary>逃生触发时的 UI 侧钩子（关闭所有浮层/HUD）。</summary>
        public Action? OnEscapeHatch { get; set; }

        private bool _suspended;
        public bool IsSuspended { get { lock (_gate) return _suspended; } }
        public Action<bool>? OnSuspendChanged { get; set; }

        public MappingEngine(MappingConfig config, IActionRunner runner, IMappingEngineDelegate? del = null, IScheduler? scheduler = null)
        {
            _config = config;
            _runner = runner;
            _delegate = del;
            _scheduler = scheduler ?? new RealScheduler();
            _globalProfile = ProfileOf(config, "global");
            PublishTapRoute();
        }

        private static Dictionary<string, KeyBinding> ProfileOf(MappingConfig config, string name) =>
            config.Profiles.TryGetValue(name, out var p) ? p : new Dictionary<string, KeyBinding>();

        // ---------------- 对外 API ----------------

        public void SetConfig(MappingConfig config) => Run(() => SetConfigCore(config));

        public void SetActiveApp(string? appKey) => Run(() => SetActiveAppCore(appKey));

        public void Handle(ButtonEvent e) => Run(() => HandleCore(e));

        public void ResetInputState(string reason) => Run(() => ResetInputStateCore(reason));

        /// <summary>浮层键捕获：非 null 时按键不进状态机，转交浮层处理。</summary>
        public void SetOverlayCapture(Action<ButtonEvent>? handler) =>
            Run(() =>
            {
                _overlayHandler = handler;
                ResetInputStateCore(handler is not null ? "浮层打开" : "浮层关闭");
            });

        public void SetSuspended(bool suspended) =>
            Run(() =>
            {
                lock (_gate)
                {
                    if (_suspended == suspended) return;
                    _suspended = suspended;
                }
                if (suspended)
                {
                    _lockedLayer = 0;
                    _lockedLayerIdleSeq++;
                    MouseMode.Instance.Deactivate();
                }
                ResetInputStateCore(suspended ? "遥控暂停" : "遥控恢复");
                OnSuspendChanged?.Invoke(suspended);
            });

        private void Run(Action work)
        {
            lock (_gate) work();
        }

        // ---------------- 分流快照 ----------------

        private TapRoute _route;

        public TapRoute Route
        {
            get { lock (_gate) return _route.WithRuntimeState(); }
        }

        public MappingConfig Config
        {
            get { lock (_gate) return _config; }
        }

        private void PublishTapRoute()
        {
            _route = new TapRoute
            {
                EffectiveLayer = EffectiveLayer,
                OkDown = _okPhysicallyDown,
                NativeDirections = new HashSet<RemoteKey> { RemoteKey.Up, RemoteKey.Down, RemoteKey.Left, RemoteKey.Right },
                UiCapture = _overlayHandler is not null,
            };
        }

        // ---------------- 核心实现 ----------------

        private void SetConfigCore(MappingConfig config)
        {
            _config = config;
            _globalProfile = ProfileOf(config, "global");
            _activeOverlay = _activeAppKey is not null && config.Profiles.TryGetValue(_activeAppKey, out var p) ? p : null;
            PublishTapRoute();
            Log($"CONFIG reloaded holdMs={config.Settings.HoldMs} doubleMs={config.Settings.DoubleMs}");
        }

        private void SetActiveAppCore(string? appKey)
        {
            _activeAppKey = appKey;
            if (appKey is not null && _config.Profiles.TryGetValue(appKey, out var p))
            {
                _activeOverlay = p;
                Log($"PROFILE overlay={appKey}");
            }
            else
            {
                _activeOverlay = null;
                Log($"PROFILE global (app={appKey ?? "nil"})");
            }
            PublishTapRoute();
        }

        private void ResetInputStateCore(string reason)
        {
            foreach (var key in new List<RemoteKey>(_states.Keys))
            {
                var st = _states[key];
                st.Seq++;          // 保留条目、只 bump seq：清空会让新按压的令牌撞值
                st.Phase = Phase.Idle;
                st.HoldFired = false;
                st.SuppressTap = false;
                _states[key] = st;
            }
            _okPhysicallyDown = false;
            _momentaryLayer = null;
            _momentaryOwner = null;
            RecomputeLayer();
            PublishTapRoute();
            Log($"RESET input state ({reason})");
        }

        private void HandleCore(ButtonEvent e)
        {
            lock (_gate)
            {
                if (_suspended) return;
                if (_lockedLayer != 0) RestartLockedLayerIdleTimer();

                // 逃生键跟踪必须在浮层捕获分支之前：浮层里长按菜单也要能逃生。
                if (e.Key == RemoteKey.Menu)
                {
                    _escapeSeq++;
                    if (e.IsDown)
                    {
                        int token = _escapeSeq;
                        ScheduleAfter(EscapeHoldMs, () => OnEscapeTimer(token));
                    }
                }

                if (_overlayHandler is not null)
                {
                    var handler = _overlayHandler;
                    _scheduler.PostUi(() => handler(e));
                    return;
                }

                // 鼠标模式优先消费方向/OK/菜单/返回。
                if (MouseMode.Instance.Handle(e.Key, e.IsDown)) return;

                // 任务视图态：左右切虚拟桌面，Home/菜单退出。
                if (IsDirection(e.Key) && TransientSystemUI.IsTaskViewActive())
                {
                    if (e.IsDown)
                    {
                        switch (e.Key)
                        {
                            case RemoteKey.Left: _runner.Run(MAction.SystemAction("space_left")); break;
                            case RemoteKey.Right: _runner.Run(MAction.SystemAction("space_right")); break;
                            case RemoteKey.Up: _runner.Run(MAction.KeyStroke("up_arrow")); break;
                            case RemoteKey.Down: _runner.Run(MAction.KeyStroke("down_arrow")); break;
                        }
                        TransientSystemUI.MarkEntered();
                        Log($"TASK VIEW {e.Key}");
                    }
                    return;
                }

                if (e.Key == RemoteKey.Ok && _okPhysicallyDown != e.IsDown)
                {
                    _okPhysicallyDown = e.IsDown;
                    PublishTapRoute();
                }

                if (e.IsDown) HandleDown(e.Key); else HandleUp(e.Key);
            }
        }

        private bool IsDirection(RemoteKey k) =>
            k is RemoteKey.Up or RemoteKey.Down or RemoteKey.Left or RemoteKey.Right;

        private void HandleDown(RemoteKey key)
        {
            var st = _states.TryGetValue(key, out var s) ? s : new KeyState();

            // 1) 手势：OK 整个物理按住期间都有效（不能卡在 holdMs 内，否则「按稳再选方向」会被长按抢走）。
            if (IsDirection(key))
            {
                var dir = GestureDirection(key);
                if (dir is not null && _states.TryGetValue(RemoteKey.Ok, out var okState) && okState.Phase == Phase.Down)
                {
                    var gesture = BindingFor(RemoteKey.Ok)?.Gesture;
                    if (gesture is not null && gesture.TryGetValue(dir, out var gestureAction))
                    {
                        var ok = okState;
                        ok.Seq++;
                        ok.SuppressTap = true;
                        _states[RemoteKey.Ok] = ok;

                        st.Phase = Phase.Consumed;
                        st.Seq++;
                        _states[key] = st;

                        Perform(gestureAction, key: RemoteKey.Ok, isHold: false);
                        Log($"GESTURE ok+{dir}");
                        return;
                    }
                }
            }

            // 2) 双击窗口内的第二次按下 → 判定双击。
            if (st.Phase == Phase.WaitingDouble)
            {
                st.Seq++;
                st.Phase = Phase.Consumed;
                _states[key] = st;
                Perform(DoubleActionFor(key), key: key, isHold: false);
                Log($"DOUBLE {key.RawName()}");
                return;
            }

            // 3) 常规按下。
            st.Phase = Phase.Down;
            st.HoldFired = false;
            st.SuppressTap = false;
            st.Seq++;
            int token = st.Seq;
            _states[key] = st;
            ScheduleAfter(_config.Settings.HoldMs, () => OnHoldTimer(key, token));
        }

        private void ScheduleAfter(int ms, Action work) => _scheduler.Schedule(ms, () => Run(work));

        private void OnEscapeTimer(int token)
        {
            if (_escapeSeq != token) return;
            FireEscapeHatch();
        }

        private void FireEscapeHatch()
        {
            _overlayHandler = null;
            MouseMode.Instance.Deactivate();
            _lockedLayer = 0;
            _lockedLayerIdleSeq++;
            _momentaryLayer = null;
            _momentaryOwner = null;
            if (_states.TryGetValue(RemoteKey.Menu, out var menu))
            {
                menu.Seq++;
                menu.Phase = Phase.Consumed;
                menu.HoldFired = false;
                menu.SuppressTap = false;
                _states[RemoteKey.Menu] = menu;
            }
            int notified = _lastNotifiedLayer;
            RecomputeLayer();
            if (notified == 0) _delegate?.LayerChanged(0);
            PublishTapRoute();
            Log($"ESCAPE 长按菜单 {EscapeHoldMs}ms → 回全局层/关浮层");
            var hook = OnEscapeHatch;
            if (hook is not null) _scheduler.PostUi(hook);
        }

        private void OnHoldTimer(RemoteKey key, int token)
        {
            if (!_states.TryGetValue(key, out var st)) return;
            if (st.Seq != token || st.Phase != Phase.Down || st.HoldFired) return;
            st.HoldFired = true;
            _states[key] = st;

            // 基础文字输入态保护删除键：Profile 的 hold 不能篡改它。
            if (key == RemoteKey.Back && EffectiveLayer == 0)
            {
                if (_config.Settings.DeleteAllOnHold == true)
                {
                    _runner.Run(MAction.Macro(
                        MacroStep.Of(MAction.KeyStroke("a", "ctrl")),
                        MacroStep.Of(MAction.KeyStroke("delete"))));
                }
                else
                {
                    _runner.Run(MAction.KeyStroke("delete"));
                }
            }
            else
            {
                Perform(BindingFor(key)?.Hold, key: key, isHold: true);
            }
            Log($"HOLD {key.RawName()}");
        }

        private void HandleUp(RemoteKey key)
        {
            if (!_states.TryGetValue(key, out var st)) return;
            switch (st.Phase)
            {
                case Phase.Idle:
                    return;
                case Phase.WaitingDouble:
                    return;
                case Phase.Consumed:
                    st.Seq++;
                    st.Phase = Phase.Idle;
                    _states[key] = st;
                    break;
                case Phase.Down:
                    st.Seq++;
                    if (st.HoldFired || st.SuppressTap)
                    {
                        if (_momentaryOwner == key) ClearMomentary();
                        st.Phase = Phase.Idle;
                        _states[key] = st;
                    }
                    else
                    {
                        bool hasDouble = DoubleActionFor(key) is not null;
                        if (hasDouble && _config.Settings.DoubleMs > 0)
                        {
                            st.Phase = Phase.WaitingDouble;
                            int token = st.Seq;
                            _states[key] = st;
                            ScheduleAfter(_config.Settings.DoubleMs, () => OnDoubleTimer(key, token));
                        }
                        else
                        {
                            st.Phase = Phase.Idle;
                            _states[key] = st;
                            FireTap(key);
                            Log($"TAP {key.RawName()}");
                        }
                    }
                    break;
            }
        }

        private void OnDoubleTimer(RemoteKey key, int token)
        {
            if (!_states.TryGetValue(key, out var st)) return;
            if (st.Seq != token || st.Phase != Phase.WaitingDouble) return;
            st.Phase = Phase.Idle;
            _states[key] = st;
            FireTap(key);
            Log($"TAP(after double window) {key.RawName()}");
        }

        private void FireTap(RemoteKey key)
        {
            // 任务视图里 Home/菜单的首要语义是退出该系统态。
            if (EffectiveLayer == 0 && (key == RemoteKey.Menu || key == RemoteKey.Home) && TransientSystemUI.ConsumeExit())
            {
                _runner.Run(MAction.KeyStroke("escape"));
                Log($"{key.RawName()} 退出 Task View");
                return;
            }
            var b = BindingFor(key);
            int layer = EffectiveLayer;
            MAction? action;
            if (layer != 0 && b?.Layers is not null && b.Layers.TryGetValue(layer.ToString(), out var layered))
            {
                action = layered;
            }
            else if (layer == 0 && key == RemoteKey.Ok)
            {
                // 基础态 OK 恒为「确认/发送」，仅 per-app 显式声明可换键（如飞书 Ctrl+Enter 发送）。
                action = OverlayDeclared(RemoteKey.Ok)?.Tap ?? MAction.KeyStroke("return");
            }
            else if (layer == 0 && key == RemoteKey.Back)
            {
                action = MAction.KeyStroke("delete");
            }
            else if (layer == 0 && NativeArrowName.TryGetValue(key, out var arrow))
            {
                action = MAction.KeyStroke(arrow);
            }
            else
            {
                action = b?.Tap;
            }
            Perform(action, key: key, isHold: false);
        }

        private static readonly Dictionary<RemoteKey, string> NativeArrowName = new()
        {
            [RemoteKey.Up] = "up_arrow",
            [RemoteKey.Down] = "down_arrow",
            [RemoteKey.Left] = "left_arrow",
            [RemoteKey.Right] = "right_arrow",
        };

        private void Perform(MAction? action, RemoteKey key, bool isHold)
        {
            if (action is null) return;
            switch (action)
            {
                case LayerMomentaryAction mom:
                    if (isHold) ActivateMomentary(mom.Layer, key);
                    else Log($"layer_momentary on tap ignored for {key.RawName()}");
                    break;
                case LayerToggleAction tog:
                    ToggleLayer(tog.Layer);
                    break;
                default:
                    _runner.Run(action);
                    break;
            }
        }

        private void ToggleLayer(int n)
        {
            _lockedLayer = _lockedLayer == n ? 0 : n;
            if (_lockedLayer == 0) _lockedLayerIdleSeq++; else RestartLockedLayerIdleTimer();
            RecomputeLayer();
        }

        private void RestartLockedLayerIdleTimer()
        {
            _lockedLayerIdleSeq++;
            int token = _lockedLayerIdleSeq;
            ScheduleAfter(LockedLayerIdleMs, () =>
            {
                lock (_gate)
                {
                    if (_lockedLayerIdleSeq != token || _lockedLayer == 0) return;
                    _lockedLayer = 0;
                    _lockedLayerIdleSeq++;
                    RecomputeLayer();
                    Log($"锁定控制模式空闲 {LockedLayerIdleMs / 1000}s → 自动回到基础层");
                }
            });
        }

        private void ActivateMomentary(int n, RemoteKey owner)
        {
            _momentaryLayer = n;
            _momentaryOwner = owner;
            RecomputeLayer();
        }

        private void ClearMomentary()
        {
            _momentaryLayer = null;
            _momentaryOwner = null;
            RecomputeLayer();
        }

        private void RecomputeLayer()
        {
            int eff = EffectiveLayer;
            if (eff == _lastNotifiedLayer) return;
            _lastNotifiedLayer = eff;
            PublishTapRoute();
            _delegate?.LayerChanged(eff);
            Log($"LAYER {eff}");
        }

        /// <summary>per-app overlay 对某键的【显式】声明（继承自 global 的值不算）。</summary>
        private KeyBinding? OverlayDeclared(RemoteKey key) =>
            _activeOverlay is not null && _activeOverlay.TryGetValue(key.RawName(), out var kb) ? kb : null;

        /// <summary>per-app overlay 只覆盖声明的槽，其余继承 global。</summary>
        private KeyBinding? BindingFor(RemoteKey key)
        {
            string name = key.RawName();
            if (_activeOverlay is null || !_activeOverlay.TryGetValue(name, out var overlay))
                return _globalProfile.TryGetValue(name, out var g) ? g : null;
            if (!_globalProfile.TryGetValue(name, out var globalKb)) return overlay;

            var merged = globalKb.Clone();
            if (overlay.Tap is not null) merged.Tap = overlay.Tap;
            if (overlay.Hold is not null) merged.Hold = overlay.Hold;
            if (overlay.Double is not null) merged.Double = overlay.Double;
            if (overlay.Gesture is not null)
            {
                merged.Gesture ??= new Dictionary<string, MAction>();
                foreach (var kv in overlay.Gesture) merged.Gesture[kv.Key] = kv.Value;
            }
            if (overlay.Layers is not null)
            {
                merged.Layers ??= new Dictionary<string, MAction>();
                foreach (var kv in overlay.Layers) merged.Layers[kv.Key] = kv.Value;
            }
            return merged;
        }

        /// <summary>基础文字输入态不允许 Profile 通过 double 绕过方向/确认/删除保护。</summary>
        private MAction? DoubleActionFor(RemoteKey key)
        {
            var action = BindingFor(key)?.Double;
            if (EffectiveLayer != 0) return action;
            return key switch
            {
                RemoteKey.Up or RemoteKey.Down or RemoteKey.Left or RemoteKey.Right or RemoteKey.Back => null,
                RemoteKey.Ok => action is LayerToggleAction ? action : null,
                _ => action,
            };
        }

        private static string? GestureDirection(RemoteKey k) => k switch
        {
            RemoteKey.Up => "up",
            RemoteKey.Down => "down",
            RemoteKey.Left => "left",
            RemoteKey.Right => "right",
            _ => null,
        };

        private void Log(string msg) => _delegate?.MappingLog("MAP " + msg);
    }
}
