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

    public static byte[] FromPng(byte[] png, double widthMm, double heightMm)
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
            canvas.DrawBitmap(bitmap, SKRect.Create(widthPt, heightPt));
            document.EndPage();
            document.Close();
        }

        return stream.ToArray();
    }
}
