namespace SteelSeriesAssist.App;

/// <summary>Rectangle in physical (native) pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

/// <summary>Position and size for the panel, in both native pixels and WPF DIPs.</summary>
public readonly record struct PanelBounds(
    int X,
    int Y,
    int PixelWidth,
    int PixelHeight,
    double Width,
    double Height);

/// <summary>
/// Pure layout math for the tray panel. Kept away from WPF and Win32 so the
/// placement rules can be verified by the test project.
/// </summary>
public static class PanelGeometry
{
    public const double DesiredWidth = 460;
    public const double DesiredHeight = 720;
    private const double BaseMargin = 12;

    public static PanelBounds Compute(PixelRect workArea, uint dpi)
    {
        var scale = dpi / 96d;
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            scale = 1;
        }

        var margin = (int)Math.Round(BaseMargin * scale);
        var width = Math.Min((int)Math.Round(DesiredWidth * scale), Math.Max(1, workArea.Width - 2 * margin));
        var height = Math.Min((int)Math.Round(DesiredHeight * scale), Math.Max(1, workArea.Height - 2 * margin));
        // Screen coordinates and SetWindowPos use physical pixels. WPF Width/Height
        // use DIPs; do not mix the two on scaled or secondary monitors.
        return new PanelBounds(
            workArea.Right - width - margin,
            workArea.Bottom - height - margin,
            width,
            height,
            width / scale,
            height / scale);
    }
}
