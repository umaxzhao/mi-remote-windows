using System;
using System.Collections.Generic;

namespace MiRemote.Core
{
    /// <summary>
    /// 一个可应用到 MappingConfig 的预设。
    /// - BundleId == null：层绑定类，并入 "global"（绑定通常写在 layers 槽）。
    /// - BundleId != null：per-app profile，并入对应 profile（Windows 版为进程名小写）。
    /// </summary>
    public sealed class Preset
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Note { get; init; } = "";
        public string? BundleId { get; init; }
        public Dictionary<string, KeyBinding> Bindings { get; init; } = new();

        public static MAction Ks(string key, params string[] mods) => MAction.KeyStroke(key, mods);
    }

    public static class Presets
    {
        public static Preset CoreGestures { get; } = new()
        {
            Id = "core_gestures", DisplayName = "效率层绑定",
            Note = "快捷控制层（层1）与导航层（层3）的效率绑定。",
            Bindings = new()
            {
                ["menu"] = new KeyBinding { Layers = new() { ["1"] = MAction.SystemAction("next_app"), ["3"] = MAction.LayerToggle(3) } },
                ["left"] = new KeyBinding { Layers = new() { ["1"] = MAction.SystemAction("space_left"), ["3"] = MAction.Macro(MacroStep.Of(MAction.SystemAction("previous_app")), MacroStep.Delay(250), MacroStep.Of(MAction.FocusInput)) } },
                ["right"] = new KeyBinding { Layers = new() { ["1"] = MAction.SystemAction("space_right"), ["3"] = MAction.Macro(MacroStep.Of(MAction.SystemAction("next_app")), MacroStep.Delay(250), MacroStep.Of(MAction.FocusInput)) } },
                ["up"] = new KeyBinding { Layers = new() { ["3"] = MAction.Macro(MacroStep.Of(MAction.SystemAction("previous_app_window")), MacroStep.Delay(250), MacroStep.Of(MAction.FocusInput)) } },
                ["down"] = new KeyBinding { Layers = new() { ["3"] = MAction.Macro(MacroStep.Of(MAction.SystemAction("next_app_window")), MacroStep.Delay(250), MacroStep.Of(MAction.FocusInput)) } },
                ["ok"] = new KeyBinding { Layers = new() { ["3"] = MAction.FocusInput } },
                ["back"] = new KeyBinding { Layers = new() { ["1"] = MAction.SystemAction("previous_app"), ["3"] = MAction.SystemAction("space_left") } },
                ["home"] = new KeyBinding { Layers = new() { ["1"] = MAction.SystemAction("show_desktop"), ["2"] = Preset.Ks("c", "ctrl"), ["3"] = MAction.SystemAction("space_right") } },
                ["power"] = new KeyBinding { Layers = new() { ["3"] = MAction.SystemAction("show_desktop") } },
                ["tv"] = new KeyBinding { Layers = new() { ["1"] = MAction.SystemAction("app_expose"), ["3"] = MAction.SystemAction("app_expose") } },
            }
        };

        public static Preset AiApproval { get; } = new()
        {
            Id = "ai_approval", DisplayName = "App 控制模式 · AI 批准（通用）",
            Note = "TV 单击进/出 App 控制模式（层2）：OK=批准(Enter)、返回=Esc、上下=选项导航、音量±=切 Agent/标签、菜单=Shift+Tab。",
            Bindings = new()
            {
                ["ok"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("return") } },
                ["back"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("escape") } },
                ["up"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("up_arrow") } },
                ["down"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("down_arrow") } },
                ["menu"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("tab", "shift") } },
                ["home"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("c", "ctrl") } },
                ["power"] = new KeyBinding { Layers = new() { ["2"] = MAction.FocusInput } },
                ["volUp"] = new KeyBinding { Layers = new() { ["2"] = MAction.TabJump(dir: 1) } },
                ["volDown"] = new KeyBinding { Layers = new() { ["2"] = MAction.TabJump(dir: -1) } },
                ["left"] = new KeyBinding { Layers = new() { ["2"] = MAction.TabJump(dir: -1) } },
                ["right"] = new KeyBinding { Layers = new() { ["2"] = MAction.TabJump(dir: 1) } },
            }
        };

        private static MAction Ks(string key, params string[] mods) => MAction.KeyStroke(key, mods);

        private static Preset Make(string id, string name, string? bundleId, params (string key, KeyBinding binding)[] b)
        {
            var dict = new Dictionary<string, KeyBinding>();
            foreach (var (k, v) in b) dict[k] = v;
            return new Preset { Id = id, DisplayName = name, BundleId = bundleId, Bindings = dict };
        }

        public static Preset TerminalAi { get; } = new()
        {
            Id = "ai_terminal", DisplayName = "终端 · AI CLI (Windows Terminal / PowerShell / VS Code)",
            BundleId = "windowsterminal",
            Bindings = new()
            {
                ["left"] = new KeyBinding { Tap = MAction.TabJump(dir: -1), Layers = new() { ["1"] = Preset.Ks("left_bracket", "ctrl") } },
                ["right"] = new KeyBinding { Tap = MAction.TabJump(dir: 1), Layers = new() { ["1"] = Preset.Ks("right_bracket", "ctrl") } },
                ["up"] = new KeyBinding { Tap = Preset.Ks("up_arrow"), Layers = new() { ["1"] = Preset.Ks("up_arrow", "ctrl", "alt") } },
                ["down"] = new KeyBinding { Tap = Preset.Ks("down_arrow"), Layers = new() { ["1"] = Preset.Ks("down_arrow", "ctrl", "alt") } },
                ["ok"] = new KeyBinding { Tap = Preset.Ks("return"), Hold = Preset.Ks("c", "ctrl") },
                ["back"] = new KeyBinding { Tap = Preset.Ks("escape"), Hold = Preset.Ks("u", "ctrl") },
                ["home"] = new KeyBinding { Layers = new() { ["2"] = MAction.FocusInput } },
            }
        };

        public static Preset Code { get; } = new()
        {
            Id = "editor_vscode", DisplayName = "VS Code",
            BundleId = "code",
            Bindings = new()
            {
                ["left"] = new KeyBinding { Tap = MAction.TabJump(dir: -1) },
                ["right"] = new KeyBinding { Tap = MAction.TabJump(dir: 1) },
                ["up"] = new KeyBinding { Tap = Preset.Ks("up_arrow") },
                ["down"] = new KeyBinding { Tap = Preset.Ks("down_arrow") },
                ["ok"] = new KeyBinding { Tap = Preset.Ks("return") },
                ["back"] = new KeyBinding { Tap = Preset.Ks("escape") },
                ["home"] = new KeyBinding { Layers = new() { ["2"] = MAction.FocusInput } },
            }
        };

        public static Preset Chrome { get; } = new()
        {
            Id = "browser_chrome", DisplayName = "Google Chrome / Edge",
            BundleId = "chrome",
            Bindings = new()
            {
                ["left"] = new KeyBinding { Tap = MAction.TabJump(dir: -1) },
                ["right"] = new KeyBinding { Tap = MAction.TabJump(dir: 1) },
                ["up"] = new KeyBinding { Tap = Preset.Ks("page_up") },
                ["down"] = new KeyBinding { Tap = Preset.Ks("page_down") },
                ["ok"] = new KeyBinding { Tap = Preset.Ks("return") },
                ["back"] = new KeyBinding { Tap = Preset.Ks("left_bracket", "ctrl") },
                ["home"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("l", "ctrl") } },
                ["menu"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("k", "ctrl") } },
            }
        };

        public static Preset WeChat { get; } = new()
        {
            Id = "chat_wechat", DisplayName = "微信",
            BundleId = "wechat",
            Bindings = new()
            {
                ["up"] = new KeyBinding { Tap = Preset.Ks("up_arrow") },
                ["down"] = new KeyBinding { Tap = Preset.Ks("down_arrow") },
                ["left"] = new KeyBinding { Tap = Preset.Ks("page_up") },
                ["right"] = new KeyBinding { Tap = Preset.Ks("page_down") },
                ["ok"] = new KeyBinding { Tap = Preset.Ks("return") },
                ["back"] = new KeyBinding { Tap = Preset.Ks("escape") },
                ["home"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("f", "ctrl") } },
            }
        };

        public static Preset Feishu { get; } = new()
        {
            Id = "chat_feishu", DisplayName = "飞书 / Lark",
            BundleId = "feishu",
            Bindings = new()
            {
                ["ok"] = new KeyBinding { Tap = Preset.Ks("return", "ctrl") },
                ["menu"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("d", "alt", "shift") } },
                ["home"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("k", "ctrl") } },
            }
        };

        public static Preset Zoom { get; } = new()
        {
            Id = "meeting_zoom", DisplayName = "Zoom",
            BundleId = "zoom",
            Bindings = new()
            {
                ["tv"] = new KeyBinding { Tap = Preset.Ks("a", "ctrl", "shift"), Double = Preset.Ks("v", "ctrl", "shift") },
            }
        };

        public static Preset Vlc { get; } = new()
        {
            Id = "media_vlc", DisplayName = "VLC",
            BundleId = "vlc",
            Bindings = new()
            {
                ["ok"] = new KeyBinding { Tap = Preset.Ks("space") },
                ["left"] = new KeyBinding { Tap = Preset.Ks("left_arrow", "ctrl", "alt") },
                ["right"] = new KeyBinding { Tap = Preset.Ks("right_arrow", "ctrl", "alt") },
                ["up"] = new KeyBinding { Tap = Preset.Ks("f", "ctrl") },
                ["menu"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("s") } },
            }
        };

        public static Preset PowerPoint { get; } = new()
        {
            Id = "present_powerpoint", DisplayName = "PowerPoint",
            BundleId = "powerpnt",
            Bindings = new()
            {
                ["left"] = new KeyBinding { Tap = Preset.Ks("left_arrow") },
                ["right"] = new KeyBinding { Tap = Preset.Ks("right_arrow") },
                ["ok"] = new KeyBinding { Tap = Preset.Ks("f5") },
                ["menu"] = new KeyBinding { Layers = new() { ["2"] = Preset.Ks("b") } },
            }
        };

        public static List<Preset> All { get; } = new()
        {
            CoreGestures, AiApproval, TerminalAi, Code, Chrome, WeChat, Feishu, Zoom, Vlc, PowerPoint,
        };

        public static void Apply(Preset preset, ref MappingConfig config, bool force = false)
        {
            var profileKey = preset.BundleId ?? "global";
            if (!config.Profiles.TryGetValue(profileKey, out var profile)) profile = new Dictionary<string, KeyBinding>();
            foreach (var (remoteKey, presetBinding) in preset.Bindings)
            {
                if (!profile.TryGetValue(remoteKey, out var existing)) existing = new KeyBinding();
                Merge(presetBinding, ref existing, force);
                profile[remoteKey] = existing;
            }
            config.Profiles[profileKey] = profile;
        }

        private static void Merge(KeyBinding src, ref KeyBinding dst, bool force)
        {
            if (src.Tap is not null && (force || dst.Tap is null)) dst.Tap = src.Tap;
            if (src.Hold is not null && (force || dst.Hold is null)) dst.Hold = src.Hold;
            if (src.Double is not null && (force || dst.Double is null)) dst.Double = src.Double;
            if (src.Gesture is not null)
            {
                dst.Gesture ??= new Dictionary<string, MAction>();
                foreach (var kv in src.Gesture)
                    if (force || !dst.Gesture.ContainsKey(kv.Key)) dst.Gesture[kv.Key] = kv.Value;
            }
            if (src.Layers is not null)
            {
                dst.Layers ??= new Dictionary<string, MAction>();
                foreach (var kv in src.Layers)
                    if (force || !dst.Layers.ContainsKey(kv.Key)) dst.Layers[kv.Key] = kv.Value;
            }
        }

        public static List<MAction> ActionsIn(KeyBinding binding)
        {
            var outp = new List<MAction>();
            if (binding.Tap is not null) outp.Add(binding.Tap);
            if (binding.Hold is not null) outp.Add(binding.Hold);
            if (binding.Double is not null) outp.Add(binding.Double);
            if (binding.Gesture is not null) outp.AddRange(binding.Gesture.Values);
            if (binding.Layers is not null) outp.AddRange(binding.Layers.Values);
            return outp;
        }
    }
}
