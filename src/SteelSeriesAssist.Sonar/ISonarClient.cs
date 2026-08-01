using SteelSeriesAssist.Domain;

namespace SteelSeriesAssist.Sonar;

public interface ISonarClient
{
    Task<SonarSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<VolumeState> SetChannelVolumeAsync(
        string channel,
        float volume,
        CancellationToken cancellationToken = default);

    Task<VolumeState> SetChannelMutedAsync(
        string channel,
        bool muted,
        CancellationToken cancellationToken = default);

    Task<ChannelBinding> SetChannelBindingAsync(
        string channel,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task RouteApplicationAsync(
        string dataFlow,
        string targetDeviceId,
        int processId,
        CancellationToken cancellationToken = default);
}
