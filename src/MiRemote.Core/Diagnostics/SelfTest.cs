using System;
using System.Collections.Generic;

namespace MiRemote.Core
{
    public static class SelfTest
    {
        public static bool Run()
        {
            bool ok = true;
            void Expect(bool cond, string msg)
            {
                if (!cond) { ok = false; Console.WriteLine("[SelfTest] FAIL: " + msg); }
            }

            // ---- 场景 A：基础态保护返回键 ----
            {
                var (e, r, _, c) = MakeEngine();
                e.Handle(new ButtonEvent(RemoteKey.Back, true, 0));
                c.Advance(10);
                e.Handle(new ButtonEvent(RemoteKey.Back, false, 0));
                Expect(r.Actions.Count == 1 && r.Actions[0].SameAs(MAction.KeyStroke("delete")), "A protected delete tap immediate");
            }

            // ---- 场景 B：基础方向立即保持光标移动 ----
            {
                var (e, r, _, c) = MakeEngine();
                e.Handle(new ButtonEvent(RemoteKey.Up, true, 0));
                c.Advance(10);
                e.Handle(new ButtonEvent(RemoteKey.Up, false, 0));
                Expect(r.Actions.Count == 1 && r.Actions[0].SameAs(MAction.KeyStroke("up_arrow")), "B protected arrow immediate");
                c.Advance(80);
                Expect(r.Actions.Count == 1, "B no delayed extra action");
            }

            // ---- 场景 C：基础方向两次点按就是两次移动 ----
            {
                var (e, r, _, c) = MakeEngine();
                e.Handle(new ButtonEvent(RemoteKey.Up, true, 0)); c.Advance(10); e.Handle(new ButtonEvent(RemoteKey.Up, false, 0));
                c.Advance(40);
                e.Handle(new ButtonEvent(RemoteKey.Up, true, 0)); c.Advance(10); e.Handle(new ButtonEvent(RemoteKey.Up, false, 0));
                c.Advance(100);
                Expect(r.Actions.Count == 2, "C double cannot replace base cursor movement");
            }

            // ---- 场景 D：ok 长按进瞬时层1 → down 走模式覆盖 → 松开回落 ----
            {
                var (e, r, d, c) = MakeEngine();
                e.Handle(new ButtonEvent(RemoteKey.Ok, true, 0)); c.Advance(100);
                Expect(d.Layers.Count > 0 && d.Layers[^1] == 1, "D layer 1 on hold");
                e.Handle(new ButtonEvent(RemoteKey.Down, true, 0)); c.Advance(10); e.Handle(new ButtonEvent(RemoteKey.Down, false, 0));
                e.Handle(new ButtonEvent(RemoteKey.Ok, false, 0));
                Expect(r.Actions.Count == 1 && r.Actions[0].SameAs(MAction.KeyStroke("k", "right_option")), "D layered tap = k");
                Expect(d.Layers.Count >= 2 && d.Layers[^1] == 0, "D layer back to 0 on release");
            }

            // ---- 场景 E：ok 按住 + 方向上 → 手势 mission_control ----
            {
                var (e, r, d, c) = MakeEngine();
                e.Handle(new ButtonEvent(RemoteKey.Ok, true, 0)); c.Advance(20);
                e.Handle(new ButtonEvent(RemoteKey.Up, true, 0));
                e.Handle(new ButtonEvent(RemoteKey.Up, false, 0));
                e.Handle(new ButtonEvent(RemoteKey.Ok, false, 0));
                c.Advance(200);
                Expect(r.Actions.Count == 1 && r.Actions[0].SameAs(MAction.SystemAction("mission_control")), "E gesture only");
                Expect(d.Layers.Count == 0, "E no layer change");
            }

            // ---- 场景 E2：按稳超过 holdMs 后再按方向，手势仍可用 ----
            {
                var (e, r, d, c) = MakeEngine();
                e.Handle(new ButtonEvent(RemoteKey.Ok, true, 0)); c.Advance(120);
                e.Handle(new ButtonEvent(RemoteKey.Up, true, 0));
                e.Handle(new ButtonEvent(RemoteKey.Up, false, 0));
                e.Handle(new ButtonEvent(RemoteKey.Ok, false, 0));
                Expect(r.Actions.Count == 1 && r.Actions[0].SameAs(MAction.SystemAction("mission_control")), "E2 gesture remains available after hold");
                Expect(d.Layers.Count >= 2 && d.Layers[^1] == 0, "E2 mode still exits cleanly");
            }

            // ---- 场景 F：tv 长按锁定层2，再长按解锁 ----
            {
                var (e, r, d, c) = MakeEngine();
                e.Handle(new ButtonEvent(RemoteKey.Tv, true, 0)); c.Advance(100); e.Handle(new ButtonEvent(RemoteKey.Tv, false, 0));
                e.Handle(new ButtonEvent(RemoteKey.Tv, true, 0)); c.Advance(100); e.Handle(new ButtonEvent(RemoteKey.Tv, false, 0));
                Expect(d.Layers.Count >= 2 && d.Layers[0] == 2 && d.Layers[^1] == 0, "F toggle layer on/off");
                Expect(r.Actions.Count == 0, "F no tap fired while holding");
            }

            // ---- 场景 F1：锁定层 20s 无操作自动退出 ----
            {
                var (e, _, d, c) = MakeEngine();
                e.Handle(new ButtonEvent(RemoteKey.Tv, true, 0)); c.Advance(100); e.Handle(new ButtonEvent(RemoteKey.Tv, false, 0));
                c.Advance(MappingEngine.LockedLayerIdleMs - 1);
                Expect(e.Route.EffectiveLayer == 2, "F1 layer remains before idle deadline");
                e.Handle(new ButtonEvent(RemoteKey.VolUp, true, 0)); e.Handle(new ButtonEvent(RemoteKey.VolUp, false, 0));
                c.Advance(MappingEngine.LockedLayerIdleMs - 1);
                Expect(e.Route.EffectiveLayer == 2, "F1 activity restarts idle deadline");
                c.Advance(1);
                Expect(e.Route.EffectiveLayer == 0, "F1 idle timeout returns to base layer");
                Expect(d.Layers.Count >= 2 && d.Layers[^1] == 0, "F1 idle timeout emits layerChanged(0)");
            }

            // ---- 场景 F1b：任务视图内左右切 Space，Home/菜单发 Esc 退出 ----
            {
                var (e, r, _, c) = MakeEngine();
                TransientSystemUI.MarkEntered();
                e.Handle(new ButtonEvent(RemoteKey.Left, true, 0)); e.Handle(new ButtonEvent(RemoteKey.Left, false, 0));
                e.Handle(new ButtonEvent(RemoteKey.Right, true, 0)); e.Handle(new ButtonEvent(RemoteKey.Right, false, 0));
                e.Handle(new ButtonEvent(RemoteKey.Home, true, 0)); c.Advance(10); e.Handle(new ButtonEvent(RemoteKey.Home, false, 0));
                Expect(r.Actions.Count == 3, "F1b directions switch Spaces and Home exits Task View");
                TransientSystemUI.Clear();
            }

            // ---- 场景 F2：基础态覆盖规则 ----
            {
                var cfg = MakeTestConfig();
                cfg.Profiles["com.example.unsafe"] = new Dictionary<string, KeyBinding>
                {
                    ["ok"] = new KeyBinding { Tap = MAction.Shell("unsafe-ok"), Double = MAction.Shell("unsafe-ok-double") },
                    ["back"] = new KeyBinding { Tap = MAction.Shell("unsafe-back"), Hold = MAction.Shell("unsafe-hold"), Double = MAction.Shell("unsafe-back-double") },
                };
                var c = new ManualClock();
                var r = new RecordingRunner();
                var e = new MappingEngine(cfg, r, null, c);
                e.SetActiveApp("com.example.unsafe");
                e.Handle(new ButtonEvent(RemoteKey.Ok, true, 0)); c.Advance(10); e.Handle(new ButtonEvent(RemoteKey.Ok, false, 0));
                e.Handle(new ButtonEvent(RemoteKey.Back, true, 0)); c.Advance(10); e.Handle(new ButtonEvent(RemoteKey.Back, false, 0));
                Expect(r.Actions.Count == 2, "F2 per-app OK tap 生效、back 仍受保护");
                // global 改不动
                {
                    var g = cfg.DeepClone();
                    g.Profiles["global"]!["ok"]!.Tap = MAction.Shell("global-ok");
                    g.Profiles.Remove("com.example.unsafe");
                    var c2 = new ManualClock();
                    var r2 = new RecordingRunner();
                    var e2 = new MappingEngine(g, r2, null, c2);
                    e2.Handle(new ButtonEvent(RemoteKey.Ok, true, 0)); c2.Advance(10); e2.Handle(new ButtonEvent(RemoteKey.Ok, false, 0));
                    Expect(r2.Actions.Count == 1 && r2.Actions[0].SameAs(MAction.KeyStroke("return")), "F2 global OK tap 仍被基础态保护挡下");
                }
                cfg.Settings.DeleteAllOnHold = true;
                e.SetConfig(cfg);
                e.Handle(new ButtonEvent(RemoteKey.Back, true, 0)); c.Advance(100); e.Handle(new ButtonEvent(RemoteKey.Back, false, 0));
                Expect(r.Actions.Count >= 3, "F2 delete-all hold requires explicit setting");
            }

            // ---- 场景 G：App Profile 只覆盖 OK.tap 时仍继承 gesture ----
            {
                var cfg = MakeTestConfig();
                cfg.Profiles["com.example.app"] = new Dictionary<string, KeyBinding> { ["ok"] = new KeyBinding { Tap = MAction.KeyStroke("space") } };
                var clock = new ManualClock();
                var runner = new RecordingRunner();
                var engine = new MappingEngine(cfg, runner, null, clock);
                engine.SetActiveApp("com.example.app");
                engine.Handle(new ButtonEvent(RemoteKey.Ok, true, 0)); clock.Advance(20);
                engine.Handle(new ButtonEvent(RemoteKey.Up, true, 0));
                engine.Handle(new ButtonEvent(RemoteKey.Up, false, 0));
                engine.Handle(new ButtonEvent(RemoteKey.Ok, false, 0));
                Expect(runner.Actions.Count == 1 && runner.Actions[0].SameAs(MAction.SystemAction("mission_control")), "G profile slot overlay inherits gesture");
            }

            // ---- 配置往返 ----
            {
                var cfg = DefaultConfig.Build();
                var json = ConfigJson.Serialize(cfg);
                var cfg2 = ConfigJson.Deserialize(json);
                Expect(cfg2 is not null && cfg2.Profiles.ContainsKey("global"), "config round-trip");
                Expect(cfg2!.Profiles["global"].ContainsKey("voice"), "voice key preserved");
            }

            // ---- 预设 apply 不崩 ----
            {
                var cfg = DefaultConfig.Build();
                foreach (var p in Presets.All) Presets.Apply(p, ref cfg);
                Expect(cfg.Profiles.Count > 0, "presets apply ok");
            }

            // ---- 语音键：按下=开始，松开=结束（不在 tap 阶段延迟触发） ----
            {
                var cfg = MakeTestConfig();
                cfg.Profiles["global"]["voice"] = new KeyBinding { Tap = MAction.Voice };
                var clock = new ManualClock();
                var runner = new RecordingRunner();
                var engine = new MappingEngine(cfg, runner, null, clock);
                engine.Handle(new ButtonEvent(RemoteKey.Voice, true, 0));
                engine.Handle(new ButtonEvent(RemoteKey.Voice, true, 0));
                Expect(runner.VoiceBegins == 1, "voice key down starts voice once without repeats");
                clock.Advance(500);
                engine.Handle(new ButtonEvent(RemoteKey.Voice, false, 0));
                Expect(runner.VoiceEnds == 1, "voice key up ends voice");
                Expect(runner.Actions.Count == 0, "voice key does not emit delayed tap action");
            }

            {
                var (engine, _, _, _) = MakeEngine();
                Expect(engine.Route.NativeDirections?.Count == 4, "initial routing snapshot is ready before first app change");
                var json = ConfigJson.Serialize(DefaultConfig.Build());
                Expect(json.Contains("\"profiles\"") && json.Contains("\"holdMs\""), "saved configuration uses original camelCase field names");
                bool rejected = false;
                try { ConfigJson.DeserializeAction("{\"type\":\"typo_action\"}"); }
                catch (System.Text.Json.JsonException) { rejected = true; }
                Expect(rejected, "unknown action rejected instead of silently clearing binding");
            }

            if (ok) Console.WriteLine("[SelfTest] all scenarios passed");
            return ok;
        }

