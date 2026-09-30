using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ScreenAssistant.Services;

/// <summary>
/// Grabs the full virtual desktop (all monitors) into a bitmap using GDI
/// BitBlt via <see cref="Graphics.CopyFromScreen(Point, Point, Size)"/>.
/// Fast and dependency-free; can be swapped for Windows.Graphics.Capture later.
/// </summary>
public sealed class CaptureService
{
    /// <summary>
    /// Captures the whole virtual screen. Optionally downscales so the longest
    /// edge is at most <paramref name="maxEdge"/> (0 = no downscale) to speed OCR.
    /// Caller owns the returned bitmap and must Dispose it.
    /// </summary>
    public Bitmap CaptureVirtualScreen(int maxEdge = 0)
    {
        var bounds = SystemInformation.VirtualScreen;

        var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(full))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        if (maxEdge <= 0)
            return full;

        int longest = Math.Max(full.Width, full.Height);
        if (longest <= maxEdge)
            return full;

        double scale = (double)maxEdge / longest;
        int w = Math.Max(1, (int)(full.Width * scale));
        int h = Math.Max(1, (int)(full.Height * scale));

        var scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(full, 0, 0, w, h);
        }
        full.Dispose();
        return scaled;
    }
}
