using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace SteelSeriesAssist.App;

internal static class PanelPlacement
{
    public static void Place(Window window, bool atCursor)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var screen = atCursor
            ? Forms.Screen.FromPoint(Forms.Cursor.Position)
            : Forms.Screen.FromHandle(handle);
        var area = screen.WorkingArea;
        var monitor = MonitorFromPoint(new NativePoint(area.Left, area.Top), 2);
        var dpi = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX : 96u;
        var scale = dpi / 96d;
        var margin = (int)Math.Round(12 * scale);
        var width = Math.Min((int)Math.Round(460 * scale), Math.Max(1, area.Width - 2 * margin));
        var height = Math.Min((int)Math.Round(720 * scale), Math.Max(1, area.Height - 2 * margin));
        window.Width = width / scale;
        window.Height = height / scale;
        // Screen coordinates and SetWindowPos use physical pixels. WPF Width/Height
        // use DIPs; do not mix the two on scaled or secondary monitors.
        SetWindowPos(handle, IntPtr.Zero, area.Right - width - margin,
            area.Bottom - height - margin, width, height, 0x0014);
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
