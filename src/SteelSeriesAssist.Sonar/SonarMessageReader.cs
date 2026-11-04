using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace SteelSeriesAssist.Sonar;

internal static class SonarMessageReader
{
    // These are control events, not EQ catalogues or audio. Limit both accumulated
    // bytes and the lifetime of an unfinished message, without timing out idle sockets.
    internal const int MaximumMessageBytes = 1024 * 1024;

    internal static async Task<string?> ReadAsync(WebSocket socket, CancellationToken cancellationToken,
        int maximumBytes = MaximumMessageBytes, TimeSpan? fragmentTimeout = null)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        using var fragmentedMessage = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var message = new MemoryStream();
        var total = 0;
        var started = false;
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), fragmentedMessage.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.Count > maximumBytes - total)
                    throw new InvalidDataException("Sonar event exceeds the message size limit.");
                total += result.Count;
                if (result.MessageType == WebSocketMessageType.Text)
                    message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                    return result.MessageType == WebSocketMessageType.Text
                        ? Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length) : string.Empty;
                if (!started)
                {
                    started = true;
                    fragmentedMessage.CancelAfter(fragmentTimeout ?? TimeSpan.FromSeconds(10));
                }
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
