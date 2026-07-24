using SkiaSharp;

namespace LabelForge.Core.Export;

/// <summary>
/// Produces a single-page PDF with the rendered label image placed at its exact
/// physical size. Built on Skia's PDF backend rather than BinaryKits' DrawPdf,
/// whose scale factor is hardcoded for 8 dpmm and prints the wrong physical size
/// at 300/600 dpi.
/// </summary>
public static class PdfExporter
{
    private const double PointsPerMm = 72.0 / 25.4;

    /// <param name="cornerRadiusMm">Die-cut corner radius. When set, the page is clipped
    /// to the label's real shape: a PDF is the sheet someone approves a layout on, and it
    /// should not show ink in a corner the physical label does not have.</param>
    public static byte[] FromPng(byte[] png, double widthMm, double heightMm, double cornerRadiusMm = 0)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length == 0)
        {
            throw new ArgumentException("There is no rendered image to export.", nameof(png));
        }

        if (widthMm <= 0 || heightMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthMm), "Label size must be positive.");
        }

        using SKBitmap? bitmap = SKBitmap.Decode(png);
        if (bitmap is null)
        {
            throw new ArgumentException("The image bytes are not a decodable bitmap.", nameof(png));
        }

        float widthPt = (float)(widthMm * PointsPerMm);
        float heightPt = (float)(heightMm * PointsPerMm);

        using var stream = new MemoryStream();
        using (SKDocument document = SKDocument.CreatePdf(stream))
        {
            SKCanvas canvas = document.BeginPage(widthPt, heightPt);
            SKRect page = SKRect.Create(widthPt, heightPt);

            if (cornerRadiusMm > 0)
            {
                double radiusMm = Math.Clamp(cornerRadiusMm, 0, Math.Min(widthMm, heightMm) / 2);
                float radiusPt = (float)(radiusMm * PointsPerMm);
                canvas.ClipRoundRect(new SKRoundRect(page, radiusPt, radiusPt), antialias: true);
            }

            canvas.DrawBitmap(bitmap, page);
            document.EndPage();
            document.Close();
        }

        return stream.ToArray();
    }
}
