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
        _mainWindow.HideOnDeactivate = !isSmokeTest;
        MainWindow = _mainWindow;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开控制面板", null, (_, _) => ShowPanel());
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

        var workArea = SystemParameters.WorkArea;
        _mainWindow.Left = workArea.Right - _mainWindow.Width - 12;
        _mainWindow.Top = workArea.Bottom - _mainWindow.Height - 12;
        _mainWindow.Show();
        _mainWindow.Activate();
        _mainWindow.LoadSonarStateAsync();
    }

    private void ExitApplication()
    {
        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
            _mainWindow.Close();
        }

        Shutdown();
    }
}
