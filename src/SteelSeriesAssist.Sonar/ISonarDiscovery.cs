namespace SteelSeriesAssist.Sonar;

public sealed record SonarEndpoint(Uri BaseAddress, string DiscoveryMethod, int? ProcessId = null);

public interface ISonarDiscovery
{
    Task<SonarEndpoint?> DiscoverAsync(CancellationToken cancellationToken = default);
}
