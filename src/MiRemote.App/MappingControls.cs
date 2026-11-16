using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using MiRemote.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Geometry = System.Windows.Media.Geometry;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Thickness = System.Windows.Thickness;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Cursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace MiRemote.App;

public static class ActionLabels
{
    public static string Mode(int n) => n switch { 1 => "快捷控制模式", 2 => "App 控制模式", 3 => "App 导航模式", _ => $"自定义模式 {n}" };
    public static string Key(KeyStrokeAction k) => string.Join(" + ", k.Mods.Select(Mod).Concat(new[] { Main(k.Key) }));
    private static string Mod(string m) => m switch { "left_ctrl" => "Ctrl（左）", "right_ctrl" => "Ctrl（右）", "ctrl" or "control" or "left_cmd" or "right_cmd" => "Ctrl", "left_shift" => "Shift（左）", "right_shift" => "Shift（右）", "shift" => "Shift", "left_alt" => "Alt（左）", "right_alt" => "Alt（右）", "alt" or "option" or "left_option" or "right_option" => "Alt", "left_win" or "left_windows" => "Win（左）", "right_win" or "right_windows" => "Win（右）", "win" => "Win", _ => m };
    private static string Main(string k) => k switch { "return" => "Return ↵", "escape" => "Esc", "space" => "Space", "delete" => "Backspace", "forward_delete" => "Delete", "up_arrow" => "↑", "down_arrow" => "↓", "left_arrow" => "←", "right_arrow" => "→", "page_up" => "Page Up", "page_down" => "Page Down", _ when k.StartsWith('f') && k.Length > 1 => k.ToUpperInvariant(), _ => k };
    public static string Describe(MAction? a) => a switch
    {
        null or NoneAction => "无动作", KeyStrokeAction k => "发送按键 · " + Key(k), SystemActionType s => SystemName(s.Name), OpenAppAction o => "打开应用 · " + o.Target, ShellAction s => "Shell · " + s.Command, VoiceAction => "语音输入", LayerMomentaryAction l => "按住进入" + Mode(l.Layer), LayerToggleAction l => "开关" + Mode(l.Layer), WindowCycleAction w => w.Scope == "global" ? "窗口循环（全局）" : "窗口循环（同应用）", TabJumpAction t => t.Index.HasValue ? $"跳到第 {t.Index} 个标签页" : t.Dir >= 0 ? "下一个标签页" : "上一个标签页", FocusInputAction => "聚焦输入框", MouseModeAction => "鼠标模式开关", MacroAction m => "文本宏 · " + (m.Steps.OfType<TextStep>().FirstOrDefault()?.Text ?? $"{m.Steps.Count} 步"), OverlayAction o => o.Name switch { "window_picker" => "窗口选择器浮层", "system_menu" => "系统功能菜单浮层", "app_wheel" => "App 轮盘", "tutorial" => "按键教程浮层", _ => "浮层 · " + o.Name }, _ => a.Describe()
    };
    private static string SystemName(string n) => n switch { "volume_up" => "音量增大", "volume_down" => "音量减小", "mute" => "静音", "play_pause" => "播放 / 暂停", "next" => "下一首", "prev" or "previous" => "上一首", "task_view" or "mission_control" => "任务视图", "start_menu" or "launchpad" => "开始菜单", "search" or "spotlight" => "Windows 搜索", "lock_screen" => "锁定屏幕", "show_desktop" => "显示桌面", "app_mru_back" => "回到上一个 App", "next_app" => "下一个 App", "previous_app" => "上一个 App", "space_left" => "左侧虚拟桌面", "space_right" => "右侧虚拟桌面", "screenshot" => "截图", "display_sleep" => "关闭显示器", _ => "系统功能 · " + n };
}

