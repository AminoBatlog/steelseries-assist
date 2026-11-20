using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace SteelSeriesAssist.App;

public static class PanelPlacement
{
    /// <summary>
    /// Moves the panel to the bottom-right corner of the working area. The window must
    /// already have a native handle; this method never creates one.
    /// </summary>
    public static void Place(IShellWindow window, bool atCursor)
    {
        if (window.Handle == IntPtr.Zero)
        {
            return;
        }

        window.ApplyPanelBounds(ComputeBounds(atCursor, Forms.Cursor.Position));
    }

    public static PanelBounds ComputeBounds(bool atCursor, System.Drawing.Point cursor)
    {
        var screen = Forms.Screen.FromPoint(cursor);
        var area = screen.WorkingArea;
        // MDT_EFFECTIVE_DPI from the monitor that owns the cursor.
        var monitor = MonitorFromPoint(new NativePoint(area.Left, area.Top), 2);
        var dpi = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX : 96u;
        return PanelGeometry.Compute(new PixelRect(area.Left, area.Top, area.Width, area.Height), dpi);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
