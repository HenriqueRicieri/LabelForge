using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

public sealed class BarcodeCheckDigitTests
{
    [Theory]
    [InlineData("123456789012", 8)]
    [InlineData("00000000000", 0)]
    public void ModuloTen_ComputesExpectedDigit(string leading, int expected)
    {
        Assert.Equal(expected, BarcodeCheckDigit.ModuloTen(leading));
    }

    [Theory]
    [InlineData("4006381333931", true)]   // valid EAN-13
    [InlineData("4006381333930", false)]  // wrong check digit
    [InlineData("400638133393", false)]   // too short
    [InlineData("40063813339AB", false)]  // non-digits
    public void IsValidEan13_ValidatesCheckDigit(string value, bool expected)
    {
        Assert.Equal(expected, BarcodeCheckDigit.IsValidEan13(value));
    }

    [Theory]
    [InlineData("036000291452", true)]    // valid UPC-A
    [InlineData("036000291453", false)]   // wrong check digit
    [InlineData("03600029145", false)]    // too short
    public void IsValidUpcA_ValidatesCheckDigit(string value, bool expected)
    {
        Assert.Equal(expected, BarcodeCheckDigit.IsValidUpcA(value));
    }

    [Fact]
    public void ModuloTen_Throws_OnNonDigits()
    {
        Assert.Throws<ArgumentException>(() => BarcodeCheckDigit.ModuloTen("12A45"));
    }
}
