using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteelSeriesAssist.Sonar;

public sealed class GgCoreDiscovery : ISonarDiscovery
{
    private readonly string _corePropsPath;
    private readonly TimeSpan _timeout;

    public GgCoreDiscovery(string? corePropsPath = null, TimeSpan? timeout = null)
    {
        _corePropsPath = corePropsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SteelSeries", "GG", "coreProps.json");
        _timeout = timeout ?? TimeSpan.FromSeconds(2);
    }

    public async Task<SonarEndpoint?> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_corePropsPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_corePropsPath);
        var props = await JsonSerializer.DeserializeAsync<CoreProps>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (props?.GgEncryptedAddress is null || !TryCreateLoopbackUri(props.GgEncryptedAddress, out var coreAddress))
        {
            return null;
        }

        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = _timeout };
        using var response = await client.GetAsync(new Uri(coreAddress, "/subApps"), cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var subApps = await JsonSerializer.DeserializeAsync<SubAppsResponse>(responseStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var sonar = subApps?.SubApps?.Sonar;
        if (sonar is not { IsEnabled: true, IsReady: true, IsRunning: true } ||
            !Uri.TryCreate(sonar.Metadata?.WebServerAddress, UriKind.Absolute, out var sonarAddress) ||
            !IPAddress.TryParse(sonarAddress.Host, out var ip) || !IPAddress.IsLoopback(ip))
        {
            return null;
        }

        return new SonarEndpoint(sonarAddress, "gg-core-subApps");
    }

    internal static bool TryCreateLoopbackUri(string address, out Uri uri)
    {
        if (Uri.TryCreate($"https://{address}", UriKind.Absolute, out var parsed) &&
            IPAddress.TryParse(parsed.Host, out var ip) && IPAddress.IsLoopback(ip))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private sealed record CoreProps(
        [property: JsonPropertyName("ggEncryptedAddress")] string? GgEncryptedAddress);

    private sealed record SubAppsResponse(
        [property: JsonPropertyName("subApps")] SubApps? SubApps);

    private sealed record SubApps(
        [property: JsonPropertyName("sonar")] SubApp? Sonar);

    private sealed record SubApp(
        [property: JsonPropertyName("isEnabled")] bool IsEnabled,
        [property: JsonPropertyName("isReady")] bool IsReady,
        [property: JsonPropertyName("isRunning")] bool IsRunning,
        [property: JsonPropertyName("metadata")] SubAppMetadata? Metadata);

    private sealed record SubAppMetadata(
        [property: JsonPropertyName("webServerAddress")] string? WebServerAddress);
}
