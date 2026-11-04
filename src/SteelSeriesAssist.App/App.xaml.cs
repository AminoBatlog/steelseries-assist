using System.Windows;
using System.Drawing;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SteelSeriesAssist.App;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _applicationIcon;
    private MainWindow? _mainWindow;
    private Mutex? _singleInstanceMutex;
    private Forms.ContextMenuStrip? _trayMenu;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var isSmokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        var mutexName = isSmokeTest
            ? $"Local\\SteelSeriesAssist.SmokeTest.{Environment.ProcessId}"
            : "Local\\SteelSeriesAssist.SingleInstance";
        _singleInstanceMutex = new Mutex(true, mutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _mainWindow = new MainWindow();
        _mainWindow.DpiChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_mainWindow?.IsVisible == true) PanelPlacement.Place(_mainWindow, atCursor: false);
        });
        _mainWindow.HideOnDeactivate = !isSmokeTest;
        MainWindow = _mainWindow;

        var menu = new Forms.ContextMenuStrip();
        _trayMenu = menu;
        menu.Items.Add("打开控制面板", null, (_, _) => ShowPanel());
        var startupItem = new Forms.ToolStripMenuItem("开机启动")
        {
            ToolTipText = "登录 Windows 后静默启动到托盘；请先将便携版放到固定目录。"
        };
        menu.Items.Add(startupItem);
        menu.Opening += (_, _) =>
        {
            try
            {
                startupItem.Checked = StartupRegistration.IsEnabled();
                startupItem.Enabled = true;
            }
            catch (Exception exception)
            {
                startupItem.Checked = false;
                startupItem.Enabled = false;
                startupItem.ToolTipText = $"无法读取启动设置：{exception.Message}";
            }
        };
        startupItem.Click += (_, _) =>
        {
            try
            {
                StartupRegistration.SetEnabled(!startupItem.Checked);
                startupItem.Checked = StartupRegistration.IsEnabled();
            }
            catch (Exception exception)
            {
                _trayIcon?.ShowBalloonTip(5000, "开机启动设置失败", exception.Message, Forms.ToolTipIcon.Error);
            }
        };
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _applicationIcon = TrayIconFactory.Create();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "SteelSeries Assist",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                TogglePanel();
            }
        };

        // Start discovery while the program is quietly resident in the tray, so opening the
        // panel later usually reuses an already warm Sonar connection and EQ cache.
        _mainWindow.LoadSonarStateAsync(showConnecting: false);

        if (isSmokeTest)
        {
            ShowPanel();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ExitApplication();
            };
            timer.Start();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _applicationIcon?.Dispose();
        _trayMenu?.Dispose();

        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    private void TogglePanel()
    {
        if (_mainWindow?.IsVisible == true)
        {
            _mainWindow.Hide();
        }
        else
        {
            ShowPanel();
        }
    }

    private void ShowPanel()
    {
        if (_mainWindow is null)
        {
            return;
        }

        PanelPlacement.Place(_mainWindow, atCursor: true);
        _mainWindow.Show();
        PanelPlacement.Place(_mainWindow, atCursor: true);
        _mainWindow.Activate();
        _mainWindow.LoadSonarStateAsync();
    }

    private async void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;
        if (_trayIcon is not null) _trayIcon.Visible = false;
        try
        {
            if (_mainWindow is not null) await _mainWindow.ShutdownAsync();
        }
        finally
        {
            if (_mainWindow is not null)
            {
                _mainWindow.AllowClose = true;
                _mainWindow.Close();
            }
            Shutdown();
        }
    }
}
