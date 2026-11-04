using SteelSeriesAssist.Domain;

namespace SteelSeriesAssist.Application;

public static class SonarChannelAvailability
{
    // Sonar keeps saved volume/binding settings after optional virtual devices are disabled.
    public static bool IsVisible(string channel, IReadOnlyList<AudioDevice> devices) =>
        channel is not ("media" or "aux") || devices.Any(device =>
            device.IsVirtual && device.DataFlow == "render" &&
            device.State == "active" && device.Role == channel);
}
