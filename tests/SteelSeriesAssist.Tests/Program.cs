using System.Net;
using System.Net.Sockets;
using SteelSeriesAssist.Application;
using SteelSeriesAssist.Domain;
using SteelSeriesAssist.Sonar;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Composite discovery falls back", CompositeDiscoveryFallsBack),
    ("TCP discovery finds a Sonar-shaped listener", TcpDiscoveryFindsListener),
    ("Sonar client rejects non-loopback endpoints", ClientRejectsRemoteEndpoint),
    ("Sonar client validates write commands", ClientValidatesWriteCommands),
    ("Sonar event client builds a loopback socket address", EventClientBuildsSocketAddress),
    ("Sonar event client parses partial volume events", EventClientParsesPartialVolumeEvent),
    ("Sonar event client ignores unrelated events", EventClientIgnoresUnrelatedEvent),
    ("Virtual endpoint mapping selects the correct data flow", VirtualEndpointMappingSelectsDataFlow),
    ("Volume writes collapse intermediate values", VolumeWritesCollapseIntermediateValues)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

return failures == 0 ? 0 : 1;

static async Task CompositeDiscoveryFallsBack()
{
    var expected = new SonarEndpoint(new Uri("http://127.0.0.1:12345"), "test");
    var discovery = new CompositeSonarDiscovery([
        new StubDiscovery(null),
        new StubDiscovery(expected)
    ]);
    var actual = await discovery.DiscoverAsync();
    Assert(actual == expected, "Composite discovery did not return the fallback result.");
}

static async Task TcpDiscoveryFindsListener()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;

    var ports = TcpTableSonarDiscovery.GetListeningPorts();
    Assert(ports.Any(entry => entry.Port == port && entry.ProcessId == Environment.ProcessId),
        "The current process listener was not present in the Windows TCP table.");
    await Task.CompletedTask;
}

static Task ClientRejectsRemoteEndpoint()
{
    AssertThrows<ArgumentException>(() => new SonarClient(new Uri("http://192.0.2.1:1234")));
    return Task.CompletedTask;
}

static async Task ClientValidatesWriteCommands()
{
    using var client = new SonarClient(new Uri("http://127.0.0.1:1"));
    await AssertThrowsAsync<ArgumentOutOfRangeException>(() => client.SetChannelVolumeAsync("game", 1.1f));
    await AssertThrowsAsync<ArgumentException>(() => client.SetChannelMutedAsync("unknown", true));
    await AssertThrowsAsync<ArgumentException>(() => client.SetChannelBindingAsync("master", "device"));
    AssertThrows<ArgumentException>(() => client.RouteApplicationAsync("invalid", "device", 1));
}

static Task EventClientBuildsSocketAddress()
{
    var socketAddress = SonarEventClient.CreateSocketAddress(new Uri("http://127.0.0.1:12345"));
    Assert(socketAddress == new Uri("ws://127.0.0.1:12345/sock"), "The Sonar socket address was incorrect.");
    AssertThrows<ArgumentException>(() => SonarEventClient.CreateSocketAddress(new Uri("http://192.0.2.1:12345")));
    return Task.CompletedTask;
}

static Task EventClientParsesPartialVolumeEvent()
{
    const string json = """
        {
          "event": "SONAR_EVENT_VOLUME_DATA",
          "data": {
            "masters": { "classic": { "muted": true } },
            "devices": {
              "game": { "classic": { "volume": 0.42, "muted": false } }
            }
          }
        }
        """;
    var updates = SonarEventClient.ParseVolumeEvent(json);
    var master = updates.Single(update => update.Channel == "master");
    var game = updates.Single(update => update.Channel == "game");
    Assert(master.Volume is null && master.Muted == true, "The partial master update was not preserved.");
    Assert(Math.Abs(game.Volume!.Value - 0.42f) < 0.001f && game.Muted == false,
        "The game volume event was parsed incorrectly.");
    return Task.CompletedTask;
}

static Task EventClientIgnoresUnrelatedEvent()
{
    var updates = SonarEventClient.ParseVolumeEvent("""{"event":"OTHER_EVENT","data":{}}""");
    Assert(updates.Count == 0, "An unrelated event produced volume updates.");
    return Task.CompletedTask;
}

static Task VirtualEndpointMappingSelectsDataFlow()
{
    AudioDevice[] devices =
    [
        new("Mic monitor", "render-mic", "render", "chatCapture", "active", true),
        new("Sonar microphone", "capture-mic", "capture", "chatCapture", "active", true),
        new("Sonar gaming", "render-game", "render", "game", "active", true),
        new("Inactive gaming", "inactive-game", "render", "game", "inactive", true)
    ];

    Assert(WindowsAudioEndpointVolume.FindEndpointId("chatCapture", devices) == "capture-mic",
        "The microphone channel did not select its capture endpoint.");
    Assert(WindowsAudioEndpointVolume.FindEndpointId("game", devices) == "render-game",
        "The game channel did not select its active render endpoint.");
    Assert(WindowsAudioEndpointVolume.FindEndpointId("master", devices) is null,
        "Master unexpectedly selected a Windows endpoint.");
    return Task.CompletedTask;
}

static async Task VolumeWritesCollapseIntermediateValues()
{
    var writes = new List<float>();
    var writeLock = new object();
    await using var coordinator = new VolumeWriteCoordinator(async (_, volume, cancellationToken) =>
    {
        lock (writeLock)
        {
            writes.Add(volume);
        }

        await Task.Delay(20, cancellationToken);
        return new VolumeState(volume, false);
    }, TimeSpan.FromMilliseconds(75));

    coordinator.Queue("game", 0.1f, isFinal: false);
    await Task.Delay(5);
    coordinator.Queue("game", 0.2f, isFinal: false);
    coordinator.Queue("game", 0.3f, isFinal: true);
    await Task.Delay(180);

    float[] captured;
    lock (writeLock)
    {
        captured = writes.ToArray();
    }

    Assert(captured.Length is >= 1 and <= 2, "Intermediate volume values were not collapsed.");
    Assert(Math.Abs(captured[^1] - 0.3f) < 0.001f, "The final volume value was not written.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected exception {typeof(T).Name} was not thrown.");
}

static async Task AssertThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected exception {typeof(T).Name} was not thrown.");
}

file sealed class StubDiscovery(SonarEndpoint? endpoint) : ISonarDiscovery
{
    public Task<SonarEndpoint?> DiscoverAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(endpoint);
}
