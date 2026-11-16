using System;
using System.Collections.Generic;
using System.Windows;
using MiRemote.Core;

namespace MiRemote.App
{
    public partial class SystemMenuOverlay : Window
    {
        private readonly AppServices _svc;
        private readonly List<(string Label, string Action)> _items = new()
        {
            ("音量 +", "volume_up"),
            ("音量 -", "volume_down"),
            ("静音", "mute"),
            ("播放/暂停", "play_pause"),
            ("任务视图 (Win+Tab)", "task_view"),
            ("显示桌面", "show_desktop"),
            ("截图 (Win+Shift+S)", "screenshot"),
            ("锁屏", "lock_screen"),
            ("熄屏", "display_sleep"),
        };

        public SystemMenuOverlay(AppServices svc)
        {
            InitializeComponent();
            _svc = svc;
            foreach (var (label, _) in _items) List.Items.Add(label);
            if (List.Items.Count > 0) List.SelectedIndex = 0;
        }

        public void ShowAndActivate()
        {
            Show();
            Activate();
            Topmost = true;
        }

        public void HandleKey(ButtonEvent e)
        {
            if (!IsVisible) return;
            if (!e.IsDown) return;
            switch (e.Key)
            {
                case RemoteKey.Up:
                    if (List.SelectedIndex > 0) List.SelectedIndex--;
                    break;
                case RemoteKey.Down:
                    if (List.SelectedIndex < List.Items.Count - 1) List.SelectedIndex++;
                    break;
                case RemoteKey.Ok:
                    if (List.SelectedIndex >= 0 && List.SelectedIndex < _items.Count)
                        _svc.Runner.Run(MAction.SystemAction(_items[List.SelectedIndex].Action));
                    CloseOverlay();
                    break;
                case RemoteKey.Back:
                    CloseOverlay();
                    break;
            }
        }

        private void CloseOverlay()
        {
            Hide();
            _svc.Engine.SetOverlayCapture(null);
        }
    }
}
