using System;
using System.Collections.Generic;

namespace MiRemote.Core
{
    /// <summary>
    /// 遥控器 13 个实体键（HID 键盘页 usage，实测表与 macOS 版一致）。
    /// JSON 里统一使用 RawName（power/voice/up/...），以便与 macOS 版 config.json 互通。
    /// </summary>
    public enum RemoteKey
    {
        Power,
        Voice,
        Up,
        Down,
        Left,
        Right,
        Ok,
        Back,
        Home,
        Menu,
        Tv,
        VolUp,
        VolDown,
    }

    public static class RemoteKeys
    {
        /// <summary>usage（键盘页 0x07）→ 键。</summary>
        public static readonly IReadOnlyDictionary<uint, RemoteKey> UsageMap = new Dictionary<uint, RemoteKey>
        {
            [0x66] = RemoteKey.Power,
            [0x3E] = RemoteKey.Voice,
            [0x52] = RemoteKey.Up,
            [0x51] = RemoteKey.Down,
            [0x50] = RemoteKey.Left,
            [0x4F] = RemoteKey.Right,
            [0x28] = RemoteKey.Ok,
            [0xF1] = RemoteKey.Back,     // 超出标准键盘 usage 范围，Windows 上同样不产生虚拟键，只能从原始 report 读
            [0x4A] = RemoteKey.Home,
            [0x65] = RemoteKey.Menu,
            [0x35] = RemoteKey.Tv,
            [0x80] = RemoteKey.VolUp,
            [0x81] = RemoteKey.VolDown,
        };

        /// <summary>消费者页（0x0C）usage → 键。遥控器音量键走这条通道。</summary>
        public static readonly IReadOnlyDictionary<ushort, RemoteKey> ConsumerUsageMap = new Dictionary<ushort, RemoteKey>
        {
            [0xE9] = RemoteKey.VolUp,
            [0xEA] = RemoteKey.VolDown,
            [0xE2] = RemoteKey.VolDown,  // 占位：静音单独由系统处理，这里不映射
        };

        private static readonly Dictionary<RemoteKey, string> RawNames = new()
        {
            [RemoteKey.Power] = "power",
            [RemoteKey.Voice] = "voice",
            [RemoteKey.Up] = "up",
            [RemoteKey.Down] = "down",
            [RemoteKey.Left] = "left",
            [RemoteKey.Right] = "right",
            [RemoteKey.Ok] = "ok",
            [RemoteKey.Back] = "back",
            [RemoteKey.Home] = "home",
            [RemoteKey.Menu] = "menu",
            [RemoteKey.Tv] = "tv",
            [RemoteKey.VolUp] = "volUp",
            [RemoteKey.VolDown] = "volDown",
        };

        private static readonly Dictionary<string, RemoteKey> ByRawName =
            new(RawNames.ToNameKeyMap(), StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<RemoteKey, string> DisplayNames = new()
        {
            [RemoteKey.Power] = "电源",
            [RemoteKey.Voice] = "语音",
            [RemoteKey.Up] = "上",
            [RemoteKey.Down] = "下",
            [RemoteKey.Left] = "左",
            [RemoteKey.Right] = "右",
            [RemoteKey.Ok] = "OK",
            [RemoteKey.Back] = "返回",
            [RemoteKey.Home] = "主页",
            [RemoteKey.Menu] = "菜单",
            [RemoteKey.Tv] = "TV",
            [RemoteKey.VolUp] = "音量+",
            [RemoteKey.VolDown] = "音量-",
        };

        public static string RawName(this RemoteKey key) => RawNames[key];
        public static string DisplayName(this RemoteKey key) => DisplayNames[key];
        public static RemoteKey[] All { get; } = (RemoteKey[])Enum.GetValues(typeof(RemoteKey));

        public static bool TryParse(string? raw, out RemoteKey key)
        {
            if (raw != null && ByRawName.TryGetValue(raw, out key)) return true;
            key = default;
            return false;
        }

        private static Dictionary<string, RemoteKey> ToNameKeyMap(this Dictionary<RemoteKey, string> src)
        {
            var map = new Dictionary<string, RemoteKey>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in src) map[v] = k;
            return map;
        }
    }

    /// <summary>原始按键事件（HID 监听产出）。</summary>
    public readonly struct ButtonEvent
    {
        public RemoteKey Key { get; }
        public bool IsDown { get; }
        public long TimestampMs { get; }

        public ButtonEvent(RemoteKey key, bool isDown, long timestampMs = 0)
        {
            Key = key;
            IsDown = isDown;
            TimestampMs = timestampMs;
        }
    }

    /// <summary>遥控器 USB 识别信息（config.json settings 可覆盖，用于换其他型号）。</summary>
    public static class RemoteIdentity
    {
        public const int DefaultVendorId = 0x2717;
        public const int DefaultProductId = 0x32B8;

        private static int _vendorId = DefaultVendorId;
        private static int _productId = DefaultProductId;

        public static int VendorId => _vendorId;
        public static int ProductId => _productId;

        public static void Configure(int? vendorId, int? productId)
        {
            _vendorId = vendorId ?? DefaultVendorId;
            _productId = productId ?? DefaultProductId;
        }

        /// <summary>设备名里用于匹配的片段（Raw Input 的 RIDI_DEVICENAME 形如 \?\HID#VID_2717&PID_32B8...）。</summary>
        public static bool Matches(string deviceName) =>
            deviceName.Contains($"VID_{_vendorId:X4}", StringComparison.OrdinalIgnoreCase) &&
            deviceName.Contains($"PID_{_productId:X4}", StringComparison.OrdinalIgnoreCase);
    }
}
