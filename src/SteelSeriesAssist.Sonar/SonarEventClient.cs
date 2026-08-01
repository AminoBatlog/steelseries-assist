using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SteelSeriesAssist.Domain;

namespace SteelSeriesAssist.Sonar;

public sealed class SonarEventClient : IAsyncDisposable
{
    internal const string VolumeEventName = "SONAR_EVENT_VOLUME_DATA";

    private static readonly TimeSpan[] ReconnectDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    private readonly object _sync = new();
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _receiveTask;
    private volatile bool _isConnected;

    public event Action<IReadOnlyList<ChannelVolumeUpdate>>? VolumeChanged;

    public event Action<bool>? ConnectionStateChanged;

    public bool IsConnected => _isConnected;

    public void Start(Uri sonarHttpAddress)
    {
        var socketAddress = CreateSocketAddress(sonarHttpAddress);
        CancellationTokenSource cancellation;
        lock (_sync)
        {
            _lifetimeCancellation?.Cancel();
            _lifetimeCancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            _lifetimeCancellation = cancellation;
            _receiveTask = Task.Run(() => RunReceiveLoopAsync(socketAddress, cancellation.Token));
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? receiveTask;
        lock (_sync)
        {
            _lifetimeCancellation?.Cancel();
            receiveTask = _receiveTask;
        }

        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_sync)
        {
            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = null;
            _receiveTask = null;
        }

        SetConnected(false);
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
        var buffer = new byte[16 * 1024];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            try
            {
                var updates = ParseVolumeEvent(Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length)));
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
