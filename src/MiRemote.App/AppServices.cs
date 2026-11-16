using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using MiRemote.Core;

namespace MiRemote.App
{
    public class AppServices
    {
        public Logger Logger { get; }
        public ConfigStore ConfigStore { get; }
        public MappingConfig Config { get; private set; }
        public AppMru Mru { get; } = new();
        public ActionRunner Runner { get; private set; } = null!;
        public MappingEngine Engine { get; private set; } = null!;
        public KeyRouter Router { get; private set; } = null!;
        public RawInputListener RawInput { get; private set; } = null!;

        private readonly string _logFile;
        private NotifyIcon? _tray;
        private Win32.WinEventProc? _fgHook;
        private IntPtr _fgHookHandle;

        public AppServices()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiRemote");
            Directory.CreateDirectory(dir);
            _logFile = Path.Combine(dir, "miremote.log");
            Logger = new Logger(_logFile);
            ConfigStore = new ConfigStore(Logger);
            Config = ConfigStore.Load();
            RemoteIdentity.Configure(Config.Settings.RemoteVendorID, Config.Settings.RemoteProductID);
        }

        public void Start()
        {
            Runner = new ActionRunner(Logger, Mru);
            Runner.OnOverlay = name => ShowOverlay(name);
            Runner.OnAppMruBack = _ => Mru.SwitchBack();

            Engine = new MappingEngine(Config, Runner, new EngineDelegate(Logger), new RealScheduler(msg => System.Windows.Application.Current?.Dispatcher.Invoke(msg)));
            Engine.OnEscapeHatch = () => System.Windows.Application.Current?.Dispatcher.Invoke(() => CloseAllOverlays());
            Engine.OnSuspendChanged = suspended => Logger.Info(suspended ? "遥控已暂停" : "遥控已恢复");

            Router = new KeyRouter(Engine, Logger);

            RawInput = new RawInputListener(Logger);
            RawInput.ButtonPressed += ev => Router.Handle(ev);
            RawInput.Log += msg => Logger.Info(msg);

            // 前台 App 变化 → 切换 profile + 维护 MRU
            _fgHook = (_, _, hwnd, _, _, _, _) =>
            {
                if (hwnd == IntPtr.Zero) return;
                try
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Mru.Push(hwnd);
                        var key = WindowSwitcher.ForegroundProcessKey();
                        Engine.SetActiveApp(string.IsNullOrEmpty(key) ? null : key);
                    }));
                }
                catch { }
            };
            _fgHookHandle = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _fgHook, 0, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);

            if (Config.Settings.StartEnabled)
            {
                RawInput.Start();
                Logger.Info("服务已启动");
            }

            SetupTray();
        }

        public void Stop()
        {
            try { RawInput?.Stop(); } catch { }
            try { if (_fgHookHandle != IntPtr.Zero) Win32.UnhookWinEvent(_fgHookHandle); } catch { }
            try { _tray?.Dispose(); } catch { }
            Router?.StopAll();
            ShellProcessRegistry.Shared.TerminateAll();
        }

        public void ApplyConfig(MappingConfig newConfig)
        {
            Config = newConfig;
            ConfigStore.Save(newConfig);
            RemoteIdentity.Configure(newConfig.Settings.RemoteVendorID, newConfig.Settings.RemoteProductID);
            Engine.SetConfig(newConfig);
            Logger.Info("配置已保存并应用");
        }

        public void ToggleSuspended()
        {
            Engine.SetSuspended(!Engine.IsSuspended);
        }

        // ---------- Overlay 路由 ----------
        private WindowPickerOverlay? _picker;
        private SystemMenuOverlay? _systemMenu;

        public void ShowOverlay(string name)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                switch (name)
                {
                    case "window_picker":
                        if (_picker is null) _picker = new WindowPickerOverlay(this);
                        _picker.ShowAndActivate();
                        Engine.SetOverlayCapture(ev => _picker.HandleKey(ev));
                        break;
                    case "system_menu":
                        if (_systemMenu is null) _systemMenu = new SystemMenuOverlay(this);
                        _systemMenu.ShowAndActivate();
                        Engine.SetOverlayCapture(ev => _systemMenu.HandleKey(ev));
                        break;
                    case "app_wheel":
                        // 简化：复用窗口选择器的「所有 App」模式
                        if (_picker is null) _picker = new WindowPickerOverlay(this);
                        _picker.ShowAllApps();
                        Engine.SetOverlayCapture(ev => _picker.HandleKey(ev));
                        break;
                    case "tutorial":
                        System.Windows.MessageBox.Show("当前映射教程：请在设置窗口查看按键映射。", "MiRemote", MessageBoxButton.OK, MessageBoxImage.Information);
                        break;
                }
            });
        }

        public void CloseAllOverlays()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _picker?.Hide();
                _systemMenu?.Hide();
                Engine.SetOverlayCapture(null);
            });
        }

        private void SetupTray()
        {
            _tray = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "MiRemote",
                Visible = true,
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开设置", null, (_, _) => ShowMainWindow());
            menu.Items.Add("暂停/恢复遥控", null, (_, _) => ToggleSuspended());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) => { Stop(); System.Windows.Application.Current.Shutdown(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ShowMainWindow();
        }

        private MainWindow? _mainWindow;
        public void ShowMainWindow()
        {
            if (_mainWindow is null || !_mainWindow.IsVisible)
            {
                _mainWindow = new MainWindow(this);
                _mainWindow.Show();
            }
            else
            {
                _mainWindow.Activate();
                _mainWindow.WindowState = WindowState.Normal;
            }
        }

        private sealed class EngineDelegate : IMappingEngineDelegate
        {
            private readonly Logger _logger;
            public EngineDelegate(Logger logger) => _logger = logger;
            public void MappingLog(string message) => _logger.Info(message);
            public void LayerChanged(int layer)
            {
                _logger.Info($"层变化: {layer}");
                // HUD 由 MainWindow/Overlay 订阅（简化：仅日志）
            }
        }
    }
}