public sealed class ActionSlotRow : Border
{
    private readonly TextBlock _action = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    public Button Selector { get; } = new();
    public event EventHandler? Clicked;
    public ActionSlotRow(string id, string title, string detail, string icon)
    {
        Padding = new Thickness(14, 10, 14, 10); BorderBrush = new SolidColorBrush(Color.FromRgb(234, 234, 237)); BorderThickness = new Thickness(0, 0, 0, 1);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = icon, FontSize = 19, Foreground = new SolidColorBrush(Color.FromRgb(75, 155, 171)), VerticalAlignment = VerticalAlignment.Center });
        var labels = new StackPanel(); labels.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold }); labels.Children.Add(new TextBlock { Text = detail, Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) }); Grid.SetColumn(labels, 1); grid.Children.Add(labels);
        var content = new DockPanel(); var arrow = new TextBlock { Text = "⌄", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(arrow, Dock.Right); content.Children.Add(arrow); content.Children.Add(_action);
        Selector.Content = content; Selector.HorizontalContentAlignment = HorizontalAlignment.Right; Selector.Background = Brushes.Transparent; Selector.BorderThickness = new Thickness(0); Selector.Padding = new Thickness(5, 7, 5, 7); Selector.Cursor = Cursors.Hand;
        AutomationProperties.SetAutomationId(Selector, "ActionSlot_" + id); AutomationProperties.SetName(Selector, title + "动作选择"); Selector.Click += (_, _) => Clicked?.Invoke(this, EventArgs.Empty); Grid.SetColumn(Selector, 2); grid.Children.Add(Selector); Child = grid;
    }
    public void SetAction(string value) { _action.Text = value; AutomationProperties.SetName(Selector, value + "，选择动作"); }
}

public static class ActionMenus
{
    public static void Open(Button button, Window owner, bool global, Action<MAction?> choose, Action inherit, Action record, Action advanced)
    {
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        void Item(string text, Action run, string? id = null) { var item = new MenuItem { Header = text }; item.Click += (_, _) => run(); if (id != null) AutomationProperties.SetAutomationId(item, id); menu.Items.Add(item); }
        MenuItem Group(string text) { var item = new MenuItem { Header = text }; menu.Items.Add(item); return item; }
        void Child(MenuItem group, string text, MAction action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => choose(action); group.Items.Add(item); }
        Item("录制快捷键…", record, "RecordShortcut"); if (!global) Item("继承全局（清除此项覆盖）", inherit, "InheritGlobal"); Item("无动作", () => choose(MAction.None), "NoAction"); menu.Items.Add(new Separator());
        var sys = Group("系统功能"); foreach (var pair in new[] { ("音量增大", "volume_up"), ("音量减小", "volume_down"), ("静音", "mute"), ("播放 / 暂停", "play_pause"), ("下一首", "next"), ("上一首", "prev"), ("任务视图", "task_view"), ("开始菜单", "start_menu"), ("Windows 搜索", "search"), ("显示桌面", "show_desktop"), ("锁定屏幕", "lock_screen"), ("截图", "screenshot"), ("关闭显示器", "display_sleep"), ("回到上一个 App", "app_mru_back"), ("下一个 App", "next_app"), ("上一个 App", "previous_app"), ("左侧虚拟桌面", "space_left"), ("右侧虚拟桌面", "space_right") }) Child(sys, pair.Item1, MAction.SystemAction(pair.Item2));
        var moment = Group("按住进入功能模式"); var toggle = Group("开关功能模式"); for (int i = 1; i <= 3; i++) { Child(moment, ActionLabels.Mode(i), MAction.LayerMomentary(i)); Child(toggle, ActionLabels.Mode(i), MAction.LayerToggle(i)); }
        var win = Group("窗口 / 标签 / 鼠标"); Child(win, "窗口循环（同应用）", MAction.WindowCycle("app")); Child(win, "窗口循环（全局）", MAction.WindowCycle("global")); Child(win, "下一个标签页", MAction.TabJump(1)); Child(win, "上一个标签页", MAction.TabJump(-1)); for (int i = 1; i <= 9; i++) Child(win, $"跳到第 {i} 个标签页", MAction.TabJump(index: i)); Child(win, "鼠标模式开关", MAction.MouseMode); Child(win, "聚焦输入框", MAction.FocusInput);
        var overlays = Group("打开浮层"); Child(overlays, "窗口选择器", MAction.Overlay("window_picker")); Child(overlays, "系统功能菜单", MAction.Overlay("system_menu")); Child(overlays, "App 轮盘", MAction.Overlay("app_wheel")); Child(overlays, "按键教程", MAction.Overlay("tutorial"));
        Item("语音输入", () => choose(MAction.Voice)); menu.Items.Add(new Separator());
        Item("打开应用 / URI…", () => { var v = ActionInputDialog.Ask(owner, "打开应用", "应用路径、exe 名称或 URI", "ms-settings:", true); if (v != null) choose(MAction.OpenApp(v)); }, "OpenApplication");
        Item("运行 Shell 命令…", () => { var v = ActionInputDialog.Ask(owner, "运行 Shell 命令", "命令及参数"); if (v != null) choose(MAction.Shell(v)); }, "ShellCommand");
        Item("输入一段文本（宏）…", () => { var v = ActionInputDialog.Ask(owner, "输入一段文本", "要输入的文本，结尾自动追加回车", multiline: true); if (v != null) choose(MAction.Macro(MacroStep.Text(v), MacroStep.Delay(50), MacroStep.Of(MAction.KeyStroke("return")))); }, "TextMacro");
        menu.Items.Add(new Separator()); Item("高级：编辑动作 JSON…", advanced, "AdvancedJson"); button.ContextMenu = menu; menu.IsOpen = true;
    }
}

