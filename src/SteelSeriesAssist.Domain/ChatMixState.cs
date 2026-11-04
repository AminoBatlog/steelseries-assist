namespace SteelSeriesAssist.Domain;

public sealed record ChatMixState(float Balance, string State)
{
    public bool CanAdjust => State == "enabled";
}
