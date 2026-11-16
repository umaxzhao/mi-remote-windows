using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiRemote.Core;

namespace MiRemote.App
{
    public partial class MainWindow : Window
    {
        private readonly AppServices _svc;
        private string _selectedKey = "up";

        private enum RecSlot { None, Tap, Hold, Double }
        private RecSlot _recording = RecSlot.None;
        private IntPtr _recHook = IntPtr.Zero;
        private Win32.HookProc? _recHookProc;

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        public MainWindow(AppServices svc)
        {
            InitializeComponent();
            _svc = svc;

            PauseCheck.IsChecked = _svc.Engine.IsSuspended;
            PauseCheck.Checked += (_, _) => _svc.ToggleSuspended();
            PauseCheck.Unchecked += (_, _) => _svc.ToggleSuspended();

            StartEnabledCheck.IsChecked = _svc.Config.Settings.StartEnabled;

            HoldMsBox.Text = _svc.Config.Settings.HoldMs.ToString();
            DoubleMsBox.Text = _svc.Config.Settings.DoubleMs.ToString();
            RepeatDelayBox.Text = _svc.Config.Settings.RepeatDelayMs.ToString();

            foreach (var k in RemoteKeys.All)
                KeyList.Items.Add(k.RawName() + "  " + k.DisplayName());
            KeyList.SelectedIndex = 0;
            KeyList.SelectionChanged += KeyList_SelectionChanged;

            foreach (var p in Presets.All)
                PresetList.Items.Add(p.DisplayName + "  [" + (p.BundleId ?? "global") + "]");
            ConfigPathText.Text = "配置路径: " + _svc.ConfigStore.ConfigPath;

            _svc.Logger.Line += line =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogTail.AppendText(line + Environment.NewLine);
                    LogTail.ScrollToEnd();
                    FullLog.AppendText(line + Environment.NewLine);
                    FullLog.ScrollToEnd();
                }));
            };

            NavList.SelectedIndex = 0;
        }

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavList.SelectedItem is not ListBoxItem item) return;
            var tag = item.Tag as string ?? "";
            PageMapping.Visibility = tag == "mapping" ? Visibility.Visible : Visibility.Collapsed;
            PageProfile.Visibility = tag == "profile" ? Visibility.Visible : Visibility.Collapsed;
            PageVoice.Visibility = tag == "voice" ? Visibility.Visible : Visibility.Collapsed;
            PageGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
            PageLog.Visibility = tag == "log" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RemoteKey_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var tag = fe.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;
            var idx = -1;
            for (int i = 0; i < RemoteKeys.All.Length; i++)
                if (RemoteKeys.All[i].RawName() == tag) { idx = i; break; }
            if (idx >= 0) KeyList.SelectedIndex = idx;
        }

        private void KeyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (KeyList.SelectedItem is null) return;
            var raw = KeyList.SelectedItem.ToString()!.Split(new[] { ' ', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0];
            _selectedKey = raw;
            CurrentKeyLabel.Text = raw;
            var kb = _svc.Config.Profiles.TryGetValue("global", out var g) && g.TryGetValue(raw, out var b) ? b : null;
            TapBox.Text = kb?.Tap?.ToJson() ?? "";
            HoldBox.Text = kb?.Hold?.ToJson() ?? "";
            DoubleBox.Text = kb?.Double?.ToJson() ?? "";
        }

        private void RecTap_Click(object sender, RoutedEventArgs e) => StartRecording(RecSlot.Tap);
        private void RecHold_Click(object sender, RoutedEventArgs e) => StartRecording(RecSlot.Hold);
        private void RecDouble_Click(object sender, RoutedEventArgs e) => StartRecording(RecSlot.Double);

        private void StartRecording(RecSlot slot)
        {
            _recording = slot;
            InstallRecHook();
            RecStatus.Text = "请按下快捷键…（Esc 取消）";
            RecTapBtn.Content = slot == RecSlot.Tap ? "● 录制中…" : "⏺ 录制";
            RecHoldBtn.Content = slot == RecSlot.Hold ? "● 录制中…" : "⏺ 录制";
            RecDoubleBtn.Content = slot == RecSlot.Double ? "● 录制中…" : "⏺ 录制";
        }

        private void InstallRecHook()
        {
            _recHookProc = RecHookCallback;
            _recHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _recHookProc, Win32.GetModuleHandle(null), 0);
            if (_recHook == IntPtr.Zero)
                _svc.Logger.Info("[REC] SetWindowsHookEx 失败: " + Marshal.GetLastWin32Error());
            else
                _svc.Logger.Info("[REC] 低级键盘钩子已安装");
        }

        private void UninstallRecHook()
        {
            if (_recHook != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_recHook);
                _recHook = IntPtr.Zero;
            }
            _recHookProc = null;
        }

        private IntPtr RecHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == Win32.HC_ACTION && (wParam == (IntPtr)Win32.WM_KEYDOWN || wParam == (IntPtr)Win32.WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)kb.vkCode;

                if (vk == 0x1B) // Escape 取消
                {
                    Dispatcher.BeginInvoke(new Action(CancelRecording));
                    UninstallRecHook();
                    return (IntPtr)1;
                }

                // 纯修饰键：不结束录制，放行
                if (vk is 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C
                    or 0x10 or 0x11 or 0x12)
                {
                    return Win32.CallNextHookEx(_recHook, nCode, wParam, lParam);
                }

                var wpfKey = KeyInterop.KeyFromVirtualKey(vk);
                var name = KeyToName(wpfKey);
                if (name is null)
                {
                    Dispatcher.BeginInvoke(new Action(() => { RecStatus.Text = "未识别的键: " + wpfKey; }));
                    return (IntPtr)1;
                }

                var mods = BuildMods();
                var action = new { type = "key_stroke", key = name, mods = mods.ToArray() };
                var json = System.Text.Json.JsonSerializer.Serialize(action);
                var slot = _recording;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    switch (slot)
                    {
                        case RecSlot.Tap: TapBox.Text = json; break;
                        case RecSlot.Hold: HoldBox.Text = json; break;
                        case RecSlot.Double: DoubleBox.Text = json; break;
                    }
                    StopRecording();
                }));
                UninstallRecHook();
                return (IntPtr)1; // 吞掉该按键，避免泄漏到前台
            }
            return Win32.CallNextHookEx(_recHook, nCode, wParam, lParam);
        }

        private void StopRecording()
        {
            UninstallRecHook();
            _recording = RecSlot.None;
            RecStatus.Text = "已录制";
            RecTapBtn.Content = "⏺ 录制";
            RecHoldBtn.Content = "⏺ 录制";
            RecDoubleBtn.Content = "⏺ 录制";
        }

        private void CancelRecording()
        {
            UninstallRecHook();
            _recording = RecSlot.None;
            RecStatus.Text = "已取消";
            RecTapBtn.Content = "⏺ 录制";
            RecHoldBtn.Content = "⏺ 录制";
            RecDoubleBtn.Content = "⏺ 录制";
        }

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (_recording == RecSlot.None) { base.OnPreviewKeyDown(e); return; }
            _svc.Logger.Info($"[REC] PreviewKeyDown key={e.Key} recording={_recording}");
            if (e.Key == Key.Escape) { CancelRecording(); e.Handled = true; return; }

            // 纯修饰键按下时不结束录制
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            {
                e.Handled = true;
                return;
            }

            var name = KeyToName(e.Key);
            if (name is null)
            {
                RecStatus.Text = "未识别的键: " + e.Key;
                _svc.Logger.Info($"[REC] 未识别的键: {e.Key}");
                e.Handled = true;
                return;
            }

            var mods = BuildMods();
            var action = new { type = "key_stroke", key = name, mods = mods.ToArray() };
            var json = System.Text.Json.JsonSerializer.Serialize(action);

            switch (_recording)
            {
                case RecSlot.Tap: TapBox.Text = json; break;
                case RecSlot.Hold: HoldBox.Text = json; break;
                case RecSlot.Double: DoubleBox.Text = json; break;
            }
            StopRecording();
            e.Handled = true;
        }

        private System.Collections.Generic.List<string> BuildMods()
        {
            var mods = new System.Collections.Generic.List<string>();
            if (IsKeyDown(0xA2)) mods.Add("left_ctrl");
            else if (IsKeyDown(0xA3)) mods.Add("right_ctrl");
            if (IsKeyDown(0xA0)) mods.Add("left_shift");
            else if (IsKeyDown(0xA1)) mods.Add("right_shift");
            if (IsKeyDown(0xA4)) mods.Add("left_alt");
            else if (IsKeyDown(0xA5)) mods.Add("right_alt");
            if (IsKeyDown(0x5B)) mods.Add("left_win");
            else if (IsKeyDown(0x5C)) mods.Add("right_win");
            // 兜底：Keyboard.Modifiers 有但具体左右未识别时
            if (mods.Count == 0)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) mods.Add("left_ctrl");
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) mods.Add("left_shift");
                if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt) mods.Add("left_alt");
            }
            return mods;
        }

        private static string? KeyToName(Key key)
        {
            if (key >= Key.A && key <= Key.Z) return key.ToString().ToLowerInvariant();
            if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
            if (key >= Key.F1 && key <= Key.F24) return "f" + (key - Key.F1 + 1);
            return key switch
            {
                Key.Return => "return",
                Key.Tab => "tab",
                Key.Space => "space",
                Key.Back => "delete",
                Key.Delete => "forward_delete",
                Key.Escape => null,
                Key.Up => "up_arrow",
                Key.Down => "down_arrow",
                Key.Left => "left_arrow",
                Key.Right => "right_arrow",
                Key.Home => "home",
                Key.End => "end",
                Key.PageUp => "page_up",
                Key.PageDown => "page_down",
                Key.Insert => "insert",
                Key.PrintScreen => "print_screen",
                Key.OemOpenBrackets => "left_bracket",
                Key.OemCloseBrackets => "right_bracket",
                Key.OemMinus => "minus",
                Key.OemPlus => "equal",
                Key.OemComma => "comma",
                Key.OemPeriod => "period",
                Key.OemQuestion => "slash",
                Key.OemSemicolon => "semicolon",
                Key.OemQuotes => "quote",
                Key.OemBackslash => "backslash",
                Key.OemTilde => "grave",
                _ => null,
            };
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(HoldMsBox.Text, out var h)) _svc.Config.Settings.HoldMs = h;
            if (int.TryParse(DoubleMsBox.Text, out var d)) _svc.Config.Settings.DoubleMs = d;
            if (int.TryParse(RepeatDelayBox.Text, out var r)) _svc.Config.Settings.RepeatDelayMs = r;
            _svc.Config.Settings.StartEnabled = StartEnabledCheck.IsChecked == true;
            _svc.Config.Settings.VoiceFallback = VoiceFallbackBox.SelectedIndex == 0 ? "dictation" : "none";
            _svc.ApplyConfig(_svc.Config);
            System.Windows.MessageBox.Show("已保存", "MiRemote");
        }

        private void SaveKey_Click(object sender, RoutedEventArgs e)
        {
            if (!_svc.Config.Profiles.TryGetValue("global", out var g))
            {
                g = new System.Collections.Generic.Dictionary<string, MiRemote.Core.KeyBinding>();
                _svc.Config.Profiles["global"] = g;
            }
            var kb = g.TryGetValue(_selectedKey, out var existing) ? existing : new MiRemote.Core.KeyBinding();
            kb.Tap = ParseAction(TapBox.Text);
            kb.Hold = ParseAction(HoldBox.Text);
            kb.Double = ParseAction(DoubleBox.Text);
            g[_selectedKey] = kb;
            _svc.ApplyConfig(_svc.Config);
        }

        private static MAction? ParseAction(string text)
        {
            text = text.Trim();
            if (string.IsNullOrEmpty(text)) return null;
            try { return ConfigJson.DeserializeAction(text); } catch { return null; }
        }

        private void ApplyPreset_Click(object sender, RoutedEventArgs e) => ApplyPreset(false);
        private void ApplyPresetForce_Click(object sender, RoutedEventArgs e) => ApplyPreset(true);

        private void ApplyPreset(bool force)
        {
            int idx = PresetList.SelectedIndex;
            if (idx < 0 || idx >= Presets.All.Count) return;
            var preset = Presets.All[idx];
            var cfg = _svc.Config;
            Presets.Apply(preset, ref cfg, force);
            _svc.ApplyConfig(cfg);
        }

        private void ExportConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "config.json", Filter = "JSON|*.json" };
                if (dlg.ShowDialog() == true)
                {
                    File.WriteAllText(dlg.FileName, ConfigJson.Serialize(_svc.Config));
                    System.Windows.MessageBox.Show("已导出", "MiRemote");
                }
            }
            catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message); }
        }

        private void ReloadConfig_Click(object sender, RoutedEventArgs e)
        {
            var cfg = _svc.ConfigStore.Load();
            _svc.ApplyConfig(cfg);
            HoldMsBox.Text = cfg.Settings.HoldMs.ToString();
            DoubleMsBox.Text = cfg.Settings.DoubleMs.ToString();
            RepeatDelayBox.Text = cfg.Settings.RepeatDelayMs.ToString();
            StartEnabledCheck.IsChecked = cfg.Settings.StartEnabled;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            UninstallRecHook();
            e.Cancel = true;
            Hide();
        }
    }
}
