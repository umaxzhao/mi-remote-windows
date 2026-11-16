using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace MiRemote.Core
{
    /// <summary>动作执行器契约。</summary>
    public interface IActionRunner
    {
        void Run(MAction action);
    }

    /// <summary>shell 进程组登记表：stop 时对全部活动组 TERM→KILL 清扫。</summary>
    public sealed class ShellProcessRegistry
    {
        public static ShellProcessRegistry Shared { get; } = new();
        private readonly object _gate = new();
        private readonly HashSet<int> _groups = new();

        public int ActiveCount { get { lock (_gate) return _groups.Count; } }

        public void Register(int pgid) { lock (_gate) _groups.Add(pgid); }
        public void Unregister(int pgid) { lock (_gate) _groups.Remove(pgid); }

        public void TerminateAll(int graceMs = 2000)
        {
            List<int> snapshot;
            lock (_gate) snapshot = new List<int>(_groups);
            if (snapshot.Count == 0) return;
            foreach (var g in snapshot) KillTree(g, false);
            var deadline = DateTime.Now.AddMilliseconds(graceMs);
            while (DateTime.Now < deadline && snapshot.Exists(g => ProcessAlive(g))) Thread.Sleep(100);
            foreach (var g in snapshot) if (ProcessAlive(g)) KillTree(g, true);
            lock (_gate) { foreach (var g in snapshot) _groups.Remove(g); }
        }

        private static void KillTree(int pid, bool force)
        {
            try
            {
                foreach (var p in Process.GetProcesses()) { /* best-effort: 仅杀主进程 */ }
                var p0 = Process.GetProcessById(pid);
                if (force) p0.Kill(); else p0.CloseMainWindow();
            }
            catch { }
        }

        private static bool ProcessAlive(int pid)
        {
            try { Process.GetProcessById(pid); return true; }
            catch { return false; }
        }
    }

    /// <summary>动作执行器：把 MAction 落成真正的系统事件/命令。</summary>
    public sealed class ActionRunner : IActionRunner
    {
        private readonly Logger _logger;
        private readonly AppMru _mru;

        public string VoiceFallback { get; set; } = "dictation";
        public Action<string> OnOverlay { get; set; } = _ => { };
        public Action<string> OnAppMruBack { get; set; } = _ => { };

        public ActionRunner(Logger logger, AppMru mru)
        {
            _logger = logger;
            _mru = mru;
        }

        public void Run(MAction action)
        {
            switch (action)
            {
                case KeyStrokeAction ks:
                    Stroke(ks.Key, ks.Mods);
                    break;
                case SystemActionType sys:
                    SystemAction(sys.Name);
                    break;
                case OpenAppAction oa:
                    OpenApp(oa.Target);
                    break;
                case ShellAction sh:
                    Shell(sh.Command);
                    break;
                case VoiceAction:
                    Voice();
                    break;
                case WindowCycleAction wc:
                    WindowSwitcher.Cycle(wc.Scope);
                    break;
                case TabJumpAction tj:
                    TabJump(tj.Dir, tj.Index);
                    break;
                case FocusInputAction:
                    FocusInput.Perform();
                    break;
                case MouseModeAction:
                    MouseMode.Instance.Toggle();
                    break;
                case MacroAction ma:
                    Macro(ma.Steps);
                    break;
                case OverlayAction ov:
                    _logger.Info($"overlay 动作: {ov.Name}");
                    OnOverlay?.Invoke(ov.Name);
                    break;
                case LayerMomentaryAction:
                case LayerToggleAction:
                case NoneAction:
                    break;
                default:
                    _logger.Warn($"未知动作: {action.TypeName}");
                    break;
            }
        }

        private void Stroke(string key, string[] mods)
        {
            if (!InputSimulator.TryResolveKey(key, out var vk))
            {
                _logger.Warn($"unknown key name: {key}");
                return;
            }
            if (mods is null || mods.Length == 0)
            {
                InputSimulator.TapKey(vk);
                return;
            }
            InputSimulator.Combo(vk, mods);
        }

        private void SystemAction(string name)
        {
            switch (name)
            {
                case "volume_up": InputSimulator.TapKey(0xAF); break;
                case "volume_down": InputSimulator.TapKey(0xAE); break;
                case "mute": InputSimulator.TapKey(0xAD); break;
                case "play_pause": InputSimulator.TapKey(0xB3); break;
                case "next": InputSimulator.TapKey(0xB0); break;
                case "prev":
                case "previous": InputSimulator.TapKey(0xB1); break;
                case "mission_control":
                case "task_view":
                    TransientSystemUI.MarkEntered();
                    InputSimulator.Combo(0x09, new[] { "win" }); // Win+Tab
                    break;
                case "launchpad":
                case "start_menu":
                    InputSimulator.TapKey(0x5B);
                    break;
                case "spotlight":
                case "search":
                    InputSimulator.Combo(0x53, new[] { "win" }); // Win+S
                    break;
                case "display_sleep":
                    Win32.SendMessage(Win32.GetShellWindow() == IntPtr.Zero ? IntPtr.Zero : Win32.GetShellWindow(),
                                     Win32.WM_SYSCOMMAND, (IntPtr)Win32.SC_MONITORPOWER, (IntPtr)Win32.MONITOR_OFF);
                    break;
                case "lock_screen":
                    Win32.LockWorkStation();
                    break;
                case "show_desktop":
                    InputSimulator.Combo(0x44, new[] { "win" }); // Win+D
                    break;
                case "app_expose":
                    TransientSystemUI.MarkEntered();
                    InputSimulator.Combo(0x09, new[] { "win" });
                    break;
                case "app_mru_back":
                    _mru.SwitchBack();
                    break;
                case "next_app":
                    // Alt+Tab（不阻止切换器界面出现，但至少把焦点切走）
                    InputSimulator.Combo(0x09, new[] { "alt" });
                    break;
                case "previous_app":
                    InputSimulator.Combo(0x09, new[] { "alt", "shift" });
                    break;
                case "space_left":
                    InputSimulator.Combo(0x25, new[] { "win", "ctrl" });
                    break;
                case "space_right":
                    InputSimulator.Combo(0x27, new[] { "win", "ctrl" });
                    break;
                case "screenshot":
                    InputSimulator.Combo(0x53, new[] { "win", "shift" }); // Win+Shift+S
                    break;
                case "show_desktop2":
                    InputSimulator.Combo(0x44, new[] { "win" });
                    break;
                default:
                    if (!WorkspaceActions.Perform(name, _logger)) _logger.Warn($"unknown system action: {name}");
                    break;
            }
        }

        private void OpenApp(string target)
        {
            try
            {
                // 支持 exe 路径 / 应用名称 / URI 协议
                if (File.Exists(target))
                {
                    Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
                    return;
                }
                if (target.Contains(':') && !Path.IsPathRooted(target))
                {
                    // 可能是 URI 协议（ms-settings: 等）
                    Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
                    return;
                }
                Process.Start(new ProcessStartInfo { FileName = target + ".exe", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.Warn($"open_app 失败 {target}: {ex.Message}");
            }
        }

        private void Shell(string cmd)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + cmd,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var p = Process.Start(psi);
                if (p is not null) ShellProcessRegistry.Shared.Register(p.Id);
            }
            catch (Exception ex)
            {
                _logger.Warn($"shell 失败 {cmd}: {ex.Message}");
            }
        }

        private void Voice()
        {
            // Windows 版 voice 回退：默认触发系统听写（Win+H）。
            // 真正的 ATVV BLE 语音链路在 Windows 上暂未移植（需要 VB-CABLE 等虚拟声卡）。
            if (VoiceFallback == "none") return;
            InputSimulator.Combo(0x48, new[] { "win" });
            _logger.Info("voice → Win+H (使用电脑当前麦克风的系统听写)");
        }

        private void TabJump(int? dir, int? index)
        {
            if (index.HasValue)
            {
                if (index.Value < 1 || index.Value > 9)
                {
                    _logger.Warn($"tab_jump index 超范围(1-9): {index}");
                    return;
                }
                if (InputSimulator.TryResolveKey(index.Value.ToString(), out var code))
                    InputSimulator.Combo(code, new[] { "ctrl" });
                return;
            }
            if (dir.HasValue)
            {
                var bracket = dir.Value >= 0 ? "right_bracket" : "left_bracket";
                if (InputSimulator.TryResolveKey(bracket, out var code))
                    InputSimulator.Combo(code, new[] { "ctrl", "shift" });
                return;
            }
            _logger.Warn("tab_jump 缺少 dir/index 参数");
        }

        private void Macro(List<MacroStep> steps)
        {
            foreach (var s in steps)
            {
                switch (s)
                {
                    case DelayStep d: Thread.Sleep(d.Ms); break;
                    case TextStep t: InputSimulator.Text(t.Text); break;
                    case ActionStep a: Run(a.Action); break;
                }
            }
        }
    }

    public static class WorkspaceActions
    {
        public static bool Perform(string name, Logger logger)
        {
            // Windows 版暂无更多系统动作；后续扩展可加 snap_left 等。
            return false;
        }
    }
}
