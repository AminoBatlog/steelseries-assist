using System.Net.WebSockets;
using System.Text.Json;
using SteelSeriesAssist.Domain;

namespace SteelSeriesAssist.Sonar;

public sealed class SonarEventClient : IAsyncDisposable
{
    internal const string VolumeEventName = "SONAR_EVENT_VOLUME_DATA";

    private static readonly TimeSpan[] ReconnectDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _receiveTask;
    private volatile bool _isConnected;
    private bool _disposed;

    public event Action<IReadOnlyList<ChannelVolumeUpdate>>? VolumeChanged;

    public event Action<bool>? ConnectionStateChanged;

    public event Action? ChatMixChanged;

    public bool IsConnected => _isConnected;

    public async Task StartAsync(Uri sonarHttpAddress, CancellationToken cancellationToken = default)
    {
        var socketAddress = CreateSocketAddress(sonarHttpAddress);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await StopSessionAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _lifetimeCancellation.Token;
            _receiveTask = Task.Run(() => RunReceiveLoopAsync(socketAddress, token));
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            await StopSessionAsync().ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StopSessionAsync()
    {
        _lifetimeCancellation?.Cancel();
        try
        {
            if (_receiveTask is not null) await _receiveTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = null;
            _receiveTask = null;
            SetConnected(false);
        }
    }

    internal static Uri CreateSocketAddress(Uri sonarHttpAddress)
    {
        if (sonarHttpAddress.Scheme != Uri.UriSchemeHttp ||
            !System.Net.IPAddress.TryParse(sonarHttpAddress.Host, out var address) ||
            !System.Net.IPAddress.IsLoopback(address))
        {
            throw new ArgumentException("The Sonar event address must be an HTTP loopback address.", nameof(sonarHttpAddress));
        }

        return new UriBuilder(sonarHttpAddress)
        {
            Scheme = "ws",
            Path = "/sock",
            Query = string.Empty
        }.Uri;
    }

    internal static IReadOnlyList<ChannelVolumeUpdate> ParseVolumeEvent(string message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        if (!root.TryGetProperty("event", out var eventName) ||
            eventName.GetString() != VolumeEventName ||
            !root.TryGetProperty("data", out var data))
        {
            return Array.Empty<ChannelVolumeUpdate>();
        }

        var updates = new List<ChannelVolumeUpdate>();
        if (data.TryGetProperty("masters", out var masters) &&
            masters.TryGetProperty("classic", out var masterClassic))
        {
            updates.Add(ParseChannelUpdate("master", masterClassic));
        }

        if (data.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Object)
        {
            foreach (var device in devices.EnumerateObject())
            {
                if (device.Value.TryGetProperty("classic", out var classic))
                {
                    updates.Add(ParseChannelUpdate(device.Name, classic));
                }
            }
        }

        return updates.Where(update => update.Volume.HasValue || update.Muted.HasValue).ToArray();
    }

    private static ChannelVolumeUpdate ParseChannelUpdate(string channel, JsonElement classic)
    {
        float? volume = classic.TryGetProperty("volume", out var volumeElement) && volumeElement.TryGetSingle(out var value)
            ? value
            : null;
        bool? muted = classic.TryGetProperty("muted", out var mutedElement) &&
                      mutedElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? mutedElement.GetBoolean()
            : null;
        return new ChannelVolumeUpdate(channel, volume, muted);
    }

    private async Task RunReceiveLoopAsync(Uri socketAddress, CancellationToken cancellationToken)
    {
        var failureCount = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = ReconnectDelays[Math.Min(failureCount, ReconnectDelays.Length - 1)];
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            using var socket = new ClientWebSocket();
            try
            {
                await socket.ConnectAsync(socketAddress, cancellationToken).ConfigureAwait(false);
                failureCount = 0;
                SetConnected(true);
                await ReceiveMessagesAsync(socket, cancellationToken).ConfigureAwait(false);
                failureCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                failureCount++;
            }
            finally
            {
                SetConnected(false);
            }
        }
    }

    private async Task ReceiveMessagesAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var json = await SonarMessageReader.ReadAsync(socket, cancellationToken).ConfigureAwait(false);
            if (json is null) return;
            if (json.Length == 0) continue;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("event", out var eventName) &&
                    eventName.GetString() == "EVENT_SONAR_CHATMIX_DATA")
                {
                    ChatMixChanged?.Invoke();
                }
                var updates = ParseVolumeEvent(json);
                if (updates.Count > 0)
                {
                    VolumeChanged?.Invoke(updates);
                }
            }
            catch (JsonException)
            {
                // A malformed or newer event must not terminate the receive loop.
            }
        }
    }

    private void SetConnected(bool connected)
    {
        if (_isConnected == connected)
        {
            return;
        }

        _isConnected = connected;
        ConnectionStateChanged?.Invoke(connected);
    }
}
