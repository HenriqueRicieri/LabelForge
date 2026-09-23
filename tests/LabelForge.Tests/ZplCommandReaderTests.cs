using LabelForge.Core.Io;
using LabelForge.Core.Model;
using LabelForge.Core.Zpl;

namespace LabelForge.Tests;

public sealed class ZplCommandReaderTests
{
    [Fact]
    public void BareTildeInDataMatrixEscapeParameterStaysWithBx()
    {
        const string zpl = "^XA^FO10,20^BXR,8,200,0,0,1,~\r\n^FH\\^FD##EAN##^FS^XZ";

        ZplCommand[] commands = ZplCommandReader.Read(zpl).ToArray();
        Assert.Equal("~", Assert.Single(commands, command => command.Code == "BX").Arg(6));
        Assert.DoesNotContain(commands, command => command.Prefix == '~');

        ZplDocumentImportResult imported = ZplDocumentImport.FromZpl(zpl);
        Assert.Empty(imported.Warnings);
        Assert.IsType<DataMatrixElement>(Assert.Single(imported.Document.Elements));
    }

    [Fact]
    public void CompleteTildeCommandStillStartsAControlCommand()
    {
        const string zpl = "^XA^BXR,8,200,0,0,1,~\r\n~JSN^FDtext ~ here^FS^XZ";

        ZplCommand[] commands = ZplCommandReader.Read(zpl).ToArray();
        Assert.Equal("~", Assert.Single(commands, command => command.Code == "BX").Arg(6));
        Assert.Contains(commands, command => command.Prefix == '~' && command.Code == "JS");
        Assert.Equal("text ~ here", Assert.Single(commands, command => command.Code == "FD").Parameters);
    }
}
