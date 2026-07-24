using LabelForge.Core.Model;
using LabelForge.Core.Templating;

namespace LabelForge.Tests;

/// <summary>
/// The two pieces the counter and clock features are built on: translating a date
/// format into what a Zebra real time clock understands, and splitting template text
/// into markers. Both are pure and both are easy to get subtly wrong, which is exactly
/// why they are separate from the generator tests.
/// </summary>
public sealed class ClockFormatAndScannerTests
{
    private static readonly DateTime Now = new(2026, 7, 24, 15, 4, 5, DateTimeKind.Unspecified);

    [Theory]
    [InlineData("dd/MM/yyyy", "%d/%m/%Y")]
    [InlineData("dd/MM/yy", "%d/%m/%y")]
    [InlineData("yyyy-MM-dd", "%Y-%m-%d")]
    [InlineData("yyyyMMdd", "%Y%m%d")]
    [InlineData("HH:mm:ss", "%H:%M:%S")]
    [InlineData("dd/MM/yyyy HH:mm", "%d/%m/%Y %H:%M")]
    public void TryTranslate_MapsEveryTokenWithAZplCounterpart(string net, string expected)
    {
        Assert.True(ZplClockFormat.TryTranslate(net, out string zpl));
        Assert.Equal(expected, zpl);
    }

    [Fact]
    public void TryTranslate_InvertsTheCaseOfMonthAndMinute()
    {
        // .NET writes the month MM and the minute mm; ZPL writes %m and %M. Getting
        // this backwards would print the month where the minute belongs.
        Assert.True(ZplClockFormat.TryTranslate("MM", out string month));
        Assert.True(ZplClockFormat.TryTranslate("mm", out string minute));
        Assert.Equal("%m", month);
        Assert.Equal("%M", minute);
    }

    [Theory]
    [InlineData("")]
    [InlineData("dd MMM yyyy")]   // month name
    [InlineData("dddd")]          // weekday name
    [InlineData("hh:mm tt")]      // 12-hour clock with AM/PM
    [InlineData("yyy")]           // three-digit year: not a token, not a literal
    [InlineData("100% dd")]       // a literal percent would be read as a clock code
    public void TryTranslate_RefusesAnythingItCannotReproduceExactly(string net)
    {
        Assert.False(ZplClockFormat.TryTranslate(net, out string zpl));
        Assert.Empty(zpl);
    }

    [Fact]
    public void EveryPreset_Translates_SoPickingOneNeverCostsThePrinterClock()
    {
        foreach (string preset in ZplClockFormat.Presets)
        {
            Assert.True(ZplClockFormat.TryTranslate(preset, out _), preset);
            Assert.True(VariableDefinition.TryFormatClock(preset, Now, out _), preset);
        }
    }

    [Fact]
    public void FormatClock_DegradesToAReadableDate_WhenTheFormatIsMalformed()
    {
        var definition = new VariableDefinition { Kind = VariableKind.Clock, ClockFormat = "\\" };

        Assert.False(VariableDefinition.TryFormatClock("\\", Now, out _));
        Assert.Equal("2026-07-24 15:04:05", definition.FormatClock(Now));
    }

    [Fact]
    public void TryFormatClock_ReadsASingleCharacterAsACustomSpecifier()
    {
        // Plain .NET would read "d" as the culture's short date pattern.
        Assert.True(VariableDefinition.TryFormatClock("d", Now, out string day));
        Assert.Equal("24", day);
    }

    [Theory]
    [InlineData(1, 1, 3, 0, "001")]
    [InlineData(1, 1, 3, 9, "010")]
    [InlineData(1, 1, 0, 9, "10")]
    [InlineData(500, -5, 3, 2, "490")]
    [InlineData(1000, 1, 2, 0, "1000")]   // padding is a minimum, never a truncation
    public void FormatCounterAt_AdvancesAndPads(
        long start, long step, int padding, int copyIndex, string expected)
    {
        var definition = new VariableDefinition
        {
            Kind = VariableKind.Counter,
            CounterStart = start,
            CounterStep = step,
            CounterPadding = padding,
        };

        Assert.Equal(expected, definition.FormatCounterAt(copyIndex));
    }

    [Fact]
    public void ValueAt_HoldsAtZeroRatherThanPrintingANegativeSerial()
    {
        var definition = new VariableDefinition
        {
            Kind = VariableKind.Counter, CounterStart = 3, CounterStep = -1,
        };

        Assert.Equal(0, definition.ValueAt(3));
        Assert.Equal(0, definition.ValueAt(99));
    }

    [Fact]
    public void Scan_SplitsLiteralsMarkersAndDirectives()
    {
        TemplateSegment[] segments =
            TemplateScanner.Scan("Lot ##LOTE## ##@REGION(1)## end").ToArray();

        Assert.Equal(5, segments.Length);
        Assert.Equal(TemplateSegmentKind.Literal, segments[0].Kind);
        Assert.Equal("Lot ", segments[0].Text);
        Assert.Equal(TemplateSegmentKind.Variable, segments[1].Kind);
        Assert.Equal("##LOTE##", segments[1].Text);
        Assert.Equal("LOTE", segments[1].Inner);
        Assert.Equal(TemplateSegmentKind.Literal, segments[2].Kind);
        Assert.Equal(TemplateSegmentKind.Directive, segments[3].Kind);
        Assert.Equal("@REGION(1)", segments[3].Inner);
        Assert.Equal(" end", segments[4].Text);
    }

    [Fact]
    public void Scan_TreatsAnUnterminatedMarkerAsPlainText()
    {
        TemplateSegment[] segments = TemplateScanner.Scan("half ##OPEN").ToArray();

        Assert.Equal("half ##OPEN", Assert.Single(segments).Text);
        Assert.Equal(TemplateSegmentKind.Literal, segments[0].Kind);
    }

    [Fact]
    public void Scan_RejoinsToTheOriginalText()
    {
        const string text = "##A##x##B@F(1)##y##@D##";

        Assert.Equal(text, string.Concat(TemplateScanner.Scan(text).Select(s => s.Text)));
    }
}
