using System;
using System.Collections.Generic;
using System.Timers;

namespace MiRemote.Core
{
    /// <summary>
    /// 鼠标模式（移植 macOS 版 MouseMode）：方向=匀加速移动（多方向同按=对角线）、
    /// OK=左键、菜单=右键、返回=退出模式；空闲 20s 自动退出。
    /// </summary>
    public sealed class MouseMode
    {
        public static MouseMode Instance { get; } = new();

        public const double BaseSpeed = 4.0;
        public const double MaxSpeed = 40.0;
        public const double RampSeconds = 1.5;
        public const int IdleSeconds = 20;

        private readonly object _gate = new();
        private bool _active;
        private volatile bool _activeFlag;
        private System.Threading.Timer? _timer;
        private readonly HashSet<RemoteKey> _held = new();
        private long _holdStartMs;
        private int _idleSeq;

        public bool IsActive => _activeFlag;

        public static double SpeedAfterSeconds(double t)
        {
            if (t <= 0) return BaseSpeed;
            if (t >= RampSeconds) return MaxSpeed;
            return BaseSpeed + (MaxSpeed - BaseSpeed) * (t / RampSeconds);
        }

        public static (double dx, double dy) Delta(IEnumerable<RemoteKey> dirs, double speed)
        {
            double dx = 0, dy = 0;
            foreach (var d in dirs)
            {
                switch (d)
                {
                    case RemoteKey.Left: dx -= 1; break;
                    case RemoteKey.Right: dx += 1; break;
                    case RemoteKey.Up: dy -= 1; break;
                    case RemoteKey.Down: dy += 1; break;
                }
            }
            return (dx * speed, dy * speed);
        }

        public void Toggle()
        {
            lock (_gate)
            {
                if (_active) DeactivateLocked(); else ActivateLocked();
            }
        }

        public void Deactivate()
        {
            lock (_gate) { if (_active) DeactivateLocked(); }
        }

        /// <summary>返回 true 表示事件已被鼠标模式消费。</summary>
        public bool Handle(RemoteKey key, bool isDown)
        {
            lock (_gate)
            {
                if (!_active) return false;
                bool consumed;
                switch (key)
                {
                    case RemoteKey.Up:
                    case RemoteKey.Down:
                    case RemoteKey.Left:
                    case RemoteKey.Right:
                        if (isDown)
                        {
                            if (_held.Count == 0) _holdStartMs = Environment.TickCount64;
                            _held.Add(key);
                        }
                        else
                        {
                            _held.Remove(key);
                            if (_held.Count == 0) _holdStartMs = 0;
                        }
                        consumed = true;
                        break;
                    case RemoteKey.Ok:
                        if (isDown) InputSimulator.MouseClick(false);
                        consumed = true;
                        break;
                    case RemoteKey.Menu:
                        if (isDown) InputSimulator.MouseClick(true);
                        consumed = true;
                        break;
                    case RemoteKey.Back:
                        if (isDown) DeactivateLocked();
                        consumed = true;
                        break;
                    default:
                        consumed = false;
                        break;
                }
                if (consumed && _active) RestartIdleTimerLocked();
                return consumed;
            }
        }

        private void ActivateLocked()
        {
            _active = true;
            _activeFlag = true;
            _timer = new System.Threading.Timer(_ => Tick(), null, 16, 16);
            RestartIdleTimerLocked();
            Console.WriteLine("[MouseMode] 进入鼠标模式");
        }

        private void DeactivateLocked()
        {
            _active = false;
            _activeFlag = false;
            _idleSeq++;
            _timer?.Dispose();
            _timer = null;
            _held.Clear();
            _holdStartMs = 0;
            Console.WriteLine("[MouseMode] 退出鼠标模式");
        }

        private void RestartIdleTimerLocked()
        {
            _idleSeq++;
            int token = _idleSeq;
            new System.Threading.Timer(_ =>
            {
                lock (_gate)
                {
                    if (!_active || _idleSeq != token) return;
                    if (_held.Count > 0) { RestartIdleTimerLocked(); return; }
                    DeactivateLocked();
                    Console.WriteLine($"[MouseMode] 空闲 {IdleSeconds}s 自动退出");
                }
            }, null, IdleSeconds * 1000, System.Threading.Timeout.Infinite);
        }

        private void Tick()
        {
            if (!IsActive || _held.Count == 0 || _holdStartMs == 0) return;
            double t = (Environment.TickCount64 - _holdStartMs) / 1000.0;
            var (dx, dy) = Delta(_held, SpeedAfterSeconds(t));
            if (dx == 0 && dy == 0) return;
            InputSimulator.MouseMove((int)Math.Round(dx), (int)Math.Round(dy));
        }
    }
}
