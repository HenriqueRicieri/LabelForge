using System.Text;

namespace LabelForge.Core.Imaging;

/// <summary>
/// Packs a 1-bit image into a ZPL ^GF (graphic field) command with plain ASCII hex
/// data: ^GFA,totalBytes,totalBytes,bytesPerRow,hex. Uncompressed hex keeps the
/// output deterministic and universally supported; compression is a later
/// optimization (see the backlog).
/// </summary>
public static class ZplImageEncoder
{
    /// <summary>Encodes a row-major black mask (true = black) as a complete ^GFA
    /// command, without a field origin (the caller prepends ^FO).</summary>
    public static string EncodeGfa(bool[] black, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(black);
        if (width <= 0 || height <= 0 || black.Length != width * height)
        {
            throw new ArgumentException("Mask does not match the given size.");
        }

        int bytesPerRow = (width + 7) / 8;
        int totalBytes = bytesPerRow * height;
        var sb = new StringBuilder(totalBytes * 2 + 32);
        sb.Append("^GFA,").Append(totalBytes).Append(',').Append(totalBytes)
          .Append(',').Append(bytesPerRow).Append(',');

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width;
            for (int b = 0; b < bytesPerRow; b++)
            {
                int value = 0;
                for (int bit = 0; bit < 8; bit++)
                {
                    int x = b * 8 + bit;
                    if (x < width && black[rowStart + x])
                    {
                        value |= 0x80 >> bit;
                    }
                }

                sb.Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        sb.Append("^FS");
        return sb.ToString();
    }
}
