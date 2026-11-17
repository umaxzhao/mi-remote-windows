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
        public string Status => IsAvailable ? "驱动级注入可用" : "未安装 Interception 驱动（使用普通模拟）";

        public bool TryOpen()
        {
            if (_context != IntPtr.Zero) return true;
            try
            {
                _context = interception_create_context();
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return IsAvailable;
        }

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
                interception_send(_context, 1, ref stroke, 1);
                System.Threading.Thread.Sleep(12);
            }
        }

        private static bool TryGetKey(string name, out (ushort Code, bool Extended) key)
        {
            key = default;
            if (!InputSimulator.TryResolveKey(name, out var vk)) return false;
            // Interception 需要 PS/2 扫描码与 E0 标志，而非 Win32 扩展键标志。
            bool extended = vk is 0xA3 or 0xA5 or 0x5B or 0x5C
                or 0x2E or 0x2D or 0x24 or 0x23 or 0x21 or 0x22
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
        private static extern void interception_send(IntPtr context, int device, ref InterceptionKeyStroke stroke, uint count);
    }
}
