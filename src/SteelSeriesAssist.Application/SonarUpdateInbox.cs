using SteelSeriesAssist.Domain;

namespace SteelSeriesAssist.Application;

public sealed record VolumeWriteNotice(string Channel, string? Error);
public sealed record SonarUpdateBatch(IReadOnlyList<ChannelVolumeUpdate> Volumes, bool? Connected,
    bool ChatMixChanged, VolumeWriteNotice? WriteNotice = null);

/// <summary>Stores current state, not event history. At most one UI drain is queued.</summary>
public sealed class SonarUpdateInbox(Action<Action> post, Action<SonarUpdateBatch> apply) : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ChannelVolumeUpdate> _volumes = [];
    private static readonly HashSet<string> Channels = ["master", "game", "chatRender", "chatCapture", "media", "aux"];
    private bool? _connected;
    private bool _chatMixChanged;
    private VolumeWriteNotice? _writeNotice;
    private bool _active;
    private bool _queued;
    private bool _disposed;

    public void SetActive(bool active) => Update(() => _active = active);

    public void PublishVolumes(IReadOnlyList<ChannelVolumeUpdate> updates) => Update(() =>
    {
        foreach (var next in updates) StoreVolume(next);
    });

    public void PublishWriteCompleted(string channel, VolumeState confirmed, bool isFinal) => Update(() =>
    {
        StoreVolume(new(channel, confirmed.Volume, confirmed.Muted));
        if (isFinal) _writeNotice = new(channel, null);
    });

    public void PublishWriteFailed(string channel, string error) => Update(() => _writeNotice = new(channel, error));

    private void StoreVolume(ChannelVolumeUpdate next)
    {
        if (!Channels.Contains(next.Channel)) return;
        _volumes.TryGetValue(next.Channel, out var previous);
        _volumes[next.Channel] = new(next.Channel, next.Volume ?? previous?.Volume, next.Muted ?? previous?.Muted);
    }

    public void PublishConnection(bool connected) => Update(() => _connected = connected);
    public void PublishChatMix() => Update(() => _chatMixChanged = true);

    private void Update(Action update)
    {
        lock (_sync)
        {
            if (_disposed) return;
            update();
            if (!_active || _queued || (_volumes.Count == 0 && !_connected.HasValue && !_chatMixChanged && _writeNotice is null)) return;
            _queued = true;
        }
        post(Drain);
    }

    private void Drain()
    {
        SonarUpdateBatch batch;
        lock (_sync)
        {
            _queued = false;
            if (_disposed || !_active) return;
            batch = new(_volumes.Values.ToArray(), _connected, _chatMixChanged, _writeNotice);
            _volumes.Clear();
            _connected = null;
            _chatMixChanged = false;
            _writeNotice = null;
        }
        apply(batch);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _volumes.Clear();
            _connected = null;
            _chatMixChanged = false;
            _writeNotice = null;
        }
    }
}