public sealed class ActionInputDialog : Window
{
    private readonly TextBox _box = new() { Padding = new Thickness(8), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private ActionInputDialog(string title, string label, string initial, bool browse, bool multiline)
    {
        Title = title; Width = 480; Height = multiline ? 330 : 290; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; FontFamily = new FontFamily("Microsoft YaHei UI");
        var root = new StackPanel { Margin = new Thickness(24) }; root.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold }); root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 16, 0, 8), TextWrapping = TextWrapping.Wrap }); _box.Text = initial; AutomationProperties.SetAutomationId(_box, "ActionInput"); root.Children.Add(_box);
        if (browse) { var b = new Button { Content = "浏览可执行文件…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 6, 10, 6) }; b.Click += (_, _) => { var d = new Microsoft.Win32.OpenFileDialog { Filter = "应用程序|*.exe|所有文件|*.*" }; if (d.ShowDialog(this) == true) _box.Text = d.FileName; }; root.Children.Add(b); }
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; var cancel = new Button { Content = "取消", Padding = new Thickness(14, 7, 14, 7) }; cancel.Click += (_, _) => Close(); var ok = new Button { Content = "确认", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(8, 0, 0, 0) }; ok.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_box.Text)) { DialogResult = true; Close(); } else MessageBox.Show(this, "请输入内容。", Title); }; row.Children.Add(cancel); row.Children.Add(ok); root.Children.Add(row); Content = root; Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }
    public static string? Ask(Window owner, string title, string label, string initial = "", bool browse = false, bool multiline = false) { var d = new ActionInputDialog(title, label, initial, browse, multiline) { Owner = owner }; return d.ShowDialog() == true ? d._box.Text.Trim() : null; }
}

