using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SteelSeriesAssist.App;

internal static class DarkWindowFrame
{
    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = 1;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) < 0)
            _ = DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));

        // Ask DWM to round the frame and retain the system shadow.
        // Older Windows versions ignore unsupported attributes.
        var round = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
        UpdateColors(window);
        window.Activated += (_, _) => UpdateColors(window);
        window.Deactivated += (_, _) => UpdateColors(window);
    }

    private static void UpdateColors(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        // COLORREF is 0x00BBGGRR. Match the content background (#171F24)
        // so the native caption and panel read as one surface.
        SetColor(handle, 35, 0x00241F17);
        SetColor(handle, 36, window.IsActive ? 0x00D5C5BB : 0x009E9388);
        SetColor(handle, 34, window.IsActive ? 0x004D453A : 0x00383229);
    }

    private static void SetColor(IntPtr handle, int attribute, int color) =>
        _ = DwmSetWindowAttribute(handle, attribute, ref color, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
