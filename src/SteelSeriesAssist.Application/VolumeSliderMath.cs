namespace SteelSeriesAssist.Application;

public static class VolumeSliderMath
{
    public static double ValueFromTrackPosition(
        double position,
        double trackWidth,
        double thumbWidth,
        double minimum,
        double maximum)
    {
        if (trackWidth <= 0 || thumbWidth < 0 || maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(trackWidth));
        }

        var availableWidth = Math.Max(1, trackWidth - thumbWidth);
        var ratio = Math.Clamp((position - (thumbWidth / 2)) / availableWidth, 0, 1);
        return minimum + (ratio * (maximum - minimum));
    }
}
