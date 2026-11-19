using System;
using System.Runtime.InteropServices;

namespace MiRemote.Core
{
    /// <summary>
    /// Interception 驱动注入。只用于豆包这类过滤 SendInput 的语音快捷键。
    /// 驱动未安装时 IsAvailable=false，调用方保留 SendInput 回退。
    /// </summary>
    public sealed class InterceptionKeyboard : IDisposable
    {
        private IntPtr _context;
        public bool IsAvailable => _context != IntPtr.Zero;
        /// <summary>要注入的键盘设备号（Interception 设备编号，1 起）。</summary>
        public int DeviceId { get; set; } = 3;
        public string Status => IsAvailable ? "驱动级注入可用" : "未安装 Interception 驱动（使用普通模拟）";

        public bool TryOpen()
        {
            if (_context != IntPtr.Zero) return true;
            try
            {
                _context = interception_create_context();
                // 不要设置全局 filter：没有接收循环时会吞掉物理键盘。
                // 发送到正确的设备号即可注入，不需要过滤器。
                if (_context != IntPtr.Zero)
                {
                    interception_set_precedence(_context, DeviceId, 1);
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return IsAvailable;
        }

        private static int IsKeyboard(int device) => device >= 1 && device <= 10 ? 1 : 0;

        public void SendChord(string chord, bool down)
        {
            if (!TryOpen()) return;
            var parts = chord.Split('+');
            if (!down) Array.Reverse(parts);
            foreach (var part in parts)
            {
                if (!TryGetKey(part.Trim(), out var key)) continue;
                var stroke = new InterceptionKeyStroke
                {
                    code = key.Code,
                    state = (ushort)((down ? 0 : 1) | (key.Extended ? 2 : 0)),
                    information = 0,
                };
                int sent = interception_send(_context, DeviceId, ref stroke, 1);
                if (sent != 1) throw new InvalidOperationException($"Interception send failed: {sent}");
                System.Threading.Thread.Sleep(12);
            }
        }

        private static bool TryGetKey(string name, out (ushort Code, bool Extended) key)
        {
            key = default;
            if (!InputSimulator.TryResolveKey(name, out var vk)) return false;
            // 修饰键扫描码必须按 PS/2 规范固定，不能用 MapVirtualKey——
            // 某些键盘布局会把 VK_RMENU 映成 0x56（不是右 Alt），导致豆包收到 0xFF。
            switch (vk)
            {
                case 0x11: // 通用 Ctrl：用右 Ctrl，驱动注入状态更可靠
                    key = (0x1D, true); return true;
                case 0xA2: key = (0x1D, false); return true;  // 左 Ctrl
                case 0xA3: key = (0x1D, true); return true;   // 右 Ctrl
                case 0x12: // 通用 Alt：用右 Alt（豆包默认语音键）
                case 0xA5: key = (0x38, true); return true;   // 右 Alt = E0 38
                case 0xA4: key = (0x38, false); return true;  // 左 Alt
                case 0xA0: key = (0x2A, false); return true;  // 左 Shift
                case 0xA1: key = (0x36, false); return true;  // 右 Shift
                case 0x5B: key = (0x5B, true); return true;   // 左 Win = E0 5B
                case 0x5C: key = (0x5C, true); return true;   // 右 Win = E0 5C
            }

            bool extended = vk is 0x2E or 0x2D or 0x24 or 0x23 or 0x21 or 0x22
                or 0x26 or 0x28 or 0x25 or 0x27 or 0x2C or 0x13;
            key = ((ushort)Win32.MapVirtualKey(vk, 0), extended);
            return key.Code != 0;
        }

        public void Dispose()
        {
            if (_context == IntPtr.Zero) return;
            interception_destroy_context(_context);
            _context = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct InterceptionKeyStroke
        {
            public ushort code;
            public ushort state;
            public uint information;
        }

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr interception_create_context();
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_destroy_context(IntPtr context);
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int interception_send(IntPtr context, int device, ref InterceptionKeyStroke stroke, uint count);
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_set_precedence(IntPtr context, int device, int precedence);
        private delegate int InterceptionPredicate(int device);
        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_set_filter(IntPtr context, InterceptionPredicate predicate, ushort filter);
    }
}
