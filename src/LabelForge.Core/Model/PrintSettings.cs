namespace LabelForge.Core.Model;

/// <summary>
/// Job-level print settings stored on the document. Zero means "leave the printer's
/// configured default" for darkness and speed, so a fresh document adds nothing to
/// the ZPL; only deliberate choices are emitted.
/// </summary>
public sealed class PrintSettings
{
    /// <summary>Copies to print (^PQ). Emitted only when greater than 1.</summary>
    public int Copies { get; set; } = 1;

    /// <summary>Darkness adjustment (^MD), -30 to 30; 0 keeps the printer default.</summary>
    public int DarknessDelta { get; set; }

    /// <summary>Print speed in inches per second (^PR), 2 to 14; 0 keeps the printer
    /// default.</summary>
    public int SpeedIps { get; set; }
}
