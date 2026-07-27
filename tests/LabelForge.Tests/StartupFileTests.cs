using LabelForge.Core.Io;

namespace LabelForge.Tests;

/// <summary>
/// What a command line asks the app to open. This is what makes a double-clicked label,
/// an "Open with" and a file dropped on the executable all arrive as the same thing.
/// </summary>
public sealed class StartupFileTests
{
    [Theory]
    [InlineData(@"C:\labels\caixa.lfl")]
    [InlineData(@"C:\labels\CAIXA.LFL")]
    [InlineData("caixa.Lfl")]
    public void ALabelOpensInTheDesigner(string path)
    {
        StartupFile file = Assert.NotNull(StartupFile.FromArguments([path]));
        Assert.Equal(StartupFileKind.Label, file.Kind);
        Assert.Equal(path, file.Path);
    }

    /// <summary>The corpus writes both cases, so the comparison has to be one that does
    /// not care: 208v1.ZPL and 101.zpl are the same kind of file.</summary>
    [Theory]
    [InlineData("etiqueta.zpl")]
    [InlineData("208v1.ZPL")]
    [InlineData("driver-output.prn")]
    [InlineData("etiqueta.txt")]
    public void AZplFileOpensInTheViewer(string path)
    {
        Assert.Equal(StartupFileKind.Zpl, StartupFile.FromArguments([path])!.Value.Kind);
    }

    /// <summary>Reported rather than dropped: a file handed to the app and silently
    /// ignored looks exactly like the app failing to start.</summary>
    [Fact]
    public void AnythingElseIsNamedRatherThanIgnored()
    {
        StartupFile file = Assert.NotNull(StartupFile.FromArguments([@"C:\logo.png"]));
        Assert.Equal(StartupFileKind.Unsupported, file.Kind);
    }

    [Fact]
    public void NoFileAskedForIsNoFile()
    {
        Assert.Null(StartupFile.FromArguments(null));
        Assert.Null(StartupFile.FromArguments([]));
        Assert.Null(StartupFile.FromArguments(["--dark"]));
        Assert.Null(StartupFile.FromArguments([""]));
        Assert.Null(StartupFile.FromArguments(["   "]));
    }

    /// <summary>Options are skipped rather than misread as paths, and the first real path
    /// wins: a shell double-click passes exactly one, and taking the first is the only rule
    /// that stays right when something else appends its own switches.</summary>
    [Fact]
    public void OptionsAreSkippedAndTheFirstPathWins()
    {
        StartupFile file = Assert.NotNull(
            StartupFile.FromArguments(["--dark", @"C:\labels\a.lfl", @"C:\labels\b.lfl"]));

        Assert.Equal(@"C:\labels\a.lfl", file.Path);
    }

    /// <summary>A bare word is not a file. Without an extension there is nothing to
    /// classify it by, and treating one as a path would turn a stray argument into a
    /// failed open the user never asked for.</summary>
    [Fact]
    public void AnArgumentWithNoExtensionIsNotAPath()
    {
        Assert.Null(StartupFile.FromArguments(["viewer", "dark"]));
        Assert.Equal(
            @"C:\labels\a.lfl",
            StartupFile.FromArguments(["viewer", @"C:\labels\a.lfl"])!.Value.Path);
    }

    /// <summary>Existence is deliberately not checked here. Whether a file opens is the
    /// opener's answer and it already reports that properly, so this stays a pure reading
    /// of the command line.</summary>
    [Fact]
    public void APathThatDoesNotExistIsStillTheFileThatWasAskedFor()
    {
        StartupFile file = Assert.NotNull(
            StartupFile.FromArguments([@"C:\nowhere\missing.lfl"]));

        Assert.Equal(StartupFileKind.Label, file.Kind);
    }
}
