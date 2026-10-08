using System.Collections.Concurrent;
using ZXing;
using ZXing.QrCode.Internal;

namespace LabelForge.Core.Model;

/// <summary>
/// How many modules a QR code is on a side, worked out by the same encoder the preview
/// draws with (ZXing, through BinaryKits) and with the same options: UTF-8 and the error
/// correction level the field states. The symbol version depends on the data, the level
/// and the encoding, so a capacity table read off the character count was short by up to
/// sixteen modules at the higher levels and stopped at version 10.
/// </summary>
internal static class QrSymbol
{
    /// <summary>Above this many entries the cache starts over. Bounds are asked for on
    /// every paint and hit test, so an answer is kept, but a long session typing into a
    /// field must not keep every intermediate string.</summary>
    private const int CacheLimit = 256;

    private static readonly ConcurrentDictionary<(string Data, QrErrorCorrection Level), int> Cache = new();

    private static readonly Dictionary<EncodeHintType, object> Hints = new()
    {
        [EncodeHintType.CHARACTER_SET] = "UTF-8",
    };

    /// <summary>Modules on one side of the symbol for this data and level.</summary>
    public static int Modules(string data, QrErrorCorrection level)
    {
        data ??= string.Empty;
        if (Cache.TryGetValue((data, level), out int cached))
        {
            return cached;
        }

        int modules = Measure(data, level);
        if (Cache.Count >= CacheLimit)
        {
            Cache.Clear();
        }

        Cache[(data, level)] = modules;
        return modules;
    }

    private static int Measure(string data, QrErrorCorrection level)
    {
        try
        {
            return Encoder.encode(data, ToZxing(level), Hints).Version.DimensionForVersion;
        }
        catch (WriterException)
        {
            // Empty data, or more than version 40 holds: the renderer draws nothing for
            // either, so the outline keeps the smallest and the largest symbol.
            return data.Length == 0 ? 21 : 177;
        }
    }

    private static ErrorCorrectionLevel ToZxing(QrErrorCorrection level) => level switch
    {
        QrErrorCorrection.Low => ErrorCorrectionLevel.L,
        QrErrorCorrection.Quartile => ErrorCorrectionLevel.Q,
        QrErrorCorrection.High => ErrorCorrectionLevel.H,
        _ => ErrorCorrectionLevel.M,
    };
}
