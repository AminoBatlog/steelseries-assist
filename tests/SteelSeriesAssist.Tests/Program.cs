using System.Net;
using System.Net.Sockets;
using SteelSeriesAssist.Sonar;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Composite discovery falls back", CompositeDiscoveryFallsBack),
    ("TCP discovery finds a Sonar-shaped listener", TcpDiscoveryFindsListener),
    ("Sonar client rejects non-loopback endpoints", ClientRejectsRemoteEndpoint),
    ("Sonar client validates write commands", ClientValidatesWriteCommands)
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
