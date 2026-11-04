using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SteelSeriesAssist.Application;
using SteelSeriesAssist.Domain;
using SteelSeriesAssist.Sonar;

namespace SteelSeriesAssist.App;

public partial class ChatMixControl : System.Windows.Controls.UserControl, IAsyncDisposable
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly SemaphoreSlim _sessions = new(1, 1);
    private Session? _session;
    private Uri? _address;
    private float? _pending;
    private bool _writing;
    private bool _reading;
    private bool _applying;
    private bool _disposed;
    private int _revision;
    private Task? _disposeTask;

    public ChatMixControl()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
    }

    public async Task ConnectAsync(Uri address)
    {
        await _sessions.WaitAsync();
        try
        {
            if (_disposed || _address == address) return;
            _revision++;
            _pending = null;
            var previous = _session;
            _session = null;
            SetEnabled(false);
            if (previous is not null) await previous.DisposeAsync();
            if (_disposed) return;
            _session = new Session(address);
            _address = address;
            HintText.Text = "正在读取聊天混音…";
            Refresh();
        }
        finally { _sessions.Release(); }
    }

    public void Refresh()
    {
        var session = _session;
        if (_disposed || session is null || !IsVisible || _reading || _writing ||
            BalanceSlider.IsMouseCaptureWithin || Mouse.LeftButton == MouseButtonState.Pressed) return;
        _ = session.Operations.RunAsync(token => RefreshAsync(session, token));
    }

    private async Task RefreshAsync(Session session, CancellationToken token)
    {
        _reading = true;
        var revision = _revision;
        try
        {
            var state = await session.Client.GetChatMixAsync(token);
            token.ThrowIfCancellationRequested();
            if (ReferenceEquals(session, _session) && revision == _revision && !_writing && !BalanceSlider.IsMouseCaptureWithin)
                Apply(state);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_disposed && ReferenceEquals(session, _session) && revision == _revision && !_writing)
            {
                SetEnabled(false);
                HintText.Text = "聊天混音暂不可用，连接恢复后自动重试";
            }
        }
        finally { _reading = false; }
    }

    private void Apply(ChatMixState state)
    {
        _applying = true;
        try { BalanceSlider.Value = Math.Round(state.Balance * 100); }
        finally { _applying = false; }
        UpdateLabel();
        SetEnabled(state.CanAdjust);
        HintText.Text = state.State switch
        {
            "enabled" => "向左偏游戏 · 向右偏聊天 · 点击右上角恢复均衡",
            "finiteWheel" => "由耳机旋钮控制，请使用硬件调节聊天混音",
            "differentDeviceSelected" => "请在 GG 中为游戏和聊天选择同一输出设备",
            "noDeviceSelected" => "请先在 GG 中选择游戏和聊天的输出设备",
            _ => "当前 Sonar 状态不支持调整聊天混音"
        };
    }

    private void SetEnabled(bool enabled)
    {
        BalanceSlider.IsEnabled = enabled;
        ResetButton.IsEnabled = enabled;
    }

    private void UpdateLabel()
    {
        var value = (int)Math.Round(BalanceSlider.Value);
        ResetButton.Content = value == 0 ? "均衡" : $"偏{(value < 0 ? "游戏" : "聊天")} {Math.Abs(value)}%";
    }

    private void Balance_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_applying || _disposed || _session is null || !BalanceSlider.IsEnabled) return;
        if (Mouse.LeftButton == MouseButtonState.Pressed && Math.Abs(e.NewValue) <= 2 && e.NewValue != 0)
        {
            BalanceSlider.Value = 0;
            return;
        }
        UpdateLabel();
        Commit();
    }

    private async Task WritePendingAsync(Session session, CancellationToken token)
    {
        _writing = true;
        try
        {
            while (_pending.HasValue && !token.IsCancellationRequested)
            {
                await Task.Delay(90, token);
                if (!_pending.HasValue) break;
                var balance = _pending.Value;
                _pending = null;
                var revision = _revision;
                try
                {
                    var confirmed = await session.Client.SetChatMixAsync(balance, token);
                    token.ThrowIfCancellationRequested();
                    if (revision == _revision && !_pending.HasValue && !BalanceSlider.IsMouseCaptureWithin)
                        Apply(confirmed);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    if (!_disposed && ReferenceEquals(session, _session) && revision == _revision)
                    {
                        try
                        {
                            var current = await session.Client.GetChatMixAsync(token);
                            token.ThrowIfCancellationRequested();
                            if (revision == _revision) Apply(current);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                        catch (Exception) { SetEnabled(false); }
                        if (!_disposed) HintText.Text = $"调整失败：{exception.Message}";
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { _writing = false; }
    }

    private void Commit()
    {
        var session = _session;
        if (_disposed || !BalanceSlider.IsEnabled || session is null) return;
        _revision++;
        _pending = (float)(BalanceSlider.Value / 100);
        if (!_writing) _ = session.Operations.RunAsync(token => WritePendingAsync(session, token));
    }

    private void Balance_MouseUp(object sender, MouseButtonEventArgs e) => Commit();
    private void Balance_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e) => Commit();
    private void Balance_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) Commit();
    }
    private void Reset_Click(object sender, RoutedEventArgs e) { BalanceSlider.Value = 0; Commit(); }

    public ValueTask DisposeAsync() => new(_disposeTask ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        _refreshTimer.Stop();
        _pending = null;
        await _sessions.WaitAsync();
        try
        {
            var session = _session;
            _session = null;
            if (session is not null) await session.DisposeAsync();
        }
        finally { _sessions.Release(); }
    }

    private sealed class Session(Uri address) : IAsyncDisposable
    {
        public SonarClient Client { get; } = new(address);
        public AsyncOperationScope Operations { get; } = new();
        public async ValueTask DisposeAsync()
        {
            try { await Operations.DisposeAsync(); }
            finally { Client.Dispose(); }
        }
    }
}