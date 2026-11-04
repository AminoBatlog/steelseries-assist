using System.Net;
using System.Net.Sockets;
using System.Text;
using SteelSeriesAssist.Sonar;

internal static class ChatMixTests
{
    public static async Task RoundTripAndFallback()
    {
        var paths = new List<string>();
        await using var server = new ChatMixServer(path =>
        {
            paths.Add(path);
            if (path == "/v1/chatMix") return (404, "{}");
            return (200, "{\"balance\":0.35,\"state\":\"enabled\"}");
        });
        using var client = new SonarClient(server.Address);
        var state = await client.SetChatMixAsync(0.35f);
        if (Math.Abs(state.Balance - 0.35f) > 0.001 || !state.CanAdjust ||
            !paths.SequenceEqual(new[] { "/v1/chatMix", "/chatMix", "/chatMix?balance=0.35", "/chatMix" }))
            throw new Exception("ChatMix did not fall back, use the query parameter, and confirm the write.");
    }

    public static async Task RejectsUnconfirmedWrite()
    {
        await using var server = new ChatMixServer(_ => (200, "{\"balance\":0,\"state\":\"enabled\"}"));
        using var client = new SonarClient(server.Address);
        try { await client.SetChatMixAsync(-0.5f); }
        catch (InvalidDataException) { return; }
        throw new Exception("An unconfirmed ChatMix write was accepted.");
    }

    public static async Task HardwareControlIsReadOnly()
    {
        var writes = 0;
        await using var server = new ChatMixServer(path =>
        {
            if (path.Contains('?')) writes++;
            return (200, "{\"balance\":-0.6,\"state\":\"finiteWheel\"}");
        });
        using var client = new SonarClient(server.Address);
        var state = await client.GetChatMixAsync();
        if (state.CanAdjust) throw new Exception("A hardware-controlled mix was writable.");
        try { await client.SetChatMixAsync(0); }
        catch (InvalidOperationException) when (writes == 0) { return; }
        throw new Exception("A hardware-controlled mix was written.");
    }

    public static async Task RejectsInvalidValues()
    {
        using var client = new SonarClient(new Uri("http://127.0.0.1:1"));
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, -1.01f, 1.01f })
        {
            try { await client.SetChatMixAsync(value); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new Exception("Invalid ChatMix balance was accepted.");
        }
    }

    public static async Task DoesNotFallbackOnServerFailure()
    {
        var requests = 0;
        await using var server = new ChatMixServer(_ => { requests++; return (500, "{}"); });
        using var client = new SonarClient(server.Address);
        try { await client.GetChatMixAsync(); }
        catch (HttpRequestException) when (requests == 1) { return; }
        throw new Exception("A server failure incorrectly triggered legacy fallback.");
    }

    private sealed class ChatMixServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        public Uri Address { get; }

        public ChatMixServer(Func<string, (int Status, string Body)> respond)
        {
            _listener.Start();
            Address = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                        await using var stream = connection.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                        var first = await reader.ReadLineAsync(_stop.Token) ?? throw new IOException("Missing HTTP request.");
                        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                        var response = respond(first.Split(' ')[1]);
                        var body = Encoding.UTF8.GetBytes(response.Body);
                        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {response.Status} Test\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header, _stop.Token);
                        await stream.WriteAsync(body, _stop.Token);
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            });
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _loop;
            _listener.Stop();
            _stop.Dispose();
        }
    }
}
