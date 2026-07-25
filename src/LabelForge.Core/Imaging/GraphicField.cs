using System.IO.Compression;
using System.Text;

namespace LabelForge.Core.Imaging;

/// <summary>A decoded 1-bit graphic, row-major, true = black (a set bit in the GRF).
/// Width is always a whole number of bytes: the printer stores rows byte-aligned and
/// the padding bits on the right are part of the stored graphic.</summary>
/// <param name="Black">Row-major mask of <paramref name="Width"/> * <paramref name="Height"/> entries.</param>
public sealed record GraphicBitmap(bool[] Black, int Width, int Height);

/// <summary>
/// The payload shared by ZPL's two graphic commands: ~DG (download to printer memory)
/// and ^GF (inline graphic field). Both carry the same bitmap as ASCII hex, so the
/// codec lives in one place and both directions are covered here.
///
/// Reading and writing are deliberately not symmetric.
///
/// Reading accepts the whole alternative compression scheme, because that is what real
/// driver output contains: repeat counts (G-Y for 1-19, g-z for 20-400, additive when
/// combined), "," to fill the rest of a row with white, "!" to fill it with black, and
/// ":" to repeat the previous row, plus the :Z64: (zlib) and :B64: base64 envelopes.
///
/// Writing uses only "," and ":". Zebra printers understand the rest, but our offline
/// renderer does not: BinaryKits 1.3.1 parses graphic data as raw hex and honours only
/// those two, silently drawing the wrong bitmap for "!" and throwing on a count prefix.
/// The canvas is the render of the ZPL we generate, so emitting a form the renderer
/// misreads would make the designer lie about what prints. The two characters we do
/// emit are the ones that matter on a logo anyway: blank rows and repeated rows are
/// most of it. <see cref="GraphicFieldTests"/> holds both halves of that claim.
/// </summary>
public static class GraphicField
{
    /// <summary>Compresses a black mask into the hex payload of a ~DG or ^GF command
    /// (the data only; the caller writes the command and its byte counts).</summary>
    public static string Encode(bool[] black, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(black);
        if (width <= 0 || height <= 0 || black.Length != width * height)
        {
            throw new ArgumentException("Mask does not match the given size.", nameof(black));
        }

        int bytesPerRow = (width + 7) / 8;
        var sb = new StringBuilder(bytesPerRow * 2 + 16);
        var row = new byte[bytesPerRow];
        var previous = new byte[bytesPerRow];
        bool hasPrevious = false;

        for (int y = 0; y < height; y++)
        {
            PackRow(black, width, y, row);
            if (hasPrevious && row.AsSpan().SequenceEqual(previous))
            {
                sb.Append(':');
            }
            else
            {
                AppendRow(sb, row);
            }

            row.CopyTo(previous, 0);
            hasPrevious = true;
        }

        return sb.ToString();
    }

    /// <summary>Expands a ~DG or ^GF payload back into a mask, or null when the sizes
    /// make no sense. Truncated or malformed data is padded with white rather than
    /// rejected: a partly readable logo beats no logo, and the caller can see the size.</summary>
    public static GraphicBitmap? Decode(string data, int bytesPerRow, int totalBytes)
    {
        if (data is null || bytesPerRow <= 0 || totalBytes <= 0)
        {
            return null;
        }

        int height = totalBytes / bytesPerRow;
        if (height <= 0)
        {
            return null;
        }

        byte[] bytes = TryDecodeBase64(data, bytesPerRow * height)
                       ?? DecodeHex(data, bytesPerRow, height);

        int width = bytesPerRow * 8;
        var black = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * width;
            int byteStart = y * bytesPerRow;
            for (int b = 0; b < bytesPerRow; b++)
            {
                byte value = bytes[byteStart + b];
                for (int bit = 0; bit < 8; bit++)
                {
                    black[rowStart + b * 8 + bit] = (value & (0x80 >> bit)) != 0;
                }
            }
        }

