using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

public sealed class BarcodeValidatorTests
{
    [Theory]
    [InlineData(BarcodeSymbology.Code128, "123456")]
    [InlineData(BarcodeSymbology.Code128, "ABC-123/x")]
    [InlineData(BarcodeSymbology.Ean13, "123456789012")]     // 12 digits, printer adds check
    [InlineData(BarcodeSymbology.Ean13, "4006381333931")]    // 13 digits, valid check
    [InlineData(BarcodeSymbology.UpcA, "12345678901")]       // 11 digits
    [InlineData(BarcodeSymbology.UpcA, "036000291452")]      // 12 digits, valid check
    [InlineData(BarcodeSymbology.Code39, "ABC 123-.$/+%")]
    public void Validate_ReturnsNull_ForEncodableData(BarcodeSymbology symbology, string data)
    {
        Assert.Null(BarcodeValidator.Validate(symbology, data));
    }

    [Theory]
    [InlineData(BarcodeSymbology.Ean13, "12345")]            // too short
    [InlineData(BarcodeSymbology.Ean13, "12345678901A")]     // non-digit
    [InlineData(BarcodeSymbology.Ean13, "4006381333930")]    // wrong check digit
    [InlineData(BarcodeSymbology.UpcA, "0360002914")]        // too short
    [InlineData(BarcodeSymbology.UpcA, "036000291453")]      // wrong check digit
    [InlineData(BarcodeSymbology.Code39, "abc")]             // lowercase not in charset
    [InlineData(BarcodeSymbology.Code128, "café")]           // non-ASCII
    public void Validate_ReturnsWarning_ForUnencodableData(BarcodeSymbology symbology, string data)
    {
        Assert.NotNull(BarcodeValidator.Validate(symbology, data));
    }

    [Fact]
    public void Validate_ReturnsWarning_ForEmptyData()
    {
        Assert.NotNull(BarcodeValidator.Validate(BarcodeSymbology.Code128, ""));
    }

    [Fact]
    public void Validate_SkipsTemplateMarkers()
    {
        // Markers are substituted before rendering, so they are not judged here even
        // for a numeric-only symbology that the literal text would otherwise fail.
        Assert.Null(BarcodeValidator.Validate(BarcodeSymbology.Ean13, "##BARCODE_VALUE##"));
    }
}
