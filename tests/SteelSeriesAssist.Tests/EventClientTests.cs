using System.Net.WebSockets;
using System.Text;
using SteelSeriesAssist.Sonar;

internal static class EventClientTests
{
    public static async Task RestartAndDispose()
    {
        var client = new SonarEventClient();
        await Task.WhenAll(Enumerable.Range(0, 300).Select(_ => client.StartAsync(new Uri("http://127.0.0.1:1"))));
        await Task.WhenAll(client.DisposeAsync().AsTask(), client.DisposeAsync().AsTask());
        if (client.IsConnected) throw new Exception("Disposed client remained connected.");
        try { await client.StartAsync(new Uri("http://127.0.0.1:1")); }
        catch (ObjectDisposedException) { return; }
        throw new Exception("A disposed client restarted.");
    }

    public static async Task FragmentBoundaries()
    {
        using var socket = new ScriptedSocket([(Encoding.UTF8.GetBytes("ab"), false), (Encoding.UTF8.GetBytes("cd"), true)]);
        var text = await SonarMessageReader.ReadAsync(socket, default, maximumBytes: 4);
        if (text != "abcd") throw new Exception("Fragmented event at the exact size limit was lost.");
        using var oversized = new ScriptedSocket([(new byte[3], false), (new byte[2], true)]);
        try { await SonarMessageReader.ReadAsync(oversized, default, maximumBytes: 4); }
        catch (InvalidDataException) { return; }
        throw new Exception("Oversized fragmented event was accepted.");
    }

    public static async Task FragmentTimeoutAndIdle()
    {
        using var unfinished = new ScriptedSocket([(new byte[1], false)]);
        try { await SonarMessageReader.ReadAsync(unfinished, default, fragmentTimeout: TimeSpan.FromMilliseconds(30)); }
        catch (OperationCanceledException)
        {
            using var idle = new ScriptedSocket([(Encoding.UTF8.GetBytes("ok"), true)], firstDelay: TimeSpan.FromMilliseconds(100));
            var text = await SonarMessageReader.ReadAsync(idle, default, fragmentTimeout: TimeSpan.FromMilliseconds(20));
            if (text != "ok") throw new Exception("Idle socket was treated as an unfinished message.");
            return;
        }
        throw new Exception("Unfinished message never timed out.");
    }

    public static async Task ReaderCancellation()
    {
        using var socket = new ScriptedSocket([]);
        using var cancel = new CancellationTokenSource(30);
        try { await SonarMessageReader.ReadAsync(socket, cancel.Token); }
        catch (OperationCanceledException) { return; }
        throw new Exception("Receive ignored cancellation.");
    }

    private sealed class ScriptedSocket(IEnumerable<(byte[] Data, bool End)> frames, TimeSpan? firstDelay = null) : WebSocket
    {
        private readonly Queue<(byte[] Data, bool End)> _frames = new(frames);
        private bool _first = true;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_first && firstDelay.HasValue) await Task.Delay(firstDelay.Value, cancellationToken);
            _first = false;
            if (_frames.Count == 0) await Task.Delay(Timeout.Infinite, cancellationToken);
            var frame = _frames.Dequeue();
            frame.Data.CopyTo(buffer.Array!, buffer.Offset);
            return new WebSocketReceiveResult(frame.Data.Length, WebSocketMessageType.Text, frame.End);
        }
    }
}
