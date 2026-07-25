namespace LabelForge.Core.Imaging;

/// <summary>
/// Builds ZPL's two graphic commands around a <see cref="GraphicField"/> payload:
/// ^GF for a graphic inlined in the label, ~DG for one downloaded to printer memory
/// and recalled by name with ^XG. Both declare the same byte counts, so the sizes are
/// computed once here and the codec stays the only place that knows the data format.
/// </summary>
public static class ZplImageEncoder
{
    /// <summary>Encodes a row-major black mask (true = black) as a complete ^GFA
    /// command, without a field origin (the caller prepends ^FO).</summary>
    public static string EncodeGfa(bool[] black, int width, int height)
    {
        (string data, int totalBytes, int bytesPerRow) = Pack(black, width, height);
        return $"^GFA,{totalBytes},{totalBytes},{bytesPerRow},{data}^FS";
    }

    /// <summary>Encodes the same mask as a ~DG download under <paramref name="name"/>,
    /// which <see cref="RecallGraphic"/> then places. The graphic lives in the printer's
    /// DRAM until it is power-cycled or the name is downloaded again, so a name is only
    /// ever reused for identical bits.</summary>
    public static string EncodeDownload(string name, bool[] black, int width, int height)
    {
        (string data, int totalBytes, int bytesPerRow) = Pack(black, width, height);
        return $"~DG{StorageName(name)},{totalBytes},{bytesPerRow},{data}";
    }

    /// <summary>The ^XG field that draws a downloaded graphic at its stored size.</summary>
    public static string RecallGraphic(string name) => $"^XG{StorageName(name)},1,1^FS";

    /// <summary>
    /// The fully qualified storage path for a graphic we download: DRAM, .GRF.
    ///
    /// A Zebra printer defaults an unqualified name to exactly this, so "LFG0" would
    /// print the same. Our offline renderer does not: BinaryKits 1.3.1 stores and looks
    /// graphics up by the literal string, so a bare ~DGLFG0 followed by ^XGLFG0 draws
    /// nothing at all. Writing the long form keeps the canvas and the printer agreed,
    /// and it is the form Zebra's own documentation uses.
    /// </summary>
    public static string StorageName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return $"R:{name}.GRF";
    }

    private static (string Data, int TotalBytes, int BytesPerRow) Pack(
        bool[] black, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(black);
        if (width <= 0 || height <= 0 || black.Length != width * height)
        {
            throw new ArgumentException("Mask does not match the given size.", nameof(black));
        }

        int bytesPerRow = (width + 7) / 8;
        return (GraphicField.Encode(black, width, height), bytesPerRow * height, bytesPerRow);
    }
}