public sealed class ShortcutRecorderWindow : Window
{
    private readonly TextBlock _preview = new() { FontSize = 26, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly Button _confirm = new() { Content = "确认并使用", IsEnabled = false };
    private KeyStrokeAction? _captured;
    private bool _recording = true;
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
    public ShortcutRecorderWindow()
    {
        Title = "录制快捷键"; Width = 440; Height = 340; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Microsoft YaHei UI");
        var root = new StackPanel { Margin = new Thickness(28) }; root.Children.Add(new TextBlock { Text = "录制快捷键", FontSize = 20, FontWeight = FontWeights.Bold }); root.Children.Add(new TextBlock { Text = "请按下组合键，左右修饰键会被保留。", Foreground = Brushes.Gray, Margin = new Thickness(0, 14, 0, 12) });
        var area = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(52, 120, 246)), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10), Height = 100, Padding = new Thickness(16), Background = new SolidColorBrush(Color.FromRgb(246, 249, 255)), Child = _preview, Focusable = true };
        AutomationProperties.SetAutomationId(area, "ShortcutCapture"); AutomationProperties.SetName(area, "快捷键捕获区域，按下键盘组合键"); root.Children.Add(area); _preview.Text = "按下快捷键…"; AutomationProperties.SetAutomationId(_preview, "ShortcutPreview"); AutomationProperties.SetName(_preview, "等待输入快捷键");
        root.Children.Add(new TextBlock { Text = "Esc 也会作为按键录入。取消请使用下方取消或右上角关闭。", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 18) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; var cancel = new Button { Content = "取消", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) }; AutomationProperties.SetAutomationId(cancel, "CancelShortcut"); cancel.Click += (_, _) => Close();
        var redo = new Button { Content = "重新录制", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) }; AutomationProperties.SetAutomationId(redo, "ReRecordShortcut"); redo.Click += (_, _) => { _captured = null; _recording = true; _preview.Text = "按下快捷键…"; _confirm.IsEnabled = false; area.Focus(); };
        _confirm.Padding = new Thickness(14, 7, 14, 7); AutomationProperties.SetAutomationId(_confirm, "ConfirmShortcut"); AutomationProperties.SetName(_confirm, "确认并使用快捷键"); _confirm.Click += (_, _) => { DialogResult = true; Close(); }; buttons.Children.Add(cancel); buttons.Children.Add(redo); buttons.Children.Add(_confirm); root.Children.Add(buttons); Content = root; PreviewKeyDown += CaptureKey; Loaded += (_, _) => area.Focus();
    }
    private void CaptureKey(object? sender, KeyEventArgs e)
    {
        if (!_recording) return; e.Handled = true; if (e.IsRepeat) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (key == Key.None || key == Key.DeadCharProcessed || IsModifier(key)) return;
        var name = KeyName(key); if (name == null) { _preview.Text = "暂不支持此键"; return; }
        _captured = new KeyStrokeAction(name, Modifiers().ToArray()); _recording = false; _preview.Text = ActionLabels.Key(_captured); AutomationProperties.SetName(_preview, "已捕获快捷键：" + _preview.Text); _confirm.IsEnabled = true;
    }
    private static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
    private static List<string> Modifiers()
    {
        var result = new List<string>(); Add(0xA2, "left_ctrl"); Add(0xA3, "right_ctrl"); Add(0xA0, "left_shift"); Add(0xA1, "right_shift"); Add(0xA4, "left_alt"); Add(0xA5, "right_alt"); Add(0x5B, "left_win"); Add(0x5C, "right_win");
        if (result.Count == 0) { if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) result.Add("ctrl"); if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) result.Add("shift"); if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) result.Add("alt"); if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) result.Add("win"); } return result;
        void Add(int vk, string name) { if ((GetKeyState(vk) & 0x8000) != 0) result.Add(name); }
    }
    public static string? KeyName(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return key.ToString().ToLowerInvariant(); if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString(); if (key >= Key.NumPad0 && key <= Key.NumPad9) return "numpad_" + (int)(key - Key.NumPad0); if (key >= Key.F1 && key <= Key.F24) return "f" + (int)(key - Key.F1 + 1);
        return key switch { Key.Return => "return", Key.Escape => "escape", Key.Tab => "tab", Key.Space => "space", Key.Back => "delete", Key.Delete => "forward_delete", Key.Up => "up_arrow", Key.Down => "down_arrow", Key.Left => "left_arrow", Key.Right => "right_arrow", Key.Home => "home", Key.End => "end", Key.PageUp => "page_up", Key.PageDown => "page_down", Key.Insert => "insert", Key.PrintScreen => "print_screen", Key.Scroll => "scroll_lock", Key.Pause => "pause", Key.CapsLock => "caps_lock", Key.NumLock => "num_lock", Key.OemOpenBrackets => "left_bracket", Key.OemCloseBrackets => "right_bracket", Key.OemMinus => "minus", Key.OemPlus => "equal", Key.OemComma => "comma", Key.OemPeriod => "period", Key.OemQuestion => "slash", Key.OemSemicolon => "semicolon", Key.OemQuotes => "quote", Key.OemBackslash => "backslash", Key.OemTilde => "grave", Key.VolumeUp => "volume_up", Key.VolumeDown => "volume_down", Key.VolumeMute => "mute", Key.MediaPlayPause => "play_pause", Key.MediaNextTrack => "media_next", Key.MediaPreviousTrack => "media_prev", Key.MediaStop => "media_stop", _ => null };
    }
    public KeyStrokeAction? Result => DialogResult == true ? _captured : null;
}

