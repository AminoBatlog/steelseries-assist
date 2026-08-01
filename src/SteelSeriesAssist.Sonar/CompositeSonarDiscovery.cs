namespace SteelSeriesAssist.Sonar;

public sealed class CompositeSonarDiscovery(IEnumerable<ISonarDiscovery> strategies) : ISonarDiscovery
{
    private readonly IReadOnlyList<ISonarDiscovery> _strategies = strategies.ToArray();

    public async Task<SonarEndpoint?> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        foreach (var strategy in _strategies)
        {
            try
            {
                var endpoint = await strategy.DiscoverAsync(cancellationToken).ConfigureAwait(false);
                if (endpoint is not null)
                {
                    return endpoint;
                }
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException or UnauthorizedAccessException)
            {
                // A discovery strategy is allowed to fail; the next strategy may still succeed.
            }
        }

        return null;
    }
}
