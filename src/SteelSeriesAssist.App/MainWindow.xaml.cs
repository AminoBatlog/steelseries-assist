using System.Windows;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SteelSeriesAssist.Application;
using SteelSeriesAssist.Domain;
using SteelSeriesAssist.Sonar;
using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.Brush;
using WpfPoint = System.Windows.Point;

namespace SteelSeriesAssist.App;

public partial class MainWindow : Window
{
    private static readonly IReadOnlyDictionary<string, string> ChannelNames = new Dictionary<string, string>
    {
        ["master"] = "主音量",
        ["game"] = "游戏",
        ["chatRender"] = "聊天",
        ["chatCapture"] = "麦克风",
        ["media"] = "媒体",
        ["aux"] = "Aux"
    };

    private readonly SonarStateService _stateService = new(new CompositeSonarDiscovery([
        new GgCoreDiscovery(),
        new TcpTableSonarDiscovery()
    ]));
    private SonarClient? _sonarClient;
    private readonly DispatcherTimer _syncTimer;
    private bool _isLoading;
    private bool _isWriting;
    private bool _isDragging;
    private bool _isDeviceDropDownOpen;
    private WpfPoint _dragStartPoint;
    private FrameworkElement? _draggedElement;
    private RoutingLane? _dropTargetLane;
    private Border? _dropTargetBorder;

