namespace SteelSeriesAssist.App;

/// <summary>
/// Shows the tray panel and keeps its bottom-right placement.
///
/// Placement must never run before the window exists. Forcing a native handle
/// (WindowInteropHelper.EnsureHandle) initializes the WPF input stack (HwndSource and the
/// pen/stylus provider) before normal window startup; on machines where that native
/// component is unavailable it throws DllNotFoundException and terminates the app.
/// Window.Show() creates the same handle through the regular startup path, which WPF
/// initializes defensively, so the panel is positioned only after it is visible.
/// </summary>
public static class PanelPresenter
{
    /// <param name="window">Panel window to show.</param>
    /// <param name="atCursor">
    /// True to place on the display that currently holds the mouse cursor, false to keep
    /// the display the panel is already on.
    /// </param>
    public static void Show(IShellWindow window, bool atCursor)
    {
        window.ShowSafely();
        PanelPlacement.Place(window, atCursor);
    }

    /// <summary>Re-places an already visible panel, e.g. after a monitor DPI change.</summary>
    public static void Reposition(IShellWindow window, bool atCursor)
    {
        if (window.IsVisible)
        {
            PanelPlacement.Place(window, atCursor);
        }
    }
}
