using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SteelSeriesAssist.App;

internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var background = new SolidBrush(Color.FromArgb(255, 23, 31, 36));
        using var outline = new Pen(Color.FromArgb(255, 103, 117, 255), 1.8f);
        graphics.FillEllipse(background, 1.5f, 1.5f, 29, 29);
        graphics.DrawEllipse(outline, 2.4f, 2.4f, 27.2f, 27.2f);

        DrawFader(graphics, 9, 13, Color.FromArgb(255, 103, 117, 255));
        DrawFader(graphics, 16, 20, Color.FromArgb(255, 45, 177, 252));
        DrawFader(graphics, 23, 10, Color.FromArgb(255, 2, 221, 188));

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawFader(Graphics graphics, float x, float knobY, Color accent)
    {
        using var rail = new Pen(Color.FromArgb(220, 210, 219, 230), 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var knob = new SolidBrush(accent);
        using var knobOutline = new Pen(Color.FromArgb(255, 23, 31, 36), 1.1f);

        graphics.DrawLine(rail, x, 7, x, 25);
        graphics.FillEllipse(knob, x - 3.1f, knobY - 3.1f, 6.2f, 6.2f);
        graphics.DrawEllipse(knobOutline, x - 3.1f, knobY - 3.1f, 6.2f, 6.2f);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
