using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MiRemote.Core;
using RemoteBinding = MiRemote.Core.KeyBinding;
using MessageBox = System.Windows.MessageBox;

namespace MiRemote.App;

public partial class MainWindow : Window
{
    private readonly AppServices _svc;
    private readonly RemoteDiagram _remote = new();
    private readonly List<SlotDefinition> _slots = new();
    private RemoteKey _selectedKey = RemoteKey.Ok;
    private string _selectedProfile = "global";
    private bool _loading;

    private sealed record SlotDefinition(string Id, string Title, string Detail, string Icon, string? Gesture = null, int? Layer = null)
    {
        public ActionSlotRow? Row { get; set; }
    }

    public MainWindow(AppServices svc)
    {
        InitializeComponent();
        _svc = svc;
        RemoteHost.Content = _remote;
        _remote.Selected += SelectKey;
        _remote.Select(_selectedKey);
        _svc.RawInput.ButtonPressed += RemoteButtonPressed;
        Closed += (_, _) => _svc.RawInput.ButtonPressed -= RemoteButtonPressed;
        NavList.SelectedIndex = 0;
        PauseCheck.IsChecked = _svc.Engine.IsSuspended;
        PauseCheck.Checked += (_, _) => _svc.Engine.SetSuspended(true);
        PauseCheck.Unchecked += (_, _) => _svc.Engine.SetSuspended(false);
        RefreshProfiles();
        RefreshSettings();
        foreach (var p in Presets.All) PresetList.Items.Add(p.DisplayName + "  [" + (p.BundleId ?? "global") + "]");
        ConfigPathText.Text = "配置文件：" + _svc.ConfigStore.ConfigPath;
        SelectKey(RemoteKey.Ok);
    }

