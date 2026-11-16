using System;
using System.IO;
using System.Linq;

namespace MiRemote.Core
{
    public static class DefaultConfig
    {
        public static MappingConfig Build()
        {
            var cfg = new MappingConfig();
            cfg.VoiceProfiles = new Dictionary<string, VoiceTriggerRule> { ["global"] = new VoiceTriggerRule() };
            cfg.Settings.VoiceFallback = "dictation";
            cfg.Settings.UseKeyboardHook = false;

            cfg.Profiles["global"] = new Dictionary<string, KeyBinding>
            {
                ["up"] = new KeyBinding { Tap = MAction.KeyStroke("up_arrow"), Layers = new() { ["1"] = MAction.SystemAction("volume_up") } },
                ["down"] = new KeyBinding { Tap = MAction.KeyStroke("down_arrow"), Layers = new() { ["1"] = MAction.SystemAction("volume_down") } },
                ["left"] = new KeyBinding { Tap = MAction.KeyStroke("left_arrow") },
                ["right"] = new KeyBinding { Tap = MAction.KeyStroke("right_arrow") },
                ["ok"] = new KeyBinding { Tap = MAction.KeyStroke("return") },
                ["back"] = new KeyBinding { Tap = MAction.KeyStroke("delete"), Layers = new() { ["1"] = MAction.SystemAction("previous_app") } },
                ["menu"] = new KeyBinding { Tap = MAction.Overlay("window_picker"), Hold = MAction.Overlay("system_menu"), Layers = new() { ["1"] = MAction.SystemAction("next_app") } },
                ["home"] = new KeyBinding { Tap = MAction.SystemAction("task_view"), Hold = MAction.Overlay("tutorial"), Layers = new() { ["1"] = MAction.SystemAction("task_view") } },
                ["tv"] = new KeyBinding { Tap = MAction.LayerToggle(2), Hold = MAction.Overlay("app_wheel"), Layers = new() { ["1"] = MAction.SystemAction("app_expose") } },
                ["power"] = new KeyBinding { Tap = MAction.SystemAction("display_sleep"), Hold = MAction.MouseMode },
                ["volUp"] = new KeyBinding { Tap = MAction.SystemAction("volume_up") },
                ["volDown"] = new KeyBinding { Tap = MAction.SystemAction("volume_down") },
                ["voice"] = new KeyBinding { Tap = MAction.Voice },
            };

            foreach (var p in Presets.All) Presets.Apply(p, ref cfg);
            return cfg;
        }
    }

    public sealed class ConfigStore
    {
        public string ConfigPath { get; }
        private readonly Logger _logger;

        public ConfigStore(Logger logger)
        {
            _logger = logger;
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiRemote");
            Directory.CreateDirectory(dir);
            ConfigPath = Path.Combine(dir, "config.json");
        }

        public MappingConfig Load()
        {
            if (!File.Exists(ConfigPath))
            {
                var cfg = DefaultConfig.Build();
                Save(cfg);
                return cfg;
            }
            try
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = ConfigJson.Deserialize(json);
                if (cfg is null) throw new Exception("反序列化返回空");
                // 迁移：版本不一致时仍尽量保留用户设置；最低限度确保 global 存在。
                if (!cfg.Profiles.ContainsKey("global")) cfg.Profiles["global"] = new Dictionary<string, KeyBinding>();
                RemoteIdentity.Configure(cfg.Settings.RemoteVendorID, cfg.Settings.RemoteProductID);
                return cfg;
            }
            catch (Exception ex)
            {
                _logger.Warn($"配置加载失败，回退默认: {ex.Message}");
                var cfg = DefaultConfig.Build();
                Save(cfg);
                return cfg;
            }
        }

        public void Save(MappingConfig config)
        {
            try
            {
                File.WriteAllText(ConfigPath, ConfigJson.Serialize(config));
            }
            catch (Exception ex)
            {
                _logger.Error($"保存配置失败: {ex.Message}");
            }
        }
    }
}
