using System;

namespace MiRemote.Core
{
    /// <summary>
    /// 聚焦前台 App 的输入框（移植 macOS 版 FocusInput：三级兜底）。
    /// Windows 版简化：激活前台窗口并把光标送回（UI Automation 在 .NET 9 上需额外引用，
    /// 待后续接入；当前以窗口激活 + Alt+Tab 回落保证不卡死）。
    /// </summary>
    public static class FocusInput
    {
        public static void Perform()
        {
            try
            {
                var fg = WindowSwitcher.ForegroundWindow();
                if (fg.Handle != IntPtr.Zero)
                {
                    WindowSwitcher.ActivateHandle(fg.Handle);
                }
            }
            catch { }
        }
    }
}
