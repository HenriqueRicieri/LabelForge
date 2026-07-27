namespace LabelForge.Core.Io;

/// <summary>What kind of file the app was asked to open.</summary>
public enum StartupFileKind
{
    /// <summary>A .lfl label: the app's own document format, opened in the designer.</summary>
    Label,

    /// <summary>A ZPL file, opened in the viewer. Deliberately not the designer: importing
    /// ZPL into the model is a deliberate act with choices attached (which block, which
    /// density), while the viewer reads anything and asks nothing.</summary>
    Zpl,

    /// <summary>Something else. Named rather than ignored: a file handed to the app and
    /// silently dropped looks exactly like the app failing to start.</summary>
    Unsupported,
}

/// <param name="Path">The path as it was given, not resolved or checked. Whether it opens
/// is the opener's answer, and it already reports that well.</param>
/// <param name="Kind">Which part of the app it belongs to.</param>
public readonly record struct StartupFile(string Path, StartupFileKind Kind)
{
    /// <summary>The app's own document format, and the only extension it registers with
    /// the shell.</summary>
    public const string LabelExtension = ".lfl";

    /// <summary>
    /// What is opened in the viewer, and deliberately the same list the viewer's own file
    /// picker offers, plus the .prn a printer driver writes to a FILE: port. Two lists
    /// would be two answers to "can this app open that".
    ///
    /// None of these is registered with the shell: they belong to whatever the machine
    /// already prints with, and taking them would be presumptuous. They are recognised here
    /// anyway, because a file dropped on the app is an explicit request and answering "I
    /// cannot open that" would be a lie.
    /// </summary>
    public static IReadOnlyList<string> ZplExtensions { get; } = [".zpl", ".txt", ".prn"];

    /// <summary>
    /// The file a command line asks the app to open, or null when it asks for none.
    ///
    /// The first path-looking argument wins: a shell double-click passes exactly one, and
    /// picking the first is the only rule that stays right when something else appends its
    /// own switches. Options are skipped rather than misread as paths.
    /// </summary>
    public static StartupFile? FromArguments(IEnumerable<string>? arguments)
    {
        if (arguments is null)
        {
            return null;
        }

        foreach (string argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument) || argument.StartsWith('-'))
            {
                continue;
            }

            string path = argument.Trim();
            string extension = System.IO.Path.GetExtension(path);
            if (extension.Length == 0)
            {
                continue;
            }

            return new StartupFile(path, Classify(extension));
        }

        return null;
    }

    private static StartupFileKind Classify(string extension)
    {
        if (string.Equals(extension, LabelExtension, StringComparison.OrdinalIgnoreCase))
        {
            return StartupFileKind.Label;
        }

        return ZplExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? StartupFileKind.Zpl
            : StartupFileKind.Unsupported;
    }
}