public sealed class RemoteDiagram : Canvas
{
    private readonly Dictionary<RemoteKey, Shape> _shapes = new();
    public event Action<RemoteKey>? Selected;
    private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(52, 120, 246));
    public RemoteDiagram()
    {
        Width = 180; Height = 480; Add(new Rectangle { Width = 168, Height = 448, RadiusX = 40, RadiusY = 40, Fill = new System.Windows.Media.LinearGradientBrush(Color.FromRgb(224, 226, 227), Color.FromRgb(204, 207, 209), 0), Stroke = Brushes.Silver }, 6, 6);
        Circle(RemoteKey.Power, 40, 29, 34, "⏻"); Circle(RemoteKey.Voice, 106, 29, 34, "♩"); Add(new Ellipse { Width = 142, Height = 142, Fill = Brushes.Black }, 19, 97);
        Segment(RemoteKey.Up, "M66.7,144.7 L39.8,117.8 A71,71 0 0,1 140.2,117.8 L113.3,144.7 A33,33 0 0,0 66.7,144.7 Z", 87, 110); Segment(RemoteKey.Right, "M113.3,144.7 L140.2,117.8 A71,71 0 0,1 140.2,218.2 L113.3,191.3 A33,33 0 0,0 113.3,144.7 Z", 139, 159); Segment(RemoteKey.Down, "M113.3,191.3 L140.2,218.2 A71,71 0 0,1 39.8,218.2 L66.7,191.3 A33,33 0 0,0 113.3,191.3 Z", 87, 210); Segment(RemoteKey.Left, "M66.7,191.3 L39.8,218.2 A71,71 0 0,1 39.8,117.8 L66.7,144.7 A33,33 0 0,0 66.7,191.3 Z", 35, 159);
        Circle(RemoteKey.Ok, 57, 135, 66, "OK"); Circle(RemoteKey.Back, 38, 252, 38, "←"); Circle(RemoteKey.Home, 38, 298, 38, "⌂"); Circle(RemoteKey.Menu, 38, 344, 38, "≡"); Segment(RemoteKey.VolUp, "M101,294 L101,274 A22,22 0 0,1 123,252 A22,22 0 0,1 145,274 L145,294 Z", 116, 260, "+"); Segment(RemoteKey.VolDown, "M101,294 L101,314 A22,22 0 0,0 123,336 A22,22 0 0,0 145,314 L145,294 Z", 116, 302, "−"); Circle(RemoteKey.Tv, 104, 344, 38, "▱"); Add(new TextBlock { Text = "xiaomi", Foreground = Brushes.DarkGray, FontSize = 9 }, 74, 420);
    }
    private void Add(UIElement e, double x, double y) { SetLeft(e, x); SetTop(e, y); Children.Add(e); }
    private void Circle(RemoteKey key, double x, double y, double size, string text)
    {
        var shape = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromRgb(26, 27, 28)), StrokeThickness = 2 }; var grid = new Grid { Width = size, Height = size }; grid.Children.Add(shape); grid.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontSize = key == RemoteKey.Ok ? 13 : 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }); var hit = HitButton(grid, size, size); Register(key, hit, shape); Add(hit, x, y);
    }
    private void Segment(RemoteKey key, string geometry, double x, double y, string text = "•")
    {
        var shape = new Path { Data = Geometry.Parse(geometry), Fill = new SolidColorBrush(Color.FromRgb(26, 27, 28)), StrokeThickness = 2 }; var canvas = new Canvas { Width = 180, Height = 400 }; canvas.Children.Add(shape); var label = new TextBlock { Text = text, Foreground = Brushes.LightGray, FontSize = 18, IsHitTestVisible = false }; SetLeft(label, x); SetTop(label, y); canvas.Children.Add(label); var hit = HitButton(canvas, 180, 400); Register(key, hit, shape); Children.Add(hit);
    }
    private static Button HitButton(UIElement content, double width, double height)
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        var border = new FrameworkElementFactory(typeof(Border));
        border.AppendChild(presenter);
        return new Button { Content = content, Width = width, Height = height, Padding = new Thickness(0), Margin = new Thickness(0), BorderThickness = new Thickness(0), Template = new ControlTemplate(typeof(Button)) { VisualTree = border }, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    }
    private void Register(RemoteKey key, FrameworkElement target, Shape shape)
    {
        _shapes[key] = shape; target.Cursor = Cursors.Hand; target.ToolTip = key.DisplayName(); target.Focusable = true; AutomationProperties.SetAutomationId(target, "RemoteKey_" + key.RawName()); AutomationProperties.SetName(target, key.DisplayName()); if (target is Button button) button.Click += (_, _) => Selected?.Invoke(key); target.MouseLeftButtonDown += (_, e) => { Selected?.Invoke(key); e.Handled = true; }; target.KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Return) { Selected?.Invoke(key); e.Handled = true; } };
    }
    public void Select(RemoteKey key) { foreach (var p in _shapes) p.Value.Stroke = p.Key == key ? Blue : Brushes.Transparent; }
}
