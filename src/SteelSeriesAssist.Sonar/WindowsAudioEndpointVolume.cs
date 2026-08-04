using System.Runtime.InteropServices;
using SteelSeriesAssist.Domain;

namespace SteelSeriesAssist.Sonar;

public sealed class WindowsAudioEndpointVolume
{
    private readonly object _sync = new();
    private IReadOnlyDictionary<string, string> _channelEndpoints = new Dictionary<string, string>();

    public void UpdateDevices(IReadOnlyList<AudioDevice> devices)
    {
        var endpoints = new Dictionary<string, string>();
        foreach (var channel in new[] { "game", "chatRender", "media", "aux", "chatCapture" })
        {
            var id = FindEndpointId(channel, devices);
            if (id is not null)
            {
                endpoints[channel] = id;
            }
        }

        lock (_sync)
        {
            _channelEndpoints = endpoints;
        }
    }

    public bool TrySetVolume(string channel, float volume, out VolumeState state)
    {
        if (volume is < 0 or > 1 || float.IsNaN(volume))
        {
            throw new ArgumentOutOfRangeException(nameof(volume));
        }

        if (!TryGetEndpoint(channel, out var endpointId))
        {
            state = default!;
            return false;
        }

        state = UseEndpoint(endpointId, endpoint =>
        {
            Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(volume, Guid.Empty));
            Marshal.ThrowExceptionForHR(endpoint.SetMute(volume <= 0f, Guid.Empty));
            Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var confirmed));
            Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
            return new VolumeState(confirmed, muted);
        });
        return true;
    }

    public bool TrySetMuted(string channel, bool muted, out VolumeState state)
    {
        if (!TryGetEndpoint(channel, out var endpointId))
        {
            state = default!;
            return false;
        }

        state = UseEndpoint(endpointId, endpoint =>
        {
            Marshal.ThrowExceptionForHR(endpoint.SetMute(muted, Guid.Empty));
            Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var volume));
            Marshal.ThrowExceptionForHR(endpoint.GetMute(out var confirmed));
            return new VolumeState(volume, confirmed);
        });
        return true;
    }

    internal static string? FindEndpointId(string channel, IReadOnlyList<AudioDevice> devices)
    {
        var dataFlow = channel == "chatCapture" ? "capture" : "render";
        return devices.FirstOrDefault(device =>
            device.IsVirtual &&
            device.State == "active" &&
            device.Role == channel &&
            device.DataFlow == dataFlow)?.Id;
    }

    private bool TryGetEndpoint(string channel, out string endpointId)
    {
        lock (_sync)
        {
            return _channelEndpoints.TryGetValue(channel, out endpointId!);
        }
    }

    private static T UseEndpoint<T>(string endpointId, Func<IAudioEndpointVolume, T> action)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows audio endpoints are only available on Windows.");
        }

        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? instance = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(endpointId, out device));
            var interfaceId = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref interfaceId, ClsCtxAll, IntPtr.Zero, out instance));
            return action((IAudioEndpointVolume)instance);
        }
        finally
        {
            if (instance is not null)
            {
                Marshal.FinalReleaseComObject(instance);
            }

            if (device is not null)
            {
                Marshal.FinalReleaseComObject(device);
            }

            if (enumerator is not null)
            {
                Marshal.FinalReleaseComObject(enumerator);
            }
        }
    }

    private const int ClsCtxAll = 23;

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out object devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid interfaceId,
            int classContext,
            IntPtr activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
