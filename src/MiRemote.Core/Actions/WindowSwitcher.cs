using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MiRemote.Core
{
    public struct WindowInfo
    {
        public IntPtr Handle;
        public uint Pid;
        public string Title;
        public string ProcessName;
    }

    /// <summary>
    /// 窗口切换（移植 macOS 版 WindowSwitcher：同 app 窗口循环 + 全局 MRU 循环）。
    /// Windows 上没有 AXRaise，统一用 ShowWindow(SW_RESTORE) + SetForegroundWindow。
    /// </summary>
    public static class WindowSwitcher
    {
        public static List<WindowInfo> AllWindows(bool currentAppOnly = false, uint? pid = null)
        {
            var result = new List<WindowInfo>();
            Win32.EnumWindows((hWnd, _) =>
            {
                if (!Win32.IsWindowVisible(hWnd)) return true;
                int len = Win32.GetWindowTextLength(hWnd);
                if (len <= 0) return true;

                Win32.GetWindowThreadProcessId(hWnd, out var wpid);
                if (wpid == 0) return true;
                uint current = (uint)Process.GetCurrentProcess().Id;
                if (wpid == current) return true;                     // 跳过自己
                if (currentAppOnly && pid.HasValue && wpid != pid.Value) return true;

                Win32.GetWindowRect(hWnd, out var r);
                if (r.Width < 120 || r.Height < 80) return true;      // 过滤 helper / 小浮窗

                var sb = new StringBuilder(len + 1);
                Win32.GetWindowText(hWnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                string procName;
                try { procName = Process.GetProcessById((int)wpid).ProcessName.ToLowerInvariant(); }
                catch { procName = ""; }

                // Windows 上 UWP/系统外壳巨窗（ShellWindow）不算普通应用窗口
                if (hWnd == Win32.GetShellWindow()) return true;

                result.Add(new WindowInfo { Handle = hWnd, Pid = wpid, Title = title, ProcessName = procName });
                return true;
            }, IntPtr.Zero);
            return result;
        }

        /// <summary>循环取下一个下标。</summary>
        public static int NextIndex(int i, int count) => count <= 0 ? 0 : ((i + 1) % count + count) % count;

        public static WindowInfo? PickGlobalTarget(List<WindowInfo> wins)
        {
            if (wins.Count <= 1) return null;
            var fg = Win32.GetForegroundWindow();
            int idx = wins.FindIndex(w => w.Handle == fg);
            int next = NextIndex(idx < 0 ? 0 : idx, wins.Count);
            return wins[next];
        }

        public static void Cycle(string scope)
        {
            var fg = Win32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            Win32.GetWindowThreadProcessId(fg, out var pid);

            if (scope == "global")
            {
                var target = PickGlobalTarget(AllWindows());
                if (target is null) return;
                Activate(target.Value);
                return;
            }

            // 同应用窗口循环
            var wins = AllWindows(currentAppOnly: true, pid: pid);
            if (wins.Count <= 1) return;
            int current = wins.FindIndex(w => w.Handle == fg);
            Activate(wins[NextIndex(current < 0 ? 0 : current, wins.Count)]);
        }

        /// <summary>激活窗口：还原最小化 → 置前。</summary>
        public static void Activate(WindowInfo target)
        {
            ActivateHandle(target.Handle);
        }

        public static void ActivateHandle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            if (Win32.IsIconic(hWnd)) Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
            else Win32.ShowWindow(hWnd, Win32.SW_SHOW);

            // 后台进程直接 SetForegroundWindow 常被忽略：先把输入焦点线程挂上去再置前。
            var fg = Win32.GetForegroundWindow();
            if (fg != hWnd)
            {
                uint fgThread = Win32.GetWindowThreadProcessId(fg, out _);
                uint appThread = Win32.GetWindowThreadProcessId(hWnd, out _);
                _ = fgThread; _ = appThread;
                Win32.SetForegroundWindow(hWnd);
                if (Win32.GetForegroundWindow() != hWnd) Win32.ShowWindow(hWnd, Win32.SW_RESTORE);
            }
        }

        /// <summary>当前前台进程名（小写，无扩展名）——per-app profile 的匹配键。</summary>
        public static string ForegroundProcessKey()
        {
            var fg = Win32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return "";
            Win32.GetWindowThreadProcessId(fg, out var pid);
            if (pid == 0) return "";
            try
            {
                var p = Process.GetProcessById((int)pid);
                return p.ProcessName.ToLowerInvariant();
            }
            catch { return ""; }
        }

        public static WindowInfo ForegroundWindow()
        {
            var fg = Win32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return default;
            Win32.GetWindowThreadProcessId(fg, out var pid);
            var sb = new StringBuilder(512);
            Win32.GetWindowText(fg, sb, sb.Capacity);
            string proc = "";
            try { proc = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { }
            return new WindowInfo { Handle = fg, Pid = pid, Title = sb.ToString(), ProcessName = proc };
        }
    }

    /// <summary>前台 App MRU 栈（对应 macOS 版 KeyMapperApp 的 MRU，供 next_app/previous_app 使用）。</summary>
    public sealed class AppMru
    {
        private readonly object _gate = new();
        private readonly LinkedList<IntPtr> _mru = new();
        private const int Max = 16;

        public void Push(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            lock (_gate)
            {
                _mru.Remove(hWnd);
                _mru.AddFirst(hWnd);
                while (_mru.Count > Max) _mru.RemoveLast();
            }
        }

        /// <summary>切到上一个外部窗口。</summary>
        public bool SwitchBack()
        {
            lock (_gate)
            {
                var fg = Win32.GetForegroundWindow();
                var node = _mru.First;
                while (node is not null)
                {
                    var h = node.Value;
                    node = node.Next;
                    if (h == fg) continue;
                    if (!Win32.IsWindowVisible(h))
                    {
                        _mru.Remove(h);
                        continue;
                    }
                    WindowSwitcher.ActivateHandle(h);
                    return true;
                }
            }
            return false;
        }

        public void Remove(IntPtr hWnd)
        {
            lock (_gate) _mru.Remove(hWnd);
        }
    }
}
