using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using SteelSeriesAssist.Domain;

namespace SteelSeriesAssist.Sonar;

public sealed class SonarClient : ISonarClient, IDisposable
{
    private static readonly HashSet<string> VolumeChannels =
        ["master", "game", "chatRender", "chatCapture", "media", "aux"];

    private static readonly HashSet<string> BindingChannels =
        ["game", "chat", "media", "aux", "mic"];

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public SonarClient(Uri baseAddress, TimeSpan? timeout = null)
    {
        EnsureLoopbackHttpAddress(baseAddress);
        var handler = new HttpClientHandler { UseProxy = false };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = baseAddress,
            Timeout = timeout ?? TimeSpan.FromSeconds(3)
        };
    }

    public async Task<SonarSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var modeTask = GetAsync<string>("/mode", cancellationToken);
        var volumesTask = GetAsync<VolumeSettingsDocument>("/volumeSettings/classic", cancellationToken);
        var devicesTask = GetAsync<List<AudioDeviceDto>>("/audioDevices", cancellationToken);
        var bindingsTask = GetAsync<List<ChannelBindingDto>>("/classicRedirections", cancellationToken);
        var routingTask = GetAsync<List<DeviceSessionsDto>>("/AudioDeviceRouting", cancellationToken);

        await Task.WhenAll(modeTask, volumesTask, devicesTask, bindingsTask, routingTask).ConfigureAwait(false);
        return new SonarSnapshot(
            await modeTask.ConfigureAwait(false),
            MapVolumes(await volumesTask.ConfigureAwait(false)),
            (await devicesTask.ConfigureAwait(false)).Select(MapDevice).ToArray(),
            (await bindingsTask.ConfigureAwait(false)).Select(MapBinding).ToArray(),
            (await routingTask.ConfigureAwait(false)).Select(MapDeviceSessions).ToArray(),
            DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<ChannelVolume>> GetClassicVolumesAsync(
        CancellationToken cancellationToken = default)
    {
        var document = await GetAsync<VolumeSettingsDocument>("/volumeSettings/classic", cancellationToken)
            .ConfigureAwait(false);
        return MapVolumes(document);
    }

    public async Task<VolumeState> SetChannelVolumeAsync(
        string channel,
        float volume,
        CancellationToken cancellationToken = default)
    {
        EnsureVolumeChannel(channel);
        if (volume is < 0 or > 1 || float.IsNaN(volume))
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 1.");
        }

        var value = volume.ToString("R", CultureInfo.InvariantCulture);
        await PutAsync($"/volumeSettings/classic/{channel}/Volume/{value}", cancellationToken)
            .ConfigureAwait(false);
        return await ReadClassicVolumeAsync(channel, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VolumeState> SetChannelMutedAsync(
        string channel,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        EnsureVolumeChannel(channel);
        await PutAsync($"/volumeSettings/classic/{channel}/Mute/{muted.ToString().ToLowerInvariant()}", cancellationToken)
            .ConfigureAwait(false);
        return await ReadClassicVolumeAsync(channel, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChannelBinding> SetChannelBindingAsync(
        string channel,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        if (!BindingChannels.Contains(channel))
        {
            throw new ArgumentException($"Unknown Sonar binding channel '{channel}'.", nameof(channel));
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("A target device ID is required.", nameof(deviceId));
        }

        var response = await PutAsync<ChannelBindingDto>(
            $"/classicRedirections/{channel}/deviceId/{Uri.EscapeDataString(deviceId)}",
            cancellationToken).ConfigureAwait(false);
        return MapBinding(response);
    }

    public Task RouteApplicationAsync(
        string dataFlow,
        string targetDeviceId,
        int processId,
        CancellationToken cancellationToken = default)
    {
        if (dataFlow is not ("render" or "capture"))
        {
            throw new ArgumentException("Data flow must be 'render' or 'capture'.", nameof(dataFlow));
        }

        if (string.IsNullOrWhiteSpace(targetDeviceId))
        {
            throw new ArgumentException("A target device ID is required.", nameof(targetDeviceId));
        }

        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), "A positive process ID is required.");
        }

        return PutAsync(
            $"/AudioDeviceRouting/{dataFlow}/{Uri.EscapeDataString(targetDeviceId)}/{processId}",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Sonar returned an empty JSON response for {path}.");
    }

    private async Task PutAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> PutAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Sonar returned an empty JSON response for {path}.");
    }

    private async Task<VolumeState> ReadClassicVolumeAsync(string channel, CancellationToken cancellationToken)
    {
        var document = await GetAsync<VolumeSettingsDocument>("/volumeSettings/classic", cancellationToken)
            .ConfigureAwait(false);
        var state = channel == "master"
            ? document.Masters?.Classic
            : document.Devices?.GetValueOrDefault(channel)?.Classic;
        return state?.ToDomain()
            ?? throw new InvalidDataException($"Sonar did not return volume state for channel '{channel}'.");
    }

    private static void EnsureVolumeChannel(string channel)
    {
        if (!VolumeChannels.Contains(channel))
        {
            throw new ArgumentException($"Unknown Sonar volume channel '{channel}'.", nameof(channel));
        }
    }

    private static IReadOnlyList<ChannelVolume> MapVolumes(VolumeSettingsDocument document)
    {
        var result = new List<ChannelVolume>();
        if (document.Masters?.Classic is not null)
        {
            result.Add(new ChannelVolume("master", document.Masters.Classic.ToDomain()));
        }

        if (document.Devices is not null)
        {
            result.AddRange(document.Devices
                .Where(pair => pair.Value.Classic is not null)
                .Select(pair => new ChannelVolume(pair.Key, pair.Value.Classic!.ToDomain())));
        }

        return result;
    }

    private static AudioDevice MapDevice(AudioDeviceDto device) => new(
        device.FriendlyName ?? "Unknown device",
        device.Id ?? string.Empty,
        device.DataFlow ?? "unknown",
        device.Role ?? "none",
        device.State ?? "unknown",
        device.IsVad);

    private static ChannelBinding MapBinding(ChannelBindingDto binding) => new(
        binding.Id ?? "unknown",
        binding.DeviceId ?? string.Empty,
        binding.IsRunning);

    private static DeviceSessions MapDeviceSessions(DeviceSessionsDto routing) => new(
        routing.DeviceId ?? string.Empty,
        routing.Role ?? "none",
        routing.DataFlow ?? "unknown",
        (routing.AudioSessions ?? []).Select(session => new AudioSession(
            session.Id ?? string.Empty,
            session.ProcessName ?? "Unknown",
            session.ProcessId,
            session.DisplayName ?? session.ProcessName ?? "Unknown",
            session.State ?? "unknown",
            session.IsSystemSound,
            session.RoutingErrorDetected)).ToArray());

    private static void EnsureLoopbackHttpAddress(Uri address)
    {
        if (address.Scheme != Uri.UriSchemeHttp ||
            !System.Net.IPAddress.TryParse(address.Host, out var ip) ||
            !System.Net.IPAddress.IsLoopback(ip))
        {
            throw new ArgumentException("The Sonar API address must be an HTTP loopback address.", nameof(address));
        }
    }

    internal sealed record VolumeSettingsDocument(
        [property: JsonPropertyName("masters")] VolumeBranches? Masters,
        [property: JsonPropertyName("devices")] Dictionary<string, VolumeBranches>? Devices);

    internal sealed record VolumeBranches(
        [property: JsonPropertyName("classic")] VolumeStateDto? Classic);

    internal sealed record VolumeStateDto(
        [property: JsonPropertyName("volume")] float Volume,
        [property: JsonPropertyName("muted")] bool Muted)
    {
        public VolumeState ToDomain() => new(Volume, Muted);
    }

    internal sealed record AudioDeviceDto(
        [property: JsonPropertyName("friendlyName")] string? FriendlyName,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("dataFlow")] string? DataFlow,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("isVad")] bool IsVad);

    internal sealed record ChannelBindingDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("deviceId")] string? DeviceId,
        [property: JsonPropertyName("isRunning")] bool IsRunning);

    internal sealed record DeviceSessionsDto(
        [property: JsonPropertyName("deviceId")] string? DeviceId,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("dataFlow")] string? DataFlow,
        [property: JsonPropertyName("audioSessions")] List<AudioSessionDto>? AudioSessions);

    internal sealed record AudioSessionDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("processName")] string? ProcessName,
        [property: JsonPropertyName("processId")] int ProcessId,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("isSystemSound")] bool IsSystemSound,
        [property: JsonPropertyName("routingErrorDetected")] bool RoutingErrorDetected);
}
