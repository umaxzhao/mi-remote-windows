using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiRemote.Core
{
    // =====================================================================
    // 动作（配置 JSON 的 Codable 模型，与 macOS 版同构：type 字段区分）
    // =====================================================================

    public abstract record MAction
    {
        public abstract string TypeName { get; }

        /// <summary>写 type 之外的字段。</summary>
        protected virtual void WriteProps(Utf8JsonWriter w) { }

        public void Write(Utf8JsonWriter w)
        {
            w.WriteStartObject();
            w.WriteString("type", TypeName);
            WriteProps(w);
            w.WriteEndObject();
        }

        public string ToJson()
        {
            using var ms = new System.IO.MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
                Write(w);
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }

        /// <summary>语义相等（记录里的数组按内容比较，自测用）。</summary>
        public bool SameAs(MAction? other) =>
            other != null && string.Equals(ToJson(), other.ToJson(), StringComparison.Ordinal);

        public static MAction KeyStroke(string key, params string[] mods) => new KeyStrokeAction(key, mods);
        public static MAction SystemAction(string name) => new SystemActionType(name);
        public static MAction OpenApp(string target) => new OpenAppAction(target);
        public static MAction Shell(string command) => new ShellAction(command);
        public static MAction Overlay(string name) => new OverlayAction(name);
        public static MAction LayerToggle(int layer) => new LayerToggleAction(layer);
        public static MAction LayerMomentary(int layer) => new LayerMomentaryAction(layer);
        public static MAction TabJump(int? dir = null, int? index = null) => new TabJumpAction(dir, index);
        public static MAction WindowCycle(string scope = "app") => new WindowCycleAction(scope);
        public static MAction Macro(params MacroStep[] steps) => new MacroAction(new List<MacroStep>(steps));
        public static readonly MAction Voice = new VoiceAction();
        public static readonly MAction FocusInput = new FocusInputAction();
        public static readonly MAction MouseMode = new MouseModeAction();
        public static readonly MAction None = new NoneAction();

        /// <summary>展示名（UI 用）。</summary>
        public virtual string Describe() => TypeName;
    }

    public sealed record KeyStrokeAction(string Key, string[] Mods) : MAction
    {
        public override string TypeName => "key_stroke";
        protected override void WriteProps(Utf8JsonWriter w)
        {
            w.WriteString("key", Key);
            w.WriteStartArray("mods");
            foreach (var m in Mods) w.WriteStringValue(m);
            w.WriteEndArray();
        }
        public override string Describe() =>
            Mods.Length == 0 ? Key : string.Join("+", Mods) + "+" + Key;
    }

    public sealed record SystemActionType(string Name) : MAction
    {
        public override string TypeName => "system";
        protected override void WriteProps(Utf8JsonWriter w) => w.WriteString("value", Name);
        public override string Describe() => "系统:" + Name;
    }

    public sealed record OpenAppAction(string Target) : MAction
    {
        public override string TypeName => "open_app";
        protected override void WriteProps(Utf8JsonWriter w) => w.WriteString("value", Target);
        public override string Describe() => "打开:" + Target;
    }

    public sealed record ShellAction(string Command) : MAction
    {
        public override string TypeName => "shell";
        protected override void WriteProps(Utf8JsonWriter w) => w.WriteString("value", Command);
        public override string Describe() => "命令:" + Command;
    }

    public sealed record VoiceAction : MAction
    {
        public override string TypeName => "voice";
        public override string Describe() => "语音输入";
    }

    public sealed record LayerMomentaryAction(int Layer) : MAction
    {
        public override string TypeName => "layer_momentary";
        protected override void WriteProps(Utf8JsonWriter w) => w.WriteNumber("value", Layer);
        public override string Describe() => $"临时进层{Layer}";
    }

    public sealed record LayerToggleAction(int Layer) : MAction
    {
        public override string TypeName => "layer_toggle";
        protected override void WriteProps(Utf8JsonWriter w) => w.WriteNumber("value", Layer);
        public override string Describe() => $"开关层{Layer}";
    }

    public sealed record WindowCycleAction(string Scope) : MAction
    {
        public override string TypeName => "window_cycle";
        protected override void WriteProps(Utf8JsonWriter w) => w.WriteString("scope", Scope);
        public override string Describe() => Scope == "global" ? "全局切窗口" : "同应用切窗口";
    }

    public sealed record TabJumpAction(int? Dir, int? Index) : MAction
    {
        public override string TypeName => "tab_jump";
        protected override void WriteProps(Utf8JsonWriter w)
        {
            if (Dir.HasValue) w.WriteNumber("dir", Dir.Value);
            if (Index.HasValue) w.WriteNumber("index", Index.Value);
        }
        public override string Describe() => Index.HasValue ? $"跳到第{Index}个标签" : (Dir >= 0 ? "下一个标签" : "上一个标签");
    }

    public sealed record FocusInputAction : MAction
    {
        public override string TypeName => "focus_input";
        public override string Describe() => "聚焦输入框";
    }

    public sealed record MouseModeAction : MAction
    {
        public override string TypeName => "mouse_mode";
        public override string Describe() => "鼠标模式开关";
    }

    public sealed record MacroAction(List<MacroStep> Steps) : MAction
    {
        public override string TypeName => "macro";
        protected override void WriteProps(Utf8JsonWriter w)
        {
            w.WriteStartArray("steps");
            foreach (var s in Steps) s.Write(w);
            w.WriteEndArray();
        }
        public override string Describe() => $"宏({Steps.Count}步)";
    }

    public sealed record OverlayAction(string Name) : MAction
    {
        public override string TypeName => "overlay";
        protected override void WriteProps(Utf8JsonWriter w) => w.WriteString("value", Name);
        public override string Describe() => Name switch
        {
            "window_picker" => "窗口选择器",
            "system_menu" => "系统功能菜单",
            "app_wheel" => "App 轮盘",
            "tutorial" => "当前映射教程",
            _ => "浮层:" + Name,
        };
    }

    public sealed record NoneAction : MAction
    {
        public override string TypeName => "none";
        public override string Describe() => "无动作";
    }

    // =====================================================================
    // 宏步骤：任意动作 / 延时 / 文本
    // =====================================================================

    public abstract class MacroStep
    {
        public abstract void Write(Utf8JsonWriter w);

        /// <summary>单步延时上限：来源可能是任意 config.json，超大值会让执行层卡死。</summary>
        public const int MaxDelayMs = 60_000;

        public static MacroStep Delay(int ms) => new DelayStep(Math.Clamp(ms, 0, MaxDelayMs));
        public static MacroStep Text(string text) => new TextStep(text);
        public static MacroStep Of(MAction action) => new ActionStep(action);
    }

    public sealed class DelayStep : MacroStep
    {
        public int Ms { get; }
        public DelayStep(int ms) => Ms = ms;
        public override void Write(Utf8JsonWriter w)
        {
            w.WriteStartObject();
            w.WriteString("type", "delay");
            w.WriteNumber("ms", Ms);
            w.WriteEndObject();
        }
    }

    public sealed class TextStep : MacroStep
    {
        public new string Text { get; }
        public TextStep(string text) => Text = text;
        public override void Write(Utf8JsonWriter w)
        {
            w.WriteStartObject();
            w.WriteString("type", "text");
            w.WriteString("value", Text);
            w.WriteEndObject();
        }
    }

    public sealed class ActionStep : MacroStep
    {
        public MAction Action { get; }
        public ActionStep(MAction action) => Action = action;
        public override void Write(Utf8JsonWriter w) => Action.Write(w);
    }

    // =====================================================================
    // 绑定 / 配置
    // =====================================================================

    /// <summary>单键绑定：五个动作位（均可缺省）。</summary>
    public sealed class KeyBinding
    {
        public MAction? Tap { get; set; }
        public MAction? Hold { get; set; }
        [JsonPropertyName("double")]
        public MAction? Double { get; set; }
        /// <summary>"up"/"down"/"left"/"right"（仅 OK 键生效）。</summary>
        public Dictionary<string, MAction>? Gesture { get; set; }
        /// <summary>"1"/"2"/"3" → 该层激活时的 tap 替代。</summary>
        public Dictionary<string, MAction>? Layers { get; set; }

        public KeyBinding Clone() => new()
        {
            Tap = Tap, Hold = Hold, Double = Double,
            Gesture = Gesture is null ? null : new Dictionary<string, MAction>(Gesture),
            Layers = Layers is null ? null : new Dictionary<string, MAction>(Layers),
        };
    }

    public sealed class VoiceTriggerRule
    {
        public string KeyName { get; set; } = "left_windows";
        public string Mode { get; set; } = "hold";
        /// <summary>Windows 版不需要切换输入法，保留字段仅为与 macOS 配置文件互通。</summary>
        public string? ImeBundlePrefix { get; set; }
    }

    public sealed class MappingConfig
    {
        public const int CurrentVersion = 8;

        public sealed class SettingsBlock
        {
            public int HoldMs { get; set; } = 350;
            public int DoubleMs { get; set; } = 250;
            public bool? DeleteAllOnHold { get; set; }
            public int? RemoteVendorID { get; set; }
            public int? RemoteProductID { get; set; }
            /// <summary>Windows 版专有：语音键行为。"doubao"=长按右 Alt（豆包输入法），“dictation”=Win+H，“none”=关闭。</summary>
            public string VoiceFallback { get; set; } = "doubao";
            /// <summary>语音触发键（按住说话）。豆包输入法 Windows 版默认右 Alt。</summary>
            public string VoiceTriggerKey { get; set; } = "right_alt";
            /// <summary>"hold"=按住说话松开结束；"tap"=按一下开始/再按结束。</summary>
            public string VoiceTriggerMode { get; set; } = "hold";
            /// <summary>Windows 版专有：是否用低级键盘钩子兜底吞掉遥控器键（Raw Input NOLEGACY 失效时用）。</summary>
            public bool UseKeyboardHook { get; set; }
            /// <summary>Windows 版专有：按键重复延迟/间隔（毫秒）。-1 表示关闭自动重复。</summary>
            public int RepeatDelayMs { get; set; } = 400;
            public int RepeatIntervalMs { get; set; } = 33;
            /// <summary>启动后是否自动开始监听。</summary>
            public bool StartEnabled { get; set; } = true;
            [Obsolete("仅用于兼容 macOS 配置文件")] public string? VoiceOutputDevice { get; set; }
            [Obsolete("仅用于兼容 macOS 配置文件")] public List<string>? TerminalApps { get; set; }
        }

        public int Version { get; set; } = CurrentVersion;
        public SettingsBlock Settings { get; set; } = new();
        /// <summary>profile 名 → (键名 → 绑定)。"global" 必在；Windows 版其余为进程名（小写、无 .exe）。</summary>
        public Dictionary<string, Dictionary<string, KeyBinding>> Profiles { get; set; } = new();
        public Dictionary<string, VoiceTriggerRule>? VoiceProfiles { get; set; }

        public MappingConfig DeepClone()
        {
            var json = ConfigJson.Serialize(this);
            return ConfigJson.Deserialize(json) ?? new MappingConfig();
        }
    }

    // =====================================================================
    // JSON（严格解码：未知 type 抛错，拼写错误在加载配置时即暴露）
    // =====================================================================

    public sealed class ActionJsonConverter : JsonConverter<MAction>
    {
        public override MAction Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("动作必须是 JSON 对象");
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                throw new JsonException("动作缺少 type 字段");
            var type = typeEl.GetString()!;
            switch (type)
            {
                case "key_stroke":
                    return new KeyStrokeAction(root.GetStringOrThrow("key"), root.GetStringArray("mods"));
                case "system":
                    return new SystemActionType(root.GetStringOrThrow("value"));
                case "open_app":
                    return new OpenAppAction(root.GetStringOrThrow("value"));
                case "shell":
                    return new ShellAction(root.GetStringOrThrow("value"));
                case "voice":
                    return new VoiceAction();
                case "layer_momentary":
                    return new LayerMomentaryAction(root.GetIntOrThrow("value"));
                case "layer_toggle":
                    return new LayerToggleAction(root.GetIntOrThrow("value"));
                case "window_cycle":
                    return new WindowCycleAction(root.TryGetProperty("scope", out var sc) ? sc.GetString() ?? "app" : "app");
                case "tab_jump":
                    return new TabJumpAction(
                        root.TryGetProperty("dir", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : null,
                        root.TryGetProperty("index", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : null);
                case "focus_input":
                    return new FocusInputAction();
                case "mouse_mode":
                    return new MouseModeAction();
                case "macro":
                    {
                        var steps = new List<MacroStep>();
                        if (root.TryGetProperty("steps", out var arr) && arr.ValueKind == JsonValueKind.Array)
                            foreach (var el in arr.EnumerateArray())
                                steps.Add(ReadStep(el));
                        return new MacroAction(steps);
                    }
                case "overlay":
                    return new OverlayAction(root.GetStringOrThrow("value"));
                case "none":
                    return new NoneAction();
                default:
                    throw new JsonException($"未知动作 type: {type}");
            }
        }

        private static MacroStep ReadStep(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) throw new JsonException("宏步骤必须是 JSON 对象");
            var t = el.TryGetProperty("type", out var te) ? te.GetString() : null;
            switch (t)
            {
                case "delay":
                    {
                        var ms = el.GetIntOrThrow("ms");
                        if (ms < 0) throw new JsonException($"delay 不能为负: {ms}");
                        return MacroStep.Delay(ms);
                    }
                case "text":
                    return MacroStep.Text(el.GetStringOrThrow("value"));
                default:
                    return MacroStep.Of(ActionJson.ReadAction(el));
            }
        }

        public override void Write(Utf8JsonWriter writer, MAction value, JsonSerializerOptions options) => value.Write(writer);
    }

    public static class ActionJson
    {
        public static MAction? TryRead(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Null) return null;
            var clone = el.Clone();
            return clone.Deserialize<MAction>(JsonOptions.Default);
        }

        public static MAction ReadAction(JsonElement el) => TryRead(el) ?? throw new JsonException("动作为空");
    }

    internal static class JsonElementExtensions
    {
        public static string GetStringOrThrow(this JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
                throw new JsonException($"字段缺失或非字符串: {name}");
            return v.GetString()!;
        }

        public static int GetIntOrThrow(this JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number)
                throw new JsonException($"字段缺失或非数字: {name}");
            return v.GetInt32();
        }

        public static string[] GetStringArray(this JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var item in v.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString()!);
            return list.ToArray();
        }
    }
}
