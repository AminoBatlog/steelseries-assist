namespace SteelSeriesAssist.Domain;

public sealed record VolumeState(float Volume, bool Muted);

public sealed record ChannelVolume(string Channel, VolumeState State);

public sealed record AudioDevice(
    string FriendlyName,
    string Id,
    string DataFlow,
    string Role,
    string State,
    bool IsVirtual);

public sealed record ChannelBinding(
    string Channel,
    string DeviceId,
    bool IsRunning);

public sealed record AudioSession(
    string Id,
    string ProcessName,
    int ProcessId,
    string DisplayName,
    string State,
    bool IsSystemSound,
    bool RoutingErrorDetected);

public sealed record DeviceSessions(
    string DeviceId,
    string Role,
    string DataFlow,
    IReadOnlyList<AudioSession> Sessions);

public sealed record SonarSnapshot(
    string Mode,
    IReadOnlyList<ChannelVolume> Volumes,
    IReadOnlyList<AudioDevice> Devices,
    IReadOnlyList<ChannelBinding> Bindings,
    IReadOnlyList<DeviceSessions> DeviceSessions,
    DateTimeOffset CapturedAt);
