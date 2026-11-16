using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MiRemote.Core;

namespace MiRemote.App
{
    public partial class WindowPickerOverlay : Window
    {
        private readonly AppServices _svc;
        private bool _allApps;
        private List<WindowInfo> _items = new();

        public WindowPickerOverlay(AppServices svc)
        {
            InitializeComponent();
            _svc = svc;
        }

        public void ShowAndActivate()
        {
            _allApps = false;
            Refresh();
            Show();
            Activate();
            Topmost = true;
        }

        public void ShowAllApps()
        {
            _allApps = true;
            Refresh();
            Show();
            Activate();
            Topmost = true;
        }

        private void Refresh()
        {
            uint? pid = null;
            if (!_allApps)
            {
                var fg = WindowSwitcher.ForegroundWindow();
                pid = fg.Pid;
            }
            _items = WindowSwitcher.AllWindows(currentAppOnly: !_allApps, pid: pid);
            List.Items.Clear();
            foreach (var w in _items)
                List.Items.Add(string.IsNullOrEmpty(w.Title) ? w.ProcessName : $"{w.ProcessName}  —  {w.Title}");
            if (List.Items.Count > 0) List.SelectedIndex = 0;
            TitleText.Text = _allApps ? "窗口选择器（所有应用）" : "窗口选择器（当前应用）";
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
                case RemoteKey.Left:
                case RemoteKey.Right:
                    _allApps = !_allApps;
                    Refresh();
                    break;
                case RemoteKey.Ok:
                    if (List.SelectedIndex >= 0 && List.SelectedIndex < _items.Count)
                        WindowSwitcher.Activate(_items[List.SelectedIndex]);
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

        protected override void OnDeactivated(EventArgs e)
        {
            // 不自动关，等返回键
        }
    }
}