    private void RemoteButtonPressed(ButtonEvent e)
    {
        if (!e.IsDown) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RemoteStatus.Text = "已收到遥控器按键 · " + e.Key.DisplayName();
            DeviceStatus.Text = "已收到遥控器输入。此状态仅表示本次运行期间收到过按键事件。";
        }));
    }

    private void RefreshSettings()
    {
        _loading = true;
        StartEnabledCheck.IsChecked = _svc.Config.Settings.StartEnabled;
        HoldMsBox.Text = _svc.Config.Settings.HoldMs.ToString();
        DoubleMsBox.Text = _svc.Config.Settings.DoubleMs.ToString();
        RepeatDelayBox.Text = _svc.Config.Settings.RepeatDelayMs.ToString();
        VoiceFallbackBox.SelectedIndex = _svc.Config.Settings.VoiceFallback switch { "none" => 2, "dictation" => 1, _ => 0 };
        VoiceKeyBox.Text = _svc.Config.Settings.VoiceTriggerKey;
        VoiceModeBox.SelectedIndex = _svc.Config.Settings.VoiceTriggerMode == "tap" ? 1 : 0;
        VoiceDriverStatus.Text = _svc.Runner.VoiceDriverStatus + "。豆包会过滤普通模拟按键，建议安装驱动。";
        RemoteStatus.Text = "等待遥控器连接";
        DeviceStatus.Text = "等待遥控器连接。这里只显示实际收到的输入，不表示已检测设备连接。";
        _loading = false;
    }

    private void RefreshProfiles()
    {
        _loading = true;
        var names = _svc.Config.Profiles.Keys.Where(k => k != "global").OrderBy(k => k).ToList();
        ProfileSelector.Items.Clear();
        ProfileSelector.Items.Add("global（所有应用）");
        foreach (var name in names) ProfileSelector.Items.Add(name);
        if (_selectedProfile != "global" && !_svc.Config.Profiles.ContainsKey(_selectedProfile)) _selectedProfile = "global";
        ProfileSelector.SelectedIndex = _selectedProfile == "global" ? 0 : names.IndexOf(_selectedProfile) + 1;
        _loading = false;
    }

    private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _selectedProfile = ProfileSelector.SelectedIndex <= 0 ? "global" : ProfileSelector.SelectedItem?.ToString() ?? "global";
        RebuildRows();
    }

    private void SelectKey(RemoteKey key)
    {
        _selectedKey = key;
        _remote.Select(key);
        CurrentKeyLabel.Text = key == RemoteKey.Ok ? "确认键（方向环中心）" : key.DisplayName();
        KeyBadge.Text = key == RemoteKey.Ok ? "OK" : key.DisplayName();
        var usage = RemoteKeys.UsageMap.FirstOrDefault(x => x.Value == key).Key;
        if (usage != 0) UsageLabel.Text = $"HID usage 0x{usage:X2}";
        else
        {
            var consumerUsage = RemoteKeys.ConsumerUsageMap.FirstOrDefault(x => x.Value == key).Key;
            UsageLabel.Text = consumerUsage == 0 ? "HID usage：未知" : $"HID usage 0x{consumerUsage:X2} · 消费者页";
        }
        RebuildRows();
    }

    private void RebuildRows()
    {
        _slots.Clear();
        TriggerRows.Children.Clear(); GestureRows.Children.Clear(); LayerRows.Children.Clear();
        GestureSection.Visibility = _selectedKey == RemoteKey.Ok ? Visibility.Visible : Visibility.Collapsed;
        Add(TriggerRows, new SlotDefinition("tap", "短按", "松开时立即触发", "✓"));
        Add(TriggerRows, new SlotDefinition("hold", "长按", $"超过 {_svc.Config.Settings.HoldMs} ms 触发", "◷"));
        Add(TriggerRows, new SlotDefinition("double", "双击", $"{_svc.Config.Settings.DoubleMs} ms 窗口内连接两次", "⋯"));
        if (_selectedKey == RemoteKey.Ok)
            foreach (var d in new[] { ("up", "上", "↑"), ("down", "下", "↓"), ("left", "左", "←"), ("right", "右", "→") })
                Add(GestureRows, new SlotDefinition("gesture_" + d.Item1, "OK + " + d.Item2, "按住确认键时触发", d.Item3, d.Item1));
        for (int layer = 1; layer <= 3; layer++)
            Add(LayerRows, new SlotDefinition("layer_" + layer, ActionLabels.Mode(layer), "功能模式 " + layer + " · 替代短按", "◈", Layer: layer));
    }

    private void Add(System.Windows.Controls.Panel panel, SlotDefinition slot)
    {
        var row = new ActionSlotRow(slot.Id, slot.Title, slot.Detail, slot.Icon);
        row.Clicked += (_, _) => OpenActionMenu(slot);
        slot.Row = row;
        _slots.Add(slot);
        panel.Children.Add(row);
        UpdateRow(slot);
    }

    private void UpdateRow(SlotDefinition slot)
    {
        var action = ReadSlot(slot, out bool inherited);
        slot.Row?.SetAction(inherited ? "继承全局 · " + ActionLabels.Describe(action) : ActionLabels.Describe(action));
    }

    private MAction? ReadSlot(SlotDefinition slot, out bool inherited)
    {
        inherited = false;
        RemoteBinding? binding = null;
        if (_selectedProfile != "global" && _svc.Config.Profiles.TryGetValue(_selectedProfile, out var overlay))
            overlay.TryGetValue(_selectedKey.RawName(), out binding);
        if (binding != null && TryReadRaw(binding, slot, out var own)) return own;
        if (_selectedProfile != "global") inherited = true;
        if (_svc.Config.Profiles.TryGetValue("global", out var global)) global.TryGetValue(_selectedKey.RawName(), out binding); else binding = null;
        return binding != null && TryReadRaw(binding, slot, out var action) ? action : null;
    }

    private static bool TryReadRaw(RemoteBinding binding, SlotDefinition slot, out MAction? action)
    {
        action = null;
        if (slot.Gesture != null) return binding.Gesture != null && binding.Gesture.TryGetValue(slot.Gesture, out action);
        if (slot.Layer.HasValue) return binding.Layers != null && binding.Layers.TryGetValue(slot.Layer.Value.ToString(), out action);
        action = slot.Id switch { "tap" => binding.Tap, "hold" => binding.Hold, "double" => binding.Double, _ => null };
        // Missing/null overlay slots inherit; explicit NoneAction still overrides global.
        return action != null;
    }

    private void OpenActionMenu(SlotDefinition slot)
    {
        var current = ReadSlot(slot, out _);
        ActionMenus.Open(slot.Row!.Selector, this, _selectedProfile == "global", action => SetAction(slot, action), () => SetAction(slot, null), () => RecordShortcut(slot), () => EditJson(slot, current));
    }

    private void RecordShortcut(SlotDefinition slot)
    {
        var wasSuspended = _svc.Engine.IsSuspended;
        _svc.Engine.SetSuspended(true);
        try
        {
            var dialog = new ShortcutRecorderWindow { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Result is not null) SetAction(slot, dialog.Result);
        }
        finally { _svc.Engine.SetSuspended(wasSuspended); }
    }

    private void EditJson(SlotDefinition slot, MAction? current)
    {
        var text = ActionInputDialog.Ask(this, "高级动作 JSON", "支持的动作 JSON。无效 JSON 不会保存。", current?.ToJson() ?? "", multiline: true);
        if (text == null) return;
        try { SetAction(slot, ConfigJson.DeserializeAction(text)); }
        catch (Exception ex) { MessageBox.Show(this, "动作无效：" + ex.Message, "MiRemote", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void SetAction(SlotDefinition slot, MAction? action)
    {
        if (!_svc.Config.Profiles.TryGetValue(_selectedProfile, out var profile))
        {
            profile = new Dictionary<string, RemoteBinding>();
            _svc.Config.Profiles[_selectedProfile] = profile;
        }
        profile.TryGetValue(_selectedKey.RawName(), out var binding);
        if (action == null && _selectedProfile != "global" && binding == null) { UpdateRow(slot); return; }
        binding ??= new RemoteBinding();
        if (slot.Gesture != null)
        {
            if (action == null) { binding.Gesture?.Remove(slot.Gesture); if (binding.Gesture?.Count == 0) binding.Gesture = null; }
            else (binding.Gesture ??= new Dictionary<string, MAction>())[slot.Gesture] = action;
        }
        else if (slot.Layer.HasValue)
        {
            if (action == null) { binding.Layers?.Remove(slot.Layer.Value.ToString()); if (binding.Layers?.Count == 0) binding.Layers = null; }
            else (binding.Layers ??= new Dictionary<string, MAction>())[slot.Layer.Value.ToString()] = action;
        }
        else if (slot.Id == "tap") binding.Tap = action;
        else if (slot.Id == "hold") binding.Hold = action;
        else binding.Double = action;
        if (_selectedProfile != "global" && binding.Tap == null && binding.Hold == null && binding.Double == null && binding.Gesture == null && binding.Layers == null)
            profile.Remove(_selectedKey.RawName());
        else profile[_selectedKey.RawName()] = binding;
        SaveChanges($"{slot.Title} · {ActionLabels.Describe(action)}");
    }

    private void SaveChanges(string message)
    {
        try
        {
            _svc.ApplyConfig(_svc.Config);
            SaveStatus.Text = message + " · 已自动保存";
            foreach (var slot in _slots) UpdateRow(slot);
        }
        catch (Exception ex) { SaveStatus.Text = "保存失败：" + ex.Message; MessageBox.Show(this, "保存失败：" + ex.Message, "MiRemote"); }
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem item) return;
        var tag = item.Tag as string;
        PageMapping.Visibility = tag == "mapping" ? Visibility.Visible : Visibility.Collapsed;
        PageProfile.Visibility = tag == "profile" ? Visibility.Visible : Visibility.Collapsed;
        PageVoice.Visibility = tag == "voice" ? Visibility.Visible : Visibility.Collapsed;
        PageGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ActionInputDialog.Ask(this, "添加应用 Profile", "输入进程名（例如 code，不包含 .exe）。仅修改过的槽位会覆盖全局设置。");
        if (name == null) return;
        name = Path.GetFileNameWithoutExtension(name.Trim()).ToLowerInvariant();
        if (name == "global" || string.IsNullOrWhiteSpace(name) || name.Any(c => char.IsWhiteSpace(c) || c == '/' || c == '\\')) { MessageBox.Show(this, "请输入有效的进程名。", "MiRemote"); return; }
        if (!_svc.Config.Profiles.ContainsKey(name)) { _svc.Config.Profiles[name] = new Dictionary<string, RemoteBinding>(); _svc.ApplyConfig(_svc.Config); }
        _selectedProfile = name; RefreshProfiles(); RebuildRows();
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e) => ApplyPreset(false);
    private void ApplyPresetForce_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "强制覆盖会替换预设涉及的槽位，确定继续？", "应用预设", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK) ApplyPreset(true);
    }
    private void ApplyPreset(bool force)
    {
        int idx = PresetList.SelectedIndex;
        if (idx < 0 || idx >= Presets.All.Count) { MessageBox.Show(this, "请先选择一个预设。", "MiRemote"); return; }
        var cfg = _svc.Config; Presets.Apply(Presets.All[idx], ref cfg, force); _svc.ApplyConfig(cfg); RefreshProfiles(); RebuildRows();
    }
    private void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "config.json", Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        try { File.WriteAllText(dlg.FileName, ConfigJson.Serialize(_svc.Config)); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败"); }
    }
    private void ReloadConfig_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "放弃尚未保存的通用设置并从磁盘重新加载？", "重新加载", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        try { _svc.ApplyConfig(_svc.ConfigStore.Load()); RefreshSettings(); RefreshProfiles(); RebuildRows(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "重新加载失败"); }
    }
    private void InstallDriver_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var script = Path.Combine(AppContext.BaseDirectory, "scripts", "install-driver.ps1");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
                Verb = "runas",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(this, "驱动安装已取消或失败：" + ex.Message, "MiRemote"); }
    }

    private void SaveVoice_Click(object sender, RoutedEventArgs e)
    {
        _svc.Config.Settings.VoiceFallback = VoiceFallbackBox.SelectedIndex switch { 1 => "dictation", 2 => "none", _ => "doubao" };
        var key = VoiceKeyBox.Text.Trim();
        _svc.Config.Settings.VoiceTriggerKey = string.IsNullOrEmpty(key) ? "right_alt" : key;
        _svc.Config.Settings.VoiceTriggerMode = VoiceModeBox.SelectedIndex == 1 ? "tap" : "hold";
        _svc.ApplyConfig(_svc.Config);
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(HoldMsBox.Text, out int h) || h < 50 || !int.TryParse(DoubleMsBox.Text, out int d) || d < 50 || !int.TryParse(RepeatDelayBox.Text, out int r) || r < -1) { MessageBox.Show(this, "阈值必须是有效整数，长按和双击阈值至少 50 ms。", "MiRemote"); return; }
        _svc.Config.Settings.HoldMs = h; _svc.Config.Settings.DoubleMs = d; _svc.Config.Settings.RepeatDelayMs = r; _svc.Config.Settings.StartEnabled = StartEnabledCheck.IsChecked == true; _svc.ApplyConfig(_svc.Config); RebuildRows();
    }
    protected override void OnClosing(CancelEventArgs e) { e.Cancel = true; Hide(); }
}
