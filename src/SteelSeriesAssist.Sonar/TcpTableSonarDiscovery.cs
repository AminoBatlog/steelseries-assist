using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SteelSeriesAssist.Sonar;

public sealed class TcpTableSonarDiscovery : ISonarDiscovery
{
    private readonly TimeSpan _requestTimeout;

    public TcpTableSonarDiscovery(TimeSpan? requestTimeout = null)
    {
        _requestTimeout = requestTimeout ?? TimeSpan.FromMilliseconds(750);
    }

    public async Task<SonarEndpoint?> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var processIds = Process.GetProcessesByName("SteelSeriesSonar")
            .Select(process => process.Id)
            .ToHashSet();
        if (processIds.Count == 0)
        {
            return null;
        }

        var candidates = GetListeningPorts()
            .Where(entry => processIds.Contains(entry.ProcessId))
            .OrderBy(entry => entry.Port)
            .ToArray();

        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = _requestTimeout };
        foreach (var candidate in candidates)
        {
            var baseAddress = new Uri($"http://127.0.0.1:{candidate.Port}");
            try
            {
                using var response = await client.GetAsync(new Uri(baseAddress, "/mode"), cancellationToken)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var mode = JsonSerializer.Deserialize<string>(payload);
                if (mode is "classic" or "stream")
                {
                    return new SonarEndpoint(baseAddress, "windows-tcp-table", candidate.ProcessId);
                }
            }
            catch (HttpRequestException)
            {
                // Another listener owned by the process was not the Sonar HTTP service.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Probe timeout; continue with the next listener.
            }
        }

        return null;
    }

    internal static IReadOnlyList<ListeningPort> GetListeningPorts()
    {
        var bufferLength = 0;
        var result = GetExtendedTcpTable(
            IntPtr.Zero,
            ref bufferLength,
            true,
            AddressFamily.InterNetwork,
            TcpTableClass.TcpTableOwnerPidListener,
            0);
        if (result is not ErrorInsufficientBuffer)
        {
            throw new Win32Exception((int)result);
        }

        var buffer = Marshal.AllocHGlobal(bufferLength);
        try
        {
            result = GetExtendedTcpTable(
                buffer,
                ref bufferLength,
                true,
                AddressFamily.InterNetwork,
                TcpTableClass.TcpTableOwnerPidListener,
                0);
            if (result != 0)
            {
                throw new Win32Exception((int)result);
            }

            var count = Marshal.ReadInt32(buffer);
            var rowPointer = IntPtr.Add(buffer, sizeof(int));
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var ports = new List<ListeningPort>(count);
            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPointer);
                var networkPort = unchecked((short)(row.LocalPort & 0xffff));
                var port = unchecked((ushort)IPAddress.NetworkToHostOrder(networkPort));
                ports.Add(new ListeningPort(port, unchecked((int)row.OwningPid)));
                rowPointer = IntPtr.Add(rowPointer, rowSize);
            }

            return ports;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal sealed record ListeningPort(int Port, int ProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    private enum TcpTableClass
    {
        TcpTableOwnerPidListener = 3
    }

    private const uint ErrorInsufficientBuffer = 122;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        AddressFamily ipVersion,
        TcpTableClass tableClass,
        uint reserved);
}
