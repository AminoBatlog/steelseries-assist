using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SteelSeriesAssist.App;

/// <summary>WPF implementation of <see cref="IShellWindow"/> for the tray panel.</summary>
internal sealed class WpfShellWindow(Window window) : IShellWindow
{
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public IntPtr Handle => new WindowInteropHelper(window).Handle;

    public bool IsVisible => window.IsVisible;

    public void ShowSafely()
    {
        if (!window.IsVisible)
        {
            window.Show();
        }
    }

    public void ActivateSafely()
    {
        if (window.IsVisible)
        {
            window.Activate();
        }
    }

    public void ApplyPanelBounds(PanelBounds bounds)
    {
        var handle = Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // WPF sizes are DIPs; SetWindowPos works in physical pixels.
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        _ = SetWindowPos(handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.PixelWidth, bounds.PixelHeight,
            SwpNoZOrder | SwpNoActivate);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height,
        uint flags);
}
