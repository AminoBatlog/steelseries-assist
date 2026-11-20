namespace SteelSeriesAssist.App;

/// <summary>
/// Minimal surface panel placement needs from a window. Keeping it separate from WPF lets
/// the show/position flow run without a real window and be covered by the test project.
/// </summary>
public interface IShellWindow
{
    /// <summary>Native window handle; <see cref="IntPtr.Zero"/> until the window is shown.</summary>
    IntPtr Handle { get; }

    bool IsVisible { get; }

    void ShowSafely();

    void ActivateSafely();

    void ApplyPanelBounds(PanelBounds bounds);
}
