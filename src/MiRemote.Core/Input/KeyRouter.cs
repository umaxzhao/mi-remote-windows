using System;
using System.Threading;

namespace MiRemote.Core
{
    /// <summary>
    /// 按键路由：Raw Input 与 MappingEngine 之间的胶水。
    /// - 基础文字输入态下，方向键走「原生直通」（自己合成 keydown/keyup + 自动重复），
    ///   保留系统 caret/IME 语义；
    /// - 其余键与层/手势场景喂给 MappingEngine。
    /// </summary>
    public sealed class KeyRouter
    {
        private readonly MappingEngine _engine;
        private readonly Logger _logger;
        private readonly object _gate = new();
        private readonly Dictionary<RemoteKey, Timer> _repeatTimers = new();
        private readonly HashSet<RemoteKey> _heldNative = new();

        private static readonly Dictionary<RemoteKey, string> ArrowName = new()
        {
            [RemoteKey.Up] = "up_arrow",
            [RemoteKey.Down] = "down_arrow",
            [RemoteKey.Left] = "left_arrow",
            [RemoteKey.Right] = "right_arrow",
        };

        public KeyRouter(MappingEngine engine, Logger logger)
        {
            _engine = engine;
            _logger = logger;
        }

        public void Handle(ButtonEvent e)
        {
            if (_engine.IsSuspended) return;

            // 原生直通判定（与 macOS 版 directionVerdict 对齐）。
            var route = _engine.Route;
            if (ArrowName.ContainsKey(e.Key) && e.Key is RemoteKey.Up or RemoteKey.Down or RemoteKey.Left or RemoteKey.Right)
            {
                bool native = route.EffectiveLayer == 0 && !route.OkDown && !route.UiCapture
                             && !route.MouseCapture && !route.SystemUiCapture
                             && (route.NativeDirections?.Contains(e.Key) ?? false);
                if (native)
                {
                    HandleNativeArrow(e);
                    return;
                }
            }

            _engine.Handle(e);
        }

        private void HandleNativeArrow(ButtonEvent e)
        {
            if (!ArrowName.TryGetValue(e.Key, out var name)) return;
            if (!InputSimulator.TryResolveKey(name, out var vk)) return;

            if (e.IsDown)
            {
                if (!_heldNative.Contains(e.Key))
                {
                    _heldNative.Add(e.Key);
                    InputSimulator.KeyDown(vk);
                    StartRepeat(e.Key, vk);
                }
            }
            else
            {
                if (_heldNative.Contains(e.Key))
                {
                    _heldNative.Remove(e.Key);
                    StopRepeat(e.Key);
                    InputSimulator.KeyUp(vk);
                }
            }
        }

        private void StartRepeat(RemoteKey key, ushort vk)
        {
            StopRepeat(key);
            int delayMs = _engine.Config.Settings.RepeatDelayMs;
            int intervalMs = _engine.Config.Settings.RepeatIntervalMs;
            if (delayMs < 0) return;   // 关闭自动重复
            if (delayMs < 0) { lock (_gate) { /* no repeat */ } return; }
            var t = new System.Threading.Timer(_ =>
            {
                lock (_gate)
                {
                    if (!_heldNative.Contains(key)) return;
                    InputSimulator.KeyDown(vk);
                }
            }, null, delayMs, intervalMs);
            lock (_gate) _repeatTimers[key] = t;
        }

        private void StopRepeat(RemoteKey key)
        {
            lock (_gate)
            {
                if (_repeatTimers.TryGetValue(key, out var t))
                {
                    t.Dispose();
                    _repeatTimers.Remove(key);
                }
            }
        }

        public void StopAll()
        {
            lock (_gate)
            {
                foreach (var t in _repeatTimers.Values) t.Dispose();
                _repeatTimers.Clear();
                foreach (var key in _heldNative)
                    if (ArrowName.TryGetValue(key, out var name) && InputSimulator.TryResolveKey(name, out var vk))
                        InputSimulator.KeyUp(vk);
                _heldNative.Clear();
            }
        }
    }
}
