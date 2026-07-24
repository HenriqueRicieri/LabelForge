using System.Text;

namespace LabelForge.Core.Io;

/// <summary>The result of decoding a ZPL file's bytes.</summary>
/// <param name="Text">The decoded ZPL.</param>
/// <param name="EncodingName">Which encoding produced it, for display.</param>
/// <param name="Recovered">True only when the bytes were not valid UTF-8 and were read
/// as legacy single-byte text. That is the one case where the encoding was inferred
/// rather than stated, so the caller should say so rather than pretend to be sure.</param>
public readonly record struct ZplTextRead(string Text, string EncodingName, bool Recovered);

/// <summary>
/// Reads and writes ZPL as bytes, deliberately, because a label's encoding is not a
/// detail worth letting a default guess at.
///
/// Writing is always UTF-8 with no byte order mark. Our generator declares ^CI28, so
/// UTF-8 is what the printer is told to expect, and a BOM would place three bytes in
/// front of ^XA that a printer has no reason to tolerate.
///
/// Reading is tolerant, because real ZPL arrives from wherever it was produced. A byte
/// order mark is an explicit statement and wins. Otherwise the bytes are decoded as
/// strict UTF-8, and only when that fails are they read as Latin-1, which cannot fail
/// and preserves the accented range a Brazilian label lives in. The alternative,
/// .NET's default lenient UTF-8, turns every accented character of a legacy file into
/// U+FFFD, which is worse than either answer because nothing downstream can tell it
/// happened.
/// </summary>
public static class ZplTextFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly UTF8Encoding LenientUtf8 = new(
        encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public static ZplTextRead Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // A byte order mark states the encoding; take it at its word. A BOM-marked
        // file that is then malformed is decoded leniently: the file said UTF-8, so
        // guessing something else would be second-guessing an explicit declaration.
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            return new ZplTextRead(
                LenientUtf8.GetString(bytes, 3, bytes.Length - 3), "UTF-8", Recovered: false);
        }

        if (HasPrefix(bytes, 0xFF, 0xFE))
        {
            return new ZplTextRead(
                Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 LE", Recovered: false);
        }

        if (HasPrefix(bytes, 0xFE, 0xFF))
        {
            return new ZplTextRead(
                Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 BE", Recovered: false);
        }

        try
        {
            return new ZplTextRead(StrictUtf8.GetString(bytes), "UTF-8", Recovered: false);
        }
        catch (DecoderFallbackException)
        {
            // Every byte maps to a character in Latin-1, so this branch cannot fail.
            return new ZplTextRead(Encoding.Latin1.GetString(bytes), "Latin-1", Recovered: true);
        }
    }

    /// <summary>The bytes to write to a file, a socket, or a spooler: UTF-8, no BOM.</summary>
    public static byte[] ToBytes(string zpl)
    {
        ArgumentNullException.ThrowIfNull(zpl);
        return LenientUtf8.GetBytes(zpl);
    }

    public static ZplTextRead ReadFile(string path) => Read(File.ReadAllBytes(path));

    public static async Task<ZplTextRead> ReadFileAsync(
        string path, CancellationToken cancellationToken = default) =>
        Read(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));

    public static Task WriteFileAsync(
        string path, string zpl, CancellationToken cancellationToken = default) =>
        File.WriteAllBytesAsync(path, ToBytes(zpl), cancellationToken);

    private static bool HasPrefix(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (int i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }
}
