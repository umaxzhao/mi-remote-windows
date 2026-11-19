using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MiRemote.Core
{
    /// <summary>
    /// 键鼠合成（移植 macOS 版 ActionRunner 的 CGEvent 部分）。
    /// 键名沿用 macOS 版配置（left_cmd 等）以兼容旧 config.json，同时提供 Windows 惯用名。
    /// </summary>
    public static class InputSimulator
    {
        // ===== 修饰键 =====
        // macOS 名 → Windows 语义：cmd→Ctrl（Windows 上 Ctrl 才是主修饰键），option→Alt，
        // shift/ctrl 直接对应。这样 macOS 预设里的 ⌘K（搜索）在 Windows 上落到 Ctrl+K。
        public static readonly IReadOnlyDictionary<string, ushort> Modifiers = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = 0x11, ["control"] = 0x11,
            ["left_ctrl"] = 0xA2, ["right_ctrl"] = 0xA3,
            ["alt"] = 0x12, ["menu_alt"] = 0x12,
            ["left_alt"] = 0xA4, ["right_alt"] = 0xA5,
            ["shift"] = 0x10, ["left_shift"] = 0xA0, ["right_shift"] = 0xA1,
            ["win"] = 0x5B, ["left_win"] = 0x5B, ["right_win"] = 0x5C,
            ["left_windows"] = 0x5B, ["right_windows"] = 0x5C,
            // macOS 兼容别名
            ["left_cmd"] = 0x11, ["right_cmd"] = 0x11, ["cmd"] = 0x11, ["command"] = 0x11,
            ["left_option"] = 0x12, ["right_option"] = 0x12, ["option"] = 0x12,
        };

        // ===== 主键名 → 虚拟键码 =====
        public static readonly IReadOnlyDictionary<string, ushort> KeyCodes = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["return"] = 0x0D, ["enter"] = 0x0D, ["tab"] = 0x09, ["space"] = 0x20,
            ["delete"] = 0x08, ["backspace"] = 0x08, ["forward_delete"] = 0x2E, ["del"] = 0x2E,
            ["escape"] = 0x1B, ["esc"] = 0x1B,
            ["up_arrow"] = 0x26, ["down_arrow"] = 0x28, ["left_arrow"] = 0x25, ["right_arrow"] = 0x27,
            ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
            ["home"] = 0x24, ["end"] = 0x23, ["page_up"] = 0x21, ["page_down"] = 0x22,
            ["insert"] = 0x2D, ["print_screen"] = 0x2C, ["scroll_lock"] = 0x91, ["pause"] = 0x13,
            ["caps_lock"] = 0x14, ["num_lock"] = 0x90,
            ["left_bracket"] = 0xDB, ["right_bracket"] = 0xDD,
            ["backslash"] = 0xDC, ["slash"] = 0xBF, ["comma"] = 0xBC, ["period"] = 0xBE,
            ["semicolon"] = 0xBA, ["quote"] = 0xDE, ["grave"] = 0xC0, ["backtick"] = 0xC0,
            ["minus"] = 0xBD, ["equal"] = 0xBB,
            ["volume_up"] = 0xAF, ["volume_down"] = 0xAE, ["mute"] = 0xAD,
            ["play_pause"] = 0xB3, ["media_next"] = 0xB0, ["media_prev"] = 0xB1, ["media_stop"] = 0xB2,
        };

        static InputSimulator()
        {
            for (char c = 'a'; c <= 'z'; c++) ((IDictionary<string, ushort>)KeyCodes)[c.ToString()] = (ushort)(0x41 + (c - 'a'));
            for (char c = '0'; c <= '9'; c++) ((IDictionary<string, ushort>)KeyCodes)[c.ToString()] = (ushort)(0x30 + (c - '0'));
            for (int i = 1; i <= 24; i++) ((IDictionary<string, ushort>)KeyCodes)["f" + i] = (ushort)(0x6F + i);
            // 小键盘
            for (int i = 0; i <= 9; i++) ((IDictionary<string, ushort>)KeyCodes)["numpad_" + i] = (ushort)(0x60 + i);
        }

        public static bool TryResolveKey(string name, out ushort vk)
        {
            var key = name.Trim().ToLowerInvariant();
            if (KeyCodes.TryGetValue(key, out vk)) return true;
            if (Modifiers.TryGetValue(key, out vk)) return true;
            return false;
        }

        public static bool TryResolveMod(string name, out ushort vk) => Modifiers.TryGetValue(name.Trim(), out vk);

        private static bool IsExtended(ushort vk) =>
            vk is 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C or 0x2E or 0x2D or 0x24 or 0x23 or 0x21 or 0x22 or 0x26 or 0x28 or 0x25 or 0x27 or 0x2C or 0x13;

        private static void KeyEvent(ushort vk, bool down, bool scanCode = false)
        {
            uint flags = down ? 0 : Win32.KEYEVENTF_KEYUP;
            if (IsExtended(vk)) flags |= Win32.KEYEVENTF_EXTENDEDKEY;
            ushort scan = 0;
            // 扫描码注入也必须携带扩展键标志；左 Win 与 Ctrl 的修饰状态
            // 依赖该标志。缺省时系统把它们归一成普通 Ctrl，豆包无法识别组合。
            if (scanCode)
            {
                // 必须同时带虚拟键和扫描码，某些输入法只给扫描码会识别不了修饰键。
                flags |= Win32.KEYEVENTF_SCANCODE | Win32.KEYEVENTF_EXTENDEDKEY;
                scan = vk == 0xA5 ? (ushort)0x38 : (ushort)Win32.MapVirtualKey(vk, 0);
            }
            var input = new Win32.INPUT
            {
                type = Win32.INPUT_KEYBOARD,
                u = new Win32.INPUTUNION
                {
                    ki = new Win32.KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            Win32.SendInput(1, new[] { input }, Marshal.SizeOf<Win32.INPUT>());
        }

        public static void KeyDown(ushort vk, bool scanCode = false) => KeyEvent(vk, true, scanCode);
        public static void KeyUp(ushort vk, bool scanCode = false) => KeyEvent(vk, false, scanCode);

        public static void TapKey(ushort vk)
        {
            KeyEvent(vk, true);
            Thread.Sleep(1);
            KeyEvent(vk, false);
        }

        /// <summary>合成组合键：按下修饰键 → 主键 down/up → 松开修饰键。</summary>
        public static void Combo(ushort vk, IEnumerable<string> mods, int holdMs = 0)
        {
            var resolved = new List<ushort>();
            foreach (var m in mods)
                if (TryResolveMod(m, out var mv) && !resolved.Contains(mv)) resolved.Add(mv);
            foreach (var m in resolved) KeyEvent(m, true);
            Thread.Sleep(10);
            KeyEvent(vk, true);
            if (holdMs > 0) Thread.Sleep(holdMs);
            KeyEvent(vk, false);
            Thread.Sleep(5);
            for (int i = resolved.Count - 1; i >= 0; i--) KeyEvent(resolved[i], false);
        }

        public static bool Stroke(string keyName, IEnumerable<string>? mods = null)
        {
            if (!TryResolveKey(keyName, out var vk)) return false;
            if (mods is null) { TapKey(vk); return true; }
            Combo(vk, mods);
            return true;
        }

        /// <summary>Win+H 之类：按下 Win 键后点一下字母。</summary>
        public static void WindowsShortcut(string letter) => Combo((ushort)(0x41 + (char.ToLowerInvariant(letter[0]) - 'a')), new[] { "win" });

        public static void Text(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var ch in text)
            {
                var inputs = new[]
                {
                    MakeUnicode(ch, true),
                    MakeUnicode(ch, false),
                };
                Win32.SendInput(2, inputs, Marshal.SizeOf<Win32.INPUT>());
                Thread.Sleep(1);
            }
        }

        private static Win32.INPUT MakeUnicode(char ch, bool down) => new()
        {
            type = Win32.INPUT_KEYBOARD,
            u = new Win32.INPUTUNION
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = Win32.KEYEVENTF_UNICODE | (down ? 0 : Win32.KEYEVENTF_KEYUP),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                }
            }
        };

        // ===== 鼠标 =====
        public static void MouseMove(int dx, int dy)
        {
            Win32.GetCursorPos(out var p);
            Win32.SetCursorPos(Math.Max(0, p.X + dx), Math.Max(0, p.Y + dy));
        }

        public static void MouseClick(bool right = false)
        {
            uint down = right ? Win32.MOUSEEVENTF_RIGHTDOWN : Win32.MOUSEEVENTF_LEFTDOWN;
            uint up = right ? Win32.MOUSEEVENTF_RIGHTUP : Win32.MOUSEEVENTF_LEFTUP;
            SendMouse(down);
            Thread.Sleep(10);
            SendMouse(up);
        }

        private static void SendMouse(uint flags)
        {
            var input = new Win32.INPUT
            {
                type = Win32.INPUT_MOUSE,
                u = new Win32.INPUTUNION { mi = new Win32.MOUSEINPUT { dx = 0, dy = 0, mouseData = 0, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero } }
            };
            Win32.SendInput(1, new[] { input }, Marshal.SizeOf<Win32.INPUT>());
        }
    }
}
