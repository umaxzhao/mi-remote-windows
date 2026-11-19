using System;
using System.Runtime.InteropServices;

namespace MiRemote.Core
{
    /// <summary>
    /// 短暂屏蔽某个虚拟键。用于遥控器语音键：它会同时发送 F5，
    /// 而豆包的右 Alt 快捷键会被同时按住的 F5 干扰。
    /// </summary>
    public sealed class LowLevelKeyBlocker : IDisposable
    {
        private IntPtr _hook;
        private Win32.HookProc? _proc;
        private System.Threading.Timer? _releaseTimer;

        /// <param name="downOnly">true 只拦按下，放行抬起；避免输入法一直认为按键未释放。</param>
        public void Block(ushort virtualKey, bool downOnly = false)
        {
            if (_hook != IntPtr.Zero) return;
            _proc = (nCode, wParam, lParam) =>
            {
                if (nCode >= 0)
                {
                    var kb = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                    bool keyDown = wParam == (IntPtr)Win32.WM_KEYDOWN || wParam == (IntPtr)Win32.WM_SYSKEYDOWN;
                    if (kb.vkCode == virtualKey && (!downOnly || keyDown)) return (IntPtr)1;
                }
                return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
            };
            _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
        }

        public void UnblockAfter(int milliseconds)
        {
            _releaseTimer?.Dispose();
            _releaseTimer = new System.Threading.Timer(_ => Dispose(), null, Math.Max(0, milliseconds), System.Threading.Timeout.Infinite);
        }

        public void Dispose()
        {
            _releaseTimer?.Dispose();
            _releaseTimer = null;
            if (_hook != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            _proc = null;
        }
    }
}