        private static (MappingEngine engine, RecordingRunner runner, RecordingDelegate del, ManualClock clock) MakeEngine()
        {
            var clock = new ManualClock();
            var runner = new RecordingRunner();
            var del = new RecordingDelegate();
            var engine = new MappingEngine(MakeTestConfig(), runner, del, clock);
            return (engine, runner, del, clock);
        }

        private static MappingConfig MakeTestConfig()
        {
            var cfg = new MappingConfig();
            cfg.Settings.HoldMs = 100;
            cfg.Settings.DoubleMs = 80;
            cfg.Profiles["global"] = new Dictionary<string, KeyBinding>
            {
                ["back"] = new KeyBinding { Tap = MAction.KeyStroke("escape") },
                ["up"] = new KeyBinding { Tap = MAction.KeyStroke("up_arrow"), Double = MAction.KeyStroke("page_up"), Layers = new() { ["1"] = MAction.KeyStroke("k", "right_option") } },
                ["down"] = new KeyBinding { Tap = MAction.KeyStroke("down_arrow"), Layers = new() { ["1"] = MAction.KeyStroke("k", "right_option") } },
                ["ok"] = new KeyBinding { Tap = MAction.KeyStroke("return"), Hold = MAction.LayerMomentary(1), Gesture = new() { ["up"] = MAction.SystemAction("mission_control"), ["right"] = MAction.KeyStroke("right_arrow") } },
                ["tv"] = new KeyBinding { Tap = MAction.SystemAction("volume_up"), Hold = MAction.LayerToggle(2) },
            };
            return cfg;
        }

        private sealed class RecordingRunner : IActionRunner, IVoiceRunner
        {
            public List<MAction> Actions { get; } = new();
            public int VoiceBegins { get; private set; }
            public int VoiceEnds { get; private set; }
            public void Run(MAction action) => Actions.Add(action);
            public void VoiceBegin() => VoiceBegins++;
            public void VoiceEnd() => VoiceEnds++;
        }

        private sealed class RecordingDelegate : IMappingEngineDelegate
        {
            public List<int> Layers { get; } = new();
            public void MappingLog(string message) { }
            public void LayerChanged(int layer) => Layers.Add(layer);
        }
    }
}