        return new GraphicBitmap(black, width, height);
    }

    private static void PackRow(bool[] black, int width, int y, byte[] row)
    {
        Array.Clear(row);
        int rowStart = y * width;
        for (int x = 0; x < width; x++)
        {
            if (black[rowStart + x])
            {
                row[x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }
    }

    private static void AppendRow(StringBuilder sb, byte[] row)
    {
        int nibbles = row.Length * 2;

        // Trailing white collapses to one character. Trailing black has the same
        // one-character form ("!") in the ZPL scheme, but the offline renderer
        // mishandles it, so those nibbles are written out (see the class remarks).
        int last = nibbles;
        while (last > 0 && NibbleAt(row, last - 1) == 0)
        {
            last--;
        }

        for (int i = 0; i < last; i++)
        {
            sb.Append(HexChar(NibbleAt(row, i)));
        }

        if (last < nibbles)
        {
            sb.Append(',');
        }
    }

    private static byte[] DecodeHex(string data, int bytesPerRow, int height)
    {
        int nibblesPerRow = bytesPerRow * 2;
        var bytes = new byte[bytesPerRow * height];
        int row = 0;
        int nibble = 0;
        int count = 0;

        void Put(int value)
        {
            int index = row * bytesPerRow + nibble / 2;
            if ((nibble & 1) == 0)
            {
                bytes[index] = (byte)(value << 4);
            }
            else
            {
                bytes[index] |= (byte)value;
            }

            nibble++;
        }

        foreach (char c in data)
        {
            if (row >= height)
            {
                break;
            }

            // Line breaks and indentation are formatting, not data.
            if (c <= ' ')
            {
                continue;
            }

            if (c is >= 'G' and <= 'Y')
            {
                count += c - 'G' + 1;
                continue;
            }

            if (c is >= 'g' and <= 'z')
            {
                count += (c - 'g' + 1) * 20;
                continue;
            }

            if (c is ',' or '!')
            {
                int fill = c == '!' ? 0xF : 0x0;
                while (nibble < nibblesPerRow)
                {
                    Put(fill);
                }

                row++;
                nibble = 0;
                count = 0;
                continue;
            }

            if (c == ':')
            {
                // Nothing to repeat on the first row, which then stays white.
                if (row > 0)
                {
                    Array.Copy(bytes, (row - 1) * bytesPerRow, bytes, row * bytesPerRow, bytesPerRow);
                }

                row++;
                nibble = 0;
                count = 0;
                continue;
            }

            int digit = HexValue(c);
            if (digit < 0)
            {
                // Not part of the scheme; drop it and any count it was carrying rather
                // than letting one stray byte shift every row that follows.
                count = 0;
                continue;
            }

            int repeat = Math.Max(count, 1);
            count = 0;
            for (int i = 0; i < repeat && row < height; i++)
            {
                Put(digit);
                if (nibble == nibblesPerRow)
                {
                    row++;
                    nibble = 0;
                }
            }
        }

        return bytes;
    }

    /// <summary>Unwraps the :Z64:/:B64: envelopes some drivers use, or null when the
    /// payload is plain hex. The result is padded or clipped to the declared size so a
    /// wrong byte count in the command cannot walk off the buffer.</summary>
    private static byte[]? TryDecodeBase64(string data, int totalBytes)
    {
        int start = data.IndexOf(":Z64:", StringComparison.OrdinalIgnoreCase);
        bool deflated = start >= 0;
        if (start < 0)
        {
            start = data.IndexOf(":B64:", StringComparison.OrdinalIgnoreCase);
        }

        if (start < 0)
        {
            return null;
        }

        int from = start + 5;

        // The envelope ends with ":<crc>"; without one the payload runs to the end.
        int end = data.LastIndexOf(':');
        string payload = end > from ? data[from..end] : data[from..];
        payload = string.Concat(payload.Where(ch => !char.IsWhiteSpace(ch)));

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return null;
        }

        if (deflated)
        {
            try
            {
                using var input = new MemoryStream(raw);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream(totalBytes);
                zlib.CopyTo(output);
                raw = output.ToArray();
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        if (raw.Length == totalBytes)
        {
            return raw;
        }

        var sized = new byte[totalBytes];
        raw.AsSpan(0, Math.Min(raw.Length, totalBytes)).CopyTo(sized);
        return sized;
    }

    private static int NibbleAt(byte[] row, int index) =>
        (index & 1) == 0 ? row[index / 2] >> 4 : row[index / 2] & 0x0F;

    private static char HexChar(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1,
    };
}
