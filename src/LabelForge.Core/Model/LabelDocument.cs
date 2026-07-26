using System.Text.Json.Serialization;

namespace LabelForge.Core.Model;

/// <summary>
/// The label being designed: physical size in millimeters, a target print density,
/// and the ordered elements placed on it. This is the single source of truth; the
/// canvas edits it and the ZPL generator reads it.
/// </summary>
public sealed class LabelDocument
{
    private double _heightMm = 150;

    /// <summary>Physical label width in millimeters.</summary>
    public double WidthMm { get; set; } = 100;

    /// <summary>
    /// Physical label height in millimeters.
    ///
    /// On <see cref="IsContinuous"/> stock this is not a stored size at all: a
    /// continuous roll has no die cut, so the label is exactly as long as what is on it
    /// and the value is measured from the content every time it is read. Everything
    /// downstream keeps working unchanged because there is still only one height, which
    /// is the point: the canvas, the generated ^LL and the PDF cannot end up disagreeing
    /// about how long the label is.
    ///
    /// The assigned value is kept underneath, so turning continuous stock off restores a
    /// real die-cut height rather than freezing whatever the content happened to measure.
    /// </summary>
    public double HeightMm
    {
        get => IsContinuous ? ContinuousLength.Mm(this) : _heightMm;
        set => _heightMm = value;
    }

    /// <summary>
    /// True when the label is printed on continuous stock: a roll with no gaps, notches
    /// or die cuts, where the printer is told how far to advance rather than sensing it.
    ///
    /// This changes what the label is, not just how it is described. The height stops
    /// being a fixed size and becomes the content's own length, there is no bottom edge
    /// for an element to fall off, and the die-cut corner radius stops meaning anything
    /// because nothing is die cut.
    /// </summary>
    public bool IsContinuous { get; set; }

    /// <summary>
    /// Warn when a symbol's quiet zone is not clear. On by default: a barcode that needs
    /// a second pass to scan is a label that failed, and the crowding that causes it is
    /// invisible on a design where everything looks neatly packed.
    ///
    /// It only ever produces warnings. The quiet zone is blank stock, so nothing about
    /// this reaches the ZPL, and a label that deliberately crowds a symbol still prints
    /// exactly as drawn once the switch is off.
    /// </summary>
    public bool CheckQuietZones { get; set; } = true;

    /// <summary>Blank stock left after the last ink on continuous media, in millimeters.
    /// Without it the next label starts on the previous one's last row of dots, which is
    /// not a gap anyone can tear or cut along.</summary>
    public double ContinuousMarginMm { get; set; } = 3;

    /// <summary>Print density in dots per millimeter (8 = 203 dpi, 12 = 300, 24 = 600).</summary>
    public int Dpmm { get; set; } = 8;

    /// <summary>
    /// Die-cut corner radius in millimeters; 0 is a square-cornered label. Set from the
    /// media catalog or a saved preset, and editable by hand.
    ///
    /// This describes the physical stock, not the print. It shapes the canvas and the
    /// PDF so the design is checked against the label that actually exists, and it never
    /// reaches the ZPL, which has no notion of the label's outline.
    /// </summary>
    public double CornerRadiusMm { get; set; }

    public IList<Element> Elements { get; init; } = new List<Element>();

    /// <summary>Job-level print settings emitted with the label (quantity, darkness,
    /// speed). Part of the document so a label prints the same from any machine.</summary>
    public PrintSettings Print { get; init; } = new();

    /// <summary>Preview sample values per template variable name (##NAME## markers).
    /// Preview-only: exported and printed ZPL keeps the literal markers. Variables
    /// without an entry fall back to the default sample.</summary>
    public IDictionary<string, string> SampleValues { get; init; } =
        new Dictionary<string, string>();

    /// <summary>How each template variable is filled, by variable name. Only variables
    /// that are not plain external markers need an entry, so a document without counters
    /// or clocks serializes exactly as it did before they existed.</summary>
    public IDictionary<string, VariableDefinition> Variables { get; init; } =
        new Dictionary<string, VariableDefinition>();

    /// <summary>Vertical alignment guides: X positions in dots. Design aids only,
    /// never printed; they ride the undo/save pipeline like any document change.</summary>
    public IList<int> VerticalGuides { get; init; } = new List<int>();

    /// <summary>Horizontal alignment guides: Y positions in dots.</summary>
    public IList<int> HorizontalGuides { get; init; } = new List<int>();

    [JsonIgnore]
    public int WidthDots => Units.MmToDots(WidthMm, Dpmm);

    [JsonIgnore]
    public int HeightDots => Units.MmToDots(HeightMm, Dpmm);

    /// <summary>The radius that can actually be drawn. Past half the shorter side the
    /// corners would overlap, which is not a shape any die cuts, so the clamp lives here
    /// and every drawing surface reads it rather than repeating the rule. Continuous
    /// stock has no radius at all: it is cut straight across, whatever a preset that was
    /// saved from die-cut stock happens to still carry.</summary>
    [JsonIgnore]
    public double EffectiveCornerRadiusMm => IsContinuous
        ? 0
        : Math.Clamp(CornerRadiusMm, 0, Math.Min(WidthMm, HeightMm) / 2);

    [JsonIgnore]
    public int CornerRadiusDots => Units.MmToDots(EffectiveCornerRadiusMm, Dpmm);
}
