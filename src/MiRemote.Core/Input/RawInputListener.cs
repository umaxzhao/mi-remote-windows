using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MiRemote.Core
{
    public sealed class RawInputListener : IDisposable
    {
        private readonly Logger _logger;
        private IntPtr _hwnd;
        private Win32.WndProcDelegate? _wndProc;
        private Thread? _thread;
        private uint _threadId;
        private bool _running;
        private bool _seenRemote;

        private readonly Dictionary<IntPtr, string> _deviceNames = new();
        private readonly Dictionary<string, byte[]> _prevReport = new();

        public event Action<ButtonEvent>? ButtonPressed;
        public event Action<string>? Log;

        /// <summary>遥控器被 Windows 识别为标准键盘时，虚拟键码 → 遥控键。语音键 = F5。</summary>
        private static readonly Dictionary<ushort, RemoteKey> VkMap = new()
        {
            [0x74] = RemoteKey.Voice,    // F5（遥控器语音键 usage 0x3E）
            [0x0D] = RemoteKey.Ok,
            [0x26] = RemoteKey.Up,
            [0x28] = RemoteKey.Down,
            [0x25] = RemoteKey.Left,
            [0x27] = RemoteKey.Right,
            [0x24] = RemoteKey.Home,
            [0x5D] = RemoteKey.Menu,
            [0xAF] = RemoteKey.VolUp,
            [0xAE] = RemoteKey.VolDown,
            [0xC0] = RemoteKey.Tv,
        };

        public RawInputListener(Logger logger) => _logger = logger;

        public void Start()
        {
            if (_running) return;
            _wndProc = WndProc;
            var wc = new Win32.WNDCLASS
            {
                lpfnWndProc = _wndProc,
                hInstance = Win32.GetModuleHandle(null),
                lpszClassName = "MiRemoteRawInput",
            };
            if (Win32.RegisterClass(ref wc) == 0)
            {
                LogMsg($"RegisterClass 失败: {Marshal.GetLastWin32Error()}");
                return;
            }
            _hwnd = Win32.CreateWindowEx(0, wc.lpszClassName, "", 0, 0, 0, 0, 0,
                                         Win32.HWND_MESSAGE, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                LogMsg($"CreateWindowEx 失败: {Marshal.GetLastWin32Error()}");
                return;
            }

            var devices = new Win32.RAWINPUTDEVICE[]
            {
                new() { usUsagePage = 0x01, usUsage = 0x06, dwFlags = (uint)(Win32.RIDEV_INPUTSINK | Win32.RIDEV_DEVNOTIFY), hwndTarget = _hwnd },
                new() { usUsagePage = 0x0C, usUsage = 0x01, dwFlags = (uint)(Win32.RIDEV_INPUTSINK | Win32.RIDEV_DEVNOTIFY), hwndTarget = _hwnd },
            };
            if (!Win32.RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<Win32.RAWINPUTDEVICE>()))
            {
                LogMsg($"RegisterRawInputDevices 失败: {Marshal.GetLastWin32Error()}");
                return;
            }

            _running = true;
            _thread = new Thread(MessageLoop) { IsBackground = true, Name = "RawInput" };
            _thread.Start();
            LogMsg("Raw Input 监听已启动");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            if (_threadId != 0) Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread?.Join(2000);
            if (_hwnd != IntPtr.Zero)
            {
                var devices = new Win32.RAWINPUTDEVICE[]
                {
                    new() { usUsagePage = 0x01, usUsage = 0x06, dwFlags = Win32.RIDEV_REMOVE, hwndTarget = IntPtr.Zero },
                    new() { usUsagePage = 0x0C, usUsage = 0x01, dwFlags = Win32.RIDEV_REMOVE, hwndTarget = IntPtr.Zero },
                };
                Win32.RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<Win32.RAWINPUTDEVICE>());
                Win32.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            LogMsg("Raw Input 监听已停止");
        }

        private void MessageLoop()
        {
            _threadId = Win32.GetCurrentThreadId();
            Win32.MSG msg;
            while (Win32.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessage(ref msg);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == Win32.WM_INPUT)
            {
                HandleInput(lParam);
                return IntPtr.Zero;
            }
            if (msg == Win32.WM_INPUT_DEVICE_CHANGE)
            {
                // 设备插拔：清缓存，下次重新读设备名
                _deviceNames.Clear();
                _prevReport.Clear();
                return IntPtr.Zero;
            }
            return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void HandleInput(IntPtr hRawInput)
        {
            uint size = 0;
            Win32.GetRawInputData(hRawInput, Win32.RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<Win32.RAWINPUTHEADER>());
            if (size == 0) return;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Win32.GetRawInputData(hRawInput, Win32.RID_INPUT, buf, ref size, (uint)Marshal.SizeOf<Win32.RAWINPUTHEADER>()) != size) return;

                uint dwType = (uint)Marshal.ReadInt32(buf, 0);
                if (dwType != Win32.RIM_TYPEHID && dwType != Win32.RIM_TYPEKEYBOARD) return;

                IntPtr hDevice = Marshal.ReadIntPtr(buf, 8);
                if (!_deviceNames.TryGetValue(hDevice, out var name))
                {
                    uint sz = 0;
                    Win32.GetRawInputDeviceInfo(hDevice, Win32.RID_DEVICENAME, IntPtr.Zero, ref sz);
                    if (sz > 0)
                    {
                        IntPtr nb = Marshal.AllocHGlobal((int)sz * 2);
                        try
                        {
                            if (Win32.GetRawInputDeviceInfo(hDevice, Win32.RID_DEVICENAME, nb, ref sz) > 0)
                                name = Marshal.PtrToStringUni(nb) ?? "";
                        }
                        finally { Marshal.FreeHGlobal(nb); }
                    }
                    else name = "";
                    _deviceNames[hDevice] = name ?? "";
                }

                if (!RemoteIdentity.Matches(name ?? "")) return;

                if (!_seenRemote)
                {
                    _seenRemote = true;
                    LogMsg("已检测到遥控器设备");
                }

                int off = Marshal.SizeOf<Win32.RAWINPUTHEADER>();
                if (dwType == Win32.RIM_TYPEKEYBOARD)
                {
                    // RAWKEYBOARD：MakeCode(0) Flags(2) Reserved(4) VKey(6) Message(8) Extra(12)
                    ushort vkey = (ushort)Marshal.ReadInt16(buf, off + 6);
                    ushort flags = (ushort)Marshal.ReadInt16(buf, off + 2);
                    bool down = (flags & 0x0001) == 0;   // RI_KEY_BREAK=1
                    if (VkMap.TryGetValue(vkey, out var rk))
                        Emit(rk, down);
                    else
                        LogMsg($"遥控器未识别的虚拟键 vk=0x{vkey:X2} down={down}");
                    return;
                }

                uint sizeHid = (uint)Marshal.ReadInt32(buf, off);
                uint count = (uint)Marshal.ReadInt32(buf, off + 4);
                if (sizeHid == 0 || count == 0) return;
                int dataOff = off + 8;
                int total = (int)(sizeHid * count);
                var data = new byte[total];
                Marshal.Copy(buf + dataOff, data, 0, total);

                // 取第一个 report（count 通常 1）
                var report = new byte[sizeHid];
                Buffer.BlockCopy(data, 0, report, 0, (int)sizeHid);

                DecodeReport(hDevice, report);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private void DecodeReport(IntPtr hDevice, byte[] report)
        {
            // 键盘 boot report：8 字节（无 report id）或 9+ 字节（首字节 report id）。
            // 消费者 report：通常 2-3 字节。
            // 我们用 sizeHid 长度区分。
            if (report.Length >= 8)
            {
                // 判断是否 boot keyboard report：第 2 字节为 reserved(0)，且 report 长度 8 或 9
                bool hasReportId = report.Length >= 9 && report[1] == 0x00 && report.Length <= 9;
                int start = hasReportId ? 1 : 0;
                if (report.Length - start >= 8 && report[start + 1] == 0)
                {
                    DecodeKeyboard(hDevice, report, start);
                    return;
                }
            }
            // 消费者控制 report
            DecodeConsumer(hDevice, report);
        }

        private void DecodeKeyboard(IntPtr hDevice, byte[] report, int start)
        {
            // payload: mods(1) reserved(1) key[0..5]
            byte mods = report[start];
            var keys = new List<byte>();
            for (int i = 2; i < 8; i++) if (report[start + i] != 0) keys.Add(report[start + i]);

            string key = hDevice.ToString() + ":kb";
            _prevReport.TryGetValue(key, out var prev);
            var prevKeys = prev is not null && prev.Length >= 8 ? new List<byte>(prev.AsSpan(start + 2, 6).ToArray()) : new List<byte>();
            // mods diff is ignored (remote doesn't use modifier keys as RemoteKey)

            // newly pressed
            foreach (var k in keys)
            {
                if (!prevKeys.Contains(k))
                {
                    if (RemoteKeys.UsageMap.TryGetValue(k, out var rk))
                        Emit(rk, true);
                }
            }
            // released
            foreach (var k in prevKeys)
            {
                if (!keys.Contains(k))
                {
                    if (RemoteKeys.UsageMap.TryGetValue(k, out var rk))
                        Emit(rk, false);
                }
            }

            // 保存
            var snapshot = new byte[report.Length];
            Buffer.BlockCopy(report, 0, snapshot, 0, report.Length);
            _prevReport[key] = snapshot;
        }

        private void DecodeConsumer(IntPtr hDevice, byte[] report)
        {
            // 把 report 解析成 16-bit usage 列表（跳过可能的 report id）
            var usages = new List<ushort>();
            int start = (report.Length > 2 && report[0] == 0x01 && report.Length % 2 != 0) ? 1 : 0;
            // 尝试 16-bit 小端
            for (int i = start; i + 1 < report.Length; i += 2)
            {
                ushort u = (ushort)(report[i] | (report[i + 1] << 8));
                if (u != 0) usages.Add(u);
            }
            // 若 16-bit 全零但 report 有非零字节，退化为 8-bit usage 列表
            if (usages.Count == 0)
            {
                for (int i = start; i < report.Length; i++)
                    if (report[i] != 0) usages.Add(report[i]);
            }

            string key = hDevice.ToString() + ":consumer";
            _prevReport.TryGetValue(key, out var prev);
            var prevUsages = new List<ushort>();
            if (prev is not null)
            {
                int ps = (prev.Length > 2 && prev[0] == 0x01 && prev.Length % 2 != 0) ? 1 : 0;
                for (int i = ps; i + 1 < prev.Length; i += 2)
                {
                    ushort u = (ushort)(prev[i] | (prev[i + 1] << 8));
                    if (u != 0) prevUsages.Add(u);
                }
                if (prevUsages.Count == 0)
                    for (int i = ps; i < prev.Length; i++) if (prev[i] != 0) prevUsages.Add(prev[i]);
            }

            foreach (var u in usages)
            {
                if (!prevUsages.Contains(u) && RemoteKeys.ConsumerUsageMap.TryGetValue(u, out var rk))
                    Emit(rk, true);
            }
            foreach (var u in prevUsages)
            {
                if (!usages.Contains(u) && RemoteKeys.ConsumerUsageMap.TryGetValue(u, out var rk))
                    Emit(rk, false);
            }

            var snapshot = new byte[report.Length];
            Buffer.BlockCopy(report, 0, snapshot, 0, report.Length);
            _prevReport[key] = snapshot;
        }

        private readonly Dictionary<RemoteKey, (bool Down, long Ms)> _lastEvents = new();

        private void Emit(RemoteKey rk, bool down)
        {
            long now = Environment.TickCount64;
            if (_lastEvents.TryGetValue(rk, out var last) && last.Down == down && now - last.Ms < 15)
                return;   // 同一事件经键盘/HID 双通道重复上报
            _lastEvents[rk] = (down, now);
            LogMsg($"HID {rk.RawName()} {(down ? "down" : "up")}");
            var ev = new ButtonEvent(rk, down, now);
            try { ButtonPressed?.Invoke(ev); }
            catch (Exception ex) { _logger.Warn($"ButtonPressed 回调异常: {ex.Message}"); }
        }

        private void LogMsg(string msg)
        {
            if (Log is not null)
            {
                try { Log.Invoke(msg); } catch { }
            }
            else _logger.Info(msg);
        }

        public void Dispose() => Stop();
    }

    // Win32 补充（Raw Input 用）
    public static partial class Win32
    {
        public const int HWND_MESSAGE = -3;
        public const uint WM_QUIT = 0x0012;

        public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASS
        {
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public Win32.POINT pt;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
    }
}