    public bool AllowClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _syncTimer.Tick += (_, _) =>
        {
            if (IsVisible && !_isLoading && !_isWriting && !_isDragging && !_isDeviceDropDownOpen &&
                Mouse.LeftButton == MouseButtonState.Released)
            {
                LoadSonarStateAsync(showConnecting: false);
            }
        };
        _syncTimer.Start();
    }

    public async void LoadSonarStateAsync(bool showConnecting = true)
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        if (showConnecting)
        {
            StatusText.Text = "正在连接 Sonar…";
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(97, 105, 122));
        }

        try
        {
            SonarSnapshot snapshot;
            if (_sonarClient is not null)
            {
                try
                {
                    snapshot = await _sonarClient.GetSnapshotAsync();
                }
                catch
                {
                    _sonarClient.Dispose();
                    _sonarClient = null;
                    var recovered = await _stateService.LoadAsync();
                    _sonarClient = new SonarClient(recovered.Endpoint.BaseAddress);
                    snapshot = recovered.Snapshot;
                }
            }
            else
            {
                var initial = await _stateService.LoadAsync();
                _sonarClient = new SonarClient(initial.Endpoint.BaseAddress);
                snapshot = initial.Snapshot;
            }
            var bindings = snapshot.Bindings.ToDictionary(
                binding => NormalizeBindingChannel(binding.Channel),
                binding => binding);
            var physicalDevices = snapshot.Devices
                .Where(device => device.State == "active" && !device.IsVirtual)
                .ToArray();
            ChannelList.ItemsSource = snapshot.Volumes
                .OrderBy(volume => VolumeSortOrder(volume.Channel))
                .Select(volume =>
            {
                var binding = bindings.GetValueOrDefault(volume.Channel);
                var dataFlow = volume.Channel == "chatCapture" ? "capture" : "render";
                var options = volume.Channel == "master"
                    ? Array.Empty<DeviceOption>()
                    : physicalDevices
                        .Where(device => device.DataFlow == dataFlow)
                        .Select(device => new DeviceOption(device.FriendlyName, device.Id))
                        .ToArray();
                return new ChannelRow(
                    volume.Channel,
                    ToBindingChannel(volume.Channel),
                    ChannelNames.GetValueOrDefault(volume.Channel, volume.Channel),
                    (int)Math.Round(volume.State.Volume * 100),
                    volume.State.Muted,
                    options,
                    binding?.DeviceId,
                    CreateRoleBrush(volume.Channel));
            }).ToArray();

            var routeDevices = snapshot.Devices
                .Where(device => device.State == "active" && device.IsVirtual && device.DataFlow == "render" &&
                                 device.Role is "game" or "chatRender" or "media" or "aux")
                .GroupBy(device => device.Role)
                .Select(group => group.First())
                .ToDictionary(device => device.Role, device => device);
            RoutingLaneList.ItemsSource = routeDevices.Values
                .OrderBy(device => RouteSortOrder(device.Role))
                .Select(device =>
                {
                    var applications = snapshot.DeviceSessions
                        .Where(route => route.DataFlow == "render" && route.Role == device.Role)
                        .SelectMany(route => route.Sessions)
                        .Where(session => session.State == "active" && !session.IsSystemSound && session.ProcessId > 0)
                        .GroupBy(session => session.ProcessId)
                        .Select(group => new RoutedApplication(
                            group.First().DisplayName,
                            group.Key,
                            group.Count(),
                            device.Role))
                        .OrderBy(application => application.DisplayName)
                        .ToArray();
                    return new RoutingLane(
                        ChannelNames.GetValueOrDefault(device.Role, device.Role),
                        device.Role,
                        device.Id,
                        applications,
                        CreateRoleBrush(device.Role));
                })
                .ToArray();
            StatusText.Text = $"Sonar 已连接 · {snapshot.Devices.Count(device => device.State == "active")} 个活动设备";
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(2, 221, 188));
        }
        catch (Exception exception)
        {
            ChannelList.ItemsSource = Array.Empty<ChannelRow>();
            RoutingLaneList.ItemsSource = Array.Empty<RoutingLane>();
            StatusText.Text = $"Sonar 不可用：{exception.Message}";
            StatusDot.Fill = new SolidColorBrush(MediaColor.FromRgb(255, 83, 89));
        }
        finally
        {
            _isLoading = false;
        }
    }

    private static string NormalizeBindingChannel(string channel) => channel switch
    {
        "chat" => "chatRender",
        "mic" => "chatCapture",
        _ => channel
    };

    private static string? ToBindingChannel(string channel) => channel switch
    {
        "master" => null,
        "chatRender" => "chat",
        "chatCapture" => "mic",
        _ => channel
    };

    private static int RouteSortOrder(string role) => role switch
    {
        "game" => 0,
        "chatRender" => 1,
        "media" => 2,
        "aux" => 3,
        _ => 99
    };

    private static int VolumeSortOrder(string channel) => channel switch
    {
        "master" => 0,
        "game" => 1,
        "chatRender" => 2,
        "media" => 3,
        "aux" => 4,
        "chatCapture" => 5,
        _ => 99
    };

    private static MediaBrush CreateRoleBrush(string role) => role switch
    {
        "game" => new SolidColorBrush(MediaColor.FromRgb(81, 104, 244)),
        "chatRender" => new SolidColorBrush(MediaColor.FromRgb(45, 177, 252)),
        "media" => new SolidColorBrush(MediaColor.FromRgb(2, 221, 188)),
        "aux" => new SolidColorBrush(MediaColor.FromRgb(250, 166, 48)),
        _ => new SolidColorBrush(MediaColor.FromRgb(97, 105, 122))
    };

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isDeviceDropDownOpen && !_isDragging && !IsKeyboardFocusWithin)
            {
                Hide();
            }
        }, DispatcherPriority.ContextIdle);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _syncTimer.Stop();
            _sonarClient?.Dispose();
        }
    }

    private async void VolumeSlider_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider { DataContext: ChannelRow row } || _sonarClient is null)
        {
            return;
        }

        await ExecuteWriteAsync(async () =>
        {
            var confirmed = await _sonarClient.SetChannelVolumeAsync(row.Channel, row.Percent / 100f);
            row.Percent = (int)Math.Round(confirmed.Volume * 100);
            row.Muted = confirmed.Muted;
        }, $"已更新{row.DisplayName}音量");
    }

    private async void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: ChannelRow row } || _sonarClient is null)
        {
            return;
        }

        await ExecuteWriteAsync(async () =>
        {
            var confirmed = await _sonarClient.SetChannelMutedAsync(row.Channel, !row.Muted);
            row.Percent = (int)Math.Round(confirmed.Volume * 100);
            row.Muted = confirmed.Muted;
        }, row.Muted ? $"已取消{row.DisplayName}静音" : $"已静音{row.DisplayName}");
    }

    private async void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox { DataContext: ChannelRow row, SelectedItem: DeviceOption selected } ||
            _sonarClient is null || row.BindingChannel is null || selected.Id == row.BoundDeviceId)
        {
            return;
        }

        await ExecuteWriteAsync(async () =>
        {
            var confirmed = await _sonarClient.SetChannelBindingAsync(row.BindingChannel, selected.Id);
            row.BoundDeviceId = confirmed.DeviceId;
        }, $"{row.DisplayName}已切换到 {selected.Name}");
    }

    private void DeviceComboBox_DropDownOpened(object sender, EventArgs e)
    {
        _isDeviceDropDownOpen = true;
    }

    private void DeviceComboBox_DropDownClosed(object sender, EventArgs e)
    {
        _isDeviceDropDownOpen = false;
    }

    private void RoutedApplication_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
    }

    private void RoutedApplication_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: RoutedApplication application } element)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragging = true;
        _draggedElement = element;
        ShowDragFeedback(application, element);
        try
        {
            var data = new System.Windows.DataObject(typeof(RoutedApplication), application);
            System.Windows.DragDrop.DoDragDrop(element, data, System.Windows.DragDropEffects.Move);
        }
        finally
        {
            HideDragFeedback();
            _isDragging = false;
        }
    }

    private void ShowDragFeedback(RoutedApplication application, FrameworkElement source)
    {
        DragGhostName.Text = application.DisplayName;
        DragGhostRoute.Text = $"来自 {ChannelNames.GetValueOrDefault(application.SourceRole, application.SourceRole)}";
        DragGhost.Visibility = Visibility.Visible;
        DragGhost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.96, TimeSpan.FromMilliseconds(130)));
        DragGhostScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(130)));
        DragGhostScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(130)));
        source.BeginAnimation(OpacityProperty, new DoubleAnimation(source.Opacity, 0.3, TimeSpan.FromMilliseconds(100)));
        UpdateDragGhostPosition(Mouse.GetPosition(LayoutRoot));
    }

    private void HideDragFeedback()
    {
        ResetDropTarget();
        if (_draggedElement is not null)
        {
            _draggedElement.BeginAnimation(OpacityProperty, null);
            _draggedElement.Opacity = 1;
            _draggedElement = null;
        }

        DragGhost.BeginAnimation(OpacityProperty, null);
        DragGhost.Visibility = Visibility.Collapsed;
    }

    private void Window_PreviewDragOver(object sender, System.Windows.DragEventArgs e) =>
        UpdateDragGhostPosition(e.GetPosition(LayoutRoot));

    private void UpdateDragGhostPosition(WpfPoint position)
    {
        Canvas.SetLeft(DragGhost, Math.Min(position.X + 15, Math.Max(0, LayoutRoot.ActualWidth - DragGhost.ActualWidth)));
        Canvas.SetTop(DragGhost, Math.Min(position.Y + 15, Math.Max(0, LayoutRoot.ActualHeight - DragGhost.ActualHeight)));
    }

    private void RoutingLane_DragEnter(object sender, System.Windows.DragEventArgs e) =>
        UpdateDropTarget(sender, e);

    private void RoutingLane_DragOver(object sender, System.Windows.DragEventArgs e) =>
        UpdateDropTarget(sender, e);

    private void RoutingLane_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is Border border && ReferenceEquals(border, _dropTargetBorder))
        {
            var position = e.GetPosition(border);
            if (position.X < 0 || position.Y < 0 || position.X > border.ActualWidth || position.Y > border.ActualHeight)
            {
                ResetDropTarget();
            }
        }
    }

    private void UpdateDropTarget(object sender, System.Windows.DragEventArgs e)
    {
        var application = e.Data.GetData(typeof(RoutedApplication)) as RoutedApplication;
        var lane = (sender as FrameworkElement)?.DataContext as RoutingLane;
        var canDrop = application is not null && lane is not null && application.SourceRole != lane.Role;
        e.Effects = canDrop ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
        if (sender is Border border && lane is not null)
        {
            if (canDrop)
            {
                SetDropTarget(border, lane);
                DragGhostRoute.Text = $"移动到 {lane.Name}";
            }
            else
            {
                ResetDropTarget();
                DragGhostRoute.Text = $"已在 {lane.Name}";
            }
        }
    }

    private void SetDropTarget(Border border, RoutingLane lane)
    {
        if (ReferenceEquals(_dropTargetBorder, border))
        {
            return;
        }

        ResetDropTarget();
        _dropTargetBorder = border;
        _dropTargetLane = lane;
        lane.ShowDropHint();
        border.BorderBrush = lane.AccentBrush;
        border.BeginAnimation(Border.OpacityProperty,
            new DoubleAnimation(0.78, 1, TimeSpan.FromMilliseconds(180)) { AutoReverse = true });
    }

    private void ResetDropTarget()
    {
        if (_dropTargetBorder is not null)
        {
            _dropTargetBorder.BeginAnimation(Border.OpacityProperty, null);
            _dropTargetBorder.Opacity = 1;
            _dropTargetBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(51, 65, 137));
        }

        _dropTargetLane?.HideDropHint();
        _dropTargetBorder = null;
        _dropTargetLane = null;
    }

    private async void RoutingLane_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not Border { DataContext: RoutingLane lane } border ||
            e.Data.GetData(typeof(RoutedApplication)) is not RoutedApplication application ||
            application.SourceRole == lane.Role || _sonarClient is null)
        {
            return;
        }

        ResetDropTarget();
        await ExecuteWriteAsync(async () =>
        {
            await _sonarClient.RouteApplicationAsync("render", lane.DeviceId, application.ProcessId);
            await Task.Delay(250);
        }, $"{application.DisplayName}的默认输出已设为 {lane.Name}");
        LoadSonarStateAsync();
    }

    private async Task ExecuteWriteAsync(Func<Task> operation, string successMessage)
    {
        _isWriting = true;
        ActionStatusText.Text = "正在应用…";
        try
        {
            await operation();
            ActionStatusText.Text = successMessage;
        }
        catch (Exception exception)
        {
            ActionStatusText.Text = $"操作失败：{exception.Message}";
            LoadSonarStateAsync();
        }
        finally
        {
            _isWriting = false;
        }
    }

    private sealed class ChannelRow : INotifyPropertyChanged
    {
        private int _percent;
        private bool _muted;

        public ChannelRow(
            string channel,
            string? bindingChannel,
            string displayName,
            int percent,
            bool muted,
            IReadOnlyList<DeviceOption> deviceOptions,
            string? boundDeviceId,
            MediaBrush accentBrush)
        {
            Channel = channel;
            BindingChannel = bindingChannel;
            DisplayName = displayName;
            _percent = percent;
            _muted = muted;
            DeviceOptions = deviceOptions;
            BoundDeviceId = boundDeviceId;
            SelectedDevice = deviceOptions.FirstOrDefault(device => device.Id == boundDeviceId);
            AccentBrush = accentBrush;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Channel { get; }

        public string? BindingChannel { get; }

        public string DisplayName { get; }

        public IReadOnlyList<DeviceOption> DeviceOptions { get; }

        public DeviceOption? SelectedDevice { get; }

        public string? BoundDeviceId { get; set; }

        public MediaBrush AccentBrush { get; }

        public Visibility DeviceVisibility => BindingChannel is null ? Visibility.Collapsed : Visibility.Visible;

        public Visibility MasterVisibility => BindingChannel is null ? Visibility.Visible : Visibility.Collapsed;

        public int Percent
        {
            get => _percent;
            set
            {
                if (_percent == value)
                {
                    return;
                }

                _percent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PercentLabel));
            }
        }

        public bool Muted
        {
            get => _muted;
            set
            {
                if (_muted == value)
                {
                    return;
                }

                _muted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MuteToolTip));
            }
        }

        public string PercentLabel => $"{Percent}%";

        public string MuteToolTip => Muted ? "取消静音" : "静音";

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record DeviceOption(string Name, string Id);

    private sealed record RoutedApplication(
        string DisplayName,
        int ProcessId,
        int SessionCount,
        string SourceRole)
    {
        public string SessionCountLabel => SessionCount > 1 ? $"×{SessionCount}" : string.Empty;
    }

    private sealed class RoutingLane : INotifyPropertyChanged
    {
        private bool _isDropTarget;
        private string _dropHint = string.Empty;

        public RoutingLane(
            string name,
            string role,
            string deviceId,
            IReadOnlyList<RoutedApplication> applications,
            MediaBrush accentBrush)
        {
            Name = name;
            Role = role;
            DeviceId = deviceId;
            Applications = applications;
            AccentBrush = accentBrush;
        }

        public string Name { get; }

        public string Role { get; }

        public string DeviceId { get; }

        public IReadOnlyList<RoutedApplication> Applications { get; }

        public MediaBrush AccentBrush { get; }

        public string AppCountLabel => $"{Applications.Count} 个";

        public Visibility EmptyVisibility =>
            Applications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public string DropHint => _dropHint;

        public Visibility DropHintVisibility => _isDropTarget ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void ShowDropHint()
        {
            _dropHint = $"释放以移动到 {Name}";
            _isDropTarget = true;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropHint)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropHintVisibility)));
        }

        public void HideDropHint()
        {
            if (!_isDropTarget)
            {
                return;
            }

            _isDropTarget = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropHintVisibility)));
        }
    }
}
