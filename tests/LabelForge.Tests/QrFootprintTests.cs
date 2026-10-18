using LabelForge.Core.Model;
using LabelForge.Core.Rendering;
using LabelForge.Core.Zpl;
using SkiaSharp;

namespace LabelForge.Tests;

/// <summary>
/// A QR code's size is its symbol version, which depends on the data, the error
/// correction level and the encoding, not on the character count alone. The footprint has
/// to match what the renderer draws for every level, past version 10 and with accented
/// text, because the outline, the quiet zone and a continuous label's length all read it.
/// </summary>
public sealed class QrFootprintTests
{
    public static TheoryData<string, QrErrorCorrection> Cases()
    {
        var cases = new TheoryData<string, QrErrorCorrection>();
        string[] data =
        [
            "HELLO", "https://example.com/abc?x=1", new string('a', 100),
            new string('7', 300), new string('Z', 250), "Ação é ótima",
        ];
        foreach (string d in data)
        {
            foreach (QrErrorCorrection level in Enum.GetValues<QrErrorCorrection>())
            {
                cases.Add(d, level);
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Footprint_IsTheRenderedSymbol(string data, QrErrorCorrection level)
    {
        var qr = new QrCodeElement
        {
            X = 10, Y = 10, Data = data, Magnification = 2, ErrorCorrection = level,
        };
        var document = new LabelDocument { WidthMm = 150, HeightMm = 150, Dpmm = 8 };
        document.Elements.Add(qr);

        DotRect footprint = new ElementBoundsCalculator().GetBounds(qr);
        DotRect ink = Ink(new ZplGenerator().GeneratePreview(document, 0));

        Assert.Equal(ink, footprint);
    }

    private static DotRect Ink(string zpl)
    {
        RenderResult result = new BinaryKitsRenderer().Render(zpl, 150, 150, 8);
        Assert.Empty(result.Errors);

        using SKBitmap? bitmap = SKBitmap.Decode(result.Png);
        Assert.NotNull(bitmap);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red >= 128)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < 0 ? default : new DotRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
