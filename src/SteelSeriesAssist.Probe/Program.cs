using System.Text.Json;
using SteelSeriesAssist.Application;
using SteelSeriesAssist.Sonar;

var discovery = new CompositeSonarDiscovery([
    new GgCoreDiscovery(),
    new TcpTableSonarDiscovery()
]);
var service = new SonarStateService(discovery);

try
{
    var (endpoint, snapshot) = await service.LoadAsync();
    object? writeVerification = null;
    if (args.Contains("--verify-current-writes", StringComparer.OrdinalIgnoreCase))
    {
        using var client = new SonarClient(endpoint.BaseAddress);
        var gameVolume = snapshot.Volumes.Single(volume => volume.Channel == "game");
        var confirmedVolume = await client.SetChannelVolumeAsync("game", gameVolume.State.Volume);
        var confirmedMute = await client.SetChannelMutedAsync("game", gameVolume.State.Muted);

        var gameBinding = snapshot.Bindings.Single(binding => binding.Channel == "game");
        var confirmedBinding = await client.SetChannelBindingAsync("game", gameBinding.DeviceId);

        var route = snapshot.DeviceSessions
            .Where(device => device.DataFlow == "render")
            .SelectMany(device => device.Sessions
                .Where(session => session.State == "active" && !session.IsSystemSound && session.ProcessId > 0)
                .Select(session => new { Device = device, Session = session }))
            .FirstOrDefault();
        if (route is not null)
        {
            await client.RouteApplicationAsync("render", route.Device.DeviceId, route.Session.ProcessId);
        }

        writeVerification = new
        {
            volumeRoundTrip = confirmedVolume == gameVolume.State,
            muteRoundTrip = confirmedMute == gameVolume.State,
            bindingRoundTrip = confirmedBinding.DeviceId == gameBinding.DeviceId,
            applicationRouteAccepted = route is not null,
            note = "Only existing values and routes were written back."
        };
    }

    var report = new
    {
        sonar = new
        {
            online = true,
            endpoint.DiscoveryMethod,
            endpoint.BaseAddress,
            snapshot.Mode,
            snapshot.CapturedAt
        },
        volumes = snapshot.Volumes.Select(volume => new
        {
            volume.Channel,
            percent = (int)Math.Round(volume.State.Volume * 100),
            volume.State.Muted
        }),
        devices = snapshot.Devices
            .Where(device => device.State == "active")
            .Select(device => new
            {
                device.FriendlyName,
                device.DataFlow,
                device.Role,
                device.IsVirtual
            }),
        bindings = snapshot.Bindings.Select(binding => new
        {
            binding.Channel,
            device = snapshot.Devices.FirstOrDefault(device => device.Id == binding.DeviceId)?.FriendlyName ?? "Unavailable",
            binding.IsRunning
        }),
        applications = snapshot.DeviceSessions
            .SelectMany(route => route.Sessions.Select(session => new
            {
                session.DisplayName,
                route.Role,
                route.DataFlow,
                session.State,
                session.RoutingErrorDetected
            }))
            .Distinct(),
        writeVerification
    };

    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Sonar probe failed: {exception.Message}");
    return 1;
}
