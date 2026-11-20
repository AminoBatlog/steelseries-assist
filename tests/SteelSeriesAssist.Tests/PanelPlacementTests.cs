using System.Windows.Forms;

namespace SteelSeriesAssist.App;

/// <summary>Placement-only window shell so the show/position flow can be tested without WPF.</summary>
internal sealed class FakeShellWindow(IntPtr? initialHandle = null) : IShellWindow
{
    public IntPtr Handle
    {
        get
        {
            if (!IsVisible) HandleQueriesBeforeShow++;
            else HandleQueriesAfterShow++;
            return HandleCreated ? new IntPtr(0x1234) : initialHandle ?? IntPtr.Zero;
        }
    }

    public int HandleQueriesBeforeShow { get; private set; }

    public int HandleQueriesAfterShow { get; private set; }

    public bool IsVisible { get; private set; }

    public int ShowCount { get; private set; }

    public int ActivateCount { get; private set; }

    public PanelBounds? AppliedBounds { get; private set; }

    public void ShowSafely()
    {
        ShowCount++;
        IsVisible = true;
        if (!HandleCreated)
        {
            CreateHandleOnShow();
        }
    }

    public void ActivateSafely() => ActivateCount++;

    public void ApplyPanelBounds(PanelBounds bounds) => AppliedBounds = bounds;

    /// <summary>Mirrors WPF: showing the window is what creates the native handle.</summary>
    public bool HandleCreated { get; set; }

    public void CreateHandleOnShow() => HandleCreated = true;
}

internal static class PanelPlacementTests
{
    private static readonly PixelRect FullHd = new(0, 0, 1920, 1040);

    public static Task BuildsPlacementBeforeAnyShow()
    {
        Assert(PanelGeometry.Compute(FullHd, 96).Width == PanelGeometry.DesiredWidth,
            "100% DPI did not use the desired DIP width.");
        Assert(PanelGeometry.Compute(FullHd, 96).X == 1920 - 460 - 12,
            "100% DPI did not align the panel with the working-area margin.");
        return Task.CompletedTask;
    }

    public static Task ScalesWithMonitorDpi()
    {
        var scaled = PanelGeometry.Compute(FullHd, 144);
        Assert(Math.Abs(scaled.PixelWidth - 690) <= 1, "150% DPI did not scale the pixel width.");
        Assert(Math.Abs(scaled.Width - PanelGeometry.DesiredWidth) <= 1,
            "150% DPI did not keep the DIP width stable.");
        Assert(scaled.X + scaled.PixelWidth + (int)Math.Round(12 * 1.5) == 1920,
            "150% DPI did not keep the scaled right margin.");
        return Task.CompletedTask;
    }

    public static Task ClampsToSmallWorkAreas()
    {
        var small = PanelGeometry.Compute(new PixelRect(0, 0, 480, 420), 96);
        Assert(small.PixelWidth <= 480 - 24 && small.PixelHeight <= 420 - 24,
            "A small work area was not clamped.");
        var tiny = PanelGeometry.Compute(new PixelRect(0, 0, 10, 10), 96);
        Assert(tiny.PixelWidth >= 1 && tiny.PixelHeight >= 1, "A degenerate work area produced empty bounds.");
        return Task.CompletedTask;
    }

    public static Task ShowNeverPlacesBeforeHandleCreation()
    {
        var shell = new FakeShellWindow();
        PlaceAndVerify(shell);
        Assert(shell.ShowCount == 1, "The panel was not shown exactly once.");
        return Task.CompletedTask;
    }

    public static Task ShowWaitsUntilTheHandleExists()
    {
        var shell = new FakeShellWindow();
        PlaceAndVerify(shell);
        Assert(shell.AppliedBounds is not null, "Placement was skipped because the handle was not created first.");
        Assert(shell.ShowCount == 1, "The panel did not go through Show() before placement.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Guards the reported crash: placement used to call EnsureHandle, which creates the
    /// native HWND (and initializes the WPF stylus input stack) before Show(). The window
    /// shell must never be asked for a handle before it has been shown.
    /// </summary>
    public static Task PlacementNeverForcesAHandleBeforeShow()
    {
        var shell = new FakeShellWindow();
        PanelPresenter.Show(shell, atCursor: true);
        Assert(shell.HandleQueriesBeforeShow == 0, "The window handle was requested before Show().");
        Assert(shell.HandleQueriesAfterShow > 0, "Placement never read the handle after Show().");
        return Task.CompletedTask;
    }

    public static Task RepositionSkipsHiddenPanels()
    {
        var shell = new FakeShellWindow();
        PanelPresenter.Reposition(shell, atCursor: true);
        Assert(shell.AppliedBounds is null, "A hidden panel was repositioned.");
        shell.ShowSafely();
        PanelPresenter.Reposition(shell, atCursor: true);
        Assert(shell.AppliedBounds is not null, "A visible panel was not repositioned.");
        return Task.CompletedTask;
    }

    private static void PlaceAndVerify(FakeShellWindow shell)
    {
        PanelPresenter.Show(shell, atCursor: true);
        Assert(shell.ShowCount == 1, "Placement did not start with a normal Show().");
        Assert(shell.Handle != IntPtr.Zero, "Placement ran before the window handle existed.");
        var expected = PanelPlacement.ComputeBounds(true, CursorPosition());
        Assert(shell.AppliedBounds == expected,
            "The panel bounds did not match the computed placement for the cursor monitor.");
        Assert(expected.Width > 0 && expected.Height > 0 && expected.PixelWidth > 0,
            "Placement produced an empty panel size.");
    }

    private static System.Drawing.Point CursorPosition() => Cursor.Position;

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
