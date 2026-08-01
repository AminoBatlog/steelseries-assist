using SteelSeriesAssist.Domain;
using SteelSeriesAssist.Sonar;

namespace SteelSeriesAssist.Application;

public sealed class SonarStateService(ISonarDiscovery discovery)
{
    public async Task<(SonarEndpoint Endpoint, SonarSnapshot Snapshot)> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var endpoint = await discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("SteelSeries Sonar is not running or could not be discovered.");
        using var client = new SonarClient(endpoint.BaseAddress);
        var snapshot = await client.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return (endpoint, snapshot);
    }
}
