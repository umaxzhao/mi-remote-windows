using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MiRemote.Core;

namespace MiRemote.App
{
    public partial class MainWindow : Window
    {
        private readonly AppServices _svc;
        private string _selectedKey = "up";

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
                g = new System.Collections.Generic.Dictionary<string, KeyBinding>();
                _svc.Config.Profiles["global"] = g;
            }
            var kb = g.TryGetValue(_selectedKey, out var existing) ? existing : new KeyBinding();
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
            e.Cancel = true;
            Hide();
        }
    }
}
