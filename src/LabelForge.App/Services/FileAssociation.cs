using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using LabelForge.Core.Io;

namespace LabelForge.App.Services;

/// <summary>
/// Registers the .lfl extension with the Windows shell, so double-clicking a label opens
/// LabelForge. Written at install and removed at uninstall, through Velopack's hooks.
///
/// Everything goes under HKEY_CURRENT_USER, which is not a shortcut taken to avoid a
/// prompt: Velopack installs per user, into LocalApplicationData, so a machine-wide
/// association would point every account at one account's copy of the app. It also means
/// no elevation, which is what lets this run inside an install hook at all.
///
/// The classes root is injectable for the same reason the media, catalog and recovery
/// stores are: this is per-machine state, and a harness run must not touch what the person
/// using the app has. Nothing else varies it.
///
/// What this cannot do, stated rather than implied: from Windows 8 on, a user who has
/// picked a default program for an extension has that choice recorded under
/// Explorer\FileExts, it wins over anything here, and it is hash-protected so no installer
/// can write it. Registering a brand-new extension like .lfl works because there is no such
/// choice on record; taking one back from another program is deliberately beyond reach.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileAssociation(string classesRoot = FileAssociation.DefaultClassesRoot)
{
    /// <summary>Where per-user file types live. HKCU\Software\Classes is merged over
    /// HKLM's by the shell, which is what makes a per-user association work.</summary>
    public const string DefaultClassesRoot = @"Software\Classes";

    /// <summary>The document type's identifier. Verbs, icon and description hang off it,
    /// and the extension points at it, which is the indirection that lets several
    /// extensions share one handler and lets an uninstall tell ours from someone else's.</summary>
    public const string ProgId = "LabelForge.Label";

    private const string Description = "LabelForge Label";

    /// <summary>Points .lfl at this executable. Rewritten on every install and update
    /// rather than only when missing: it costs nothing, and it repairs a registration that
    /// something else removed. It cannot override a choice the user made themselves, for
    /// the reason on the class, so this repairs rather than takes.</summary>
    /// <returns>True when the shell was told; false when it could not be, which is never
    /// worth failing an install over.</returns>
    public bool Register(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using (RegistryKey progId = Registry.CurrentUser.CreateSubKey($@"{classesRoot}\{ProgId}"))
            {
                progId.SetValue(null, Description);

                using (RegistryKey icon = progId.CreateSubKey("DefaultIcon"))
                {
                    // Index 0 is the executable's own icon, so the label files look like
                    // the app that owns them without shipping a second .ico.
                    icon.SetValue(null, $"\"{executablePath}\",0");
                }

                using RegistryKey command = progId.CreateSubKey(@"shell\open\command");

                // "%1" quoted: an unquoted one splits a path at its first space, which is
                // most paths, since the documents folder of a real machine has spaces in it.
                command.SetValue(null, $"\"{executablePath}\" \"%1\"");
            }

            using (RegistryKey extension =
                   Registry.CurrentUser.CreateSubKey($@"{classesRoot}\{StartupFile.LabelExtension}"))
            {
                extension.SetValue(null, ProgId);
            }

            NotifyShell();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException
                                       or System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Takes the association back out at uninstall, leaving behind anything that is not
    /// ours.
    ///
    /// The extension key is removed only while it still names our handler. If something
    /// else has claimed .lfl since, deleting it would break that program on our way out,
    /// and an uninstaller that damages a different application is worse than one that
    /// leaves a key behind.
    /// </summary>
    public bool Unregister()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            string extensionPath = $@"{classesRoot}\{StartupFile.LabelExtension}";
            using (RegistryKey? extension = Registry.CurrentUser.OpenSubKey(extensionPath))
            {
                if (extension?.GetValue(null) as string == ProgId)
                {
                    extension.Dispose();
                    Registry.CurrentUser.DeleteSubKeyTree(extensionPath, throwOnMissingSubKey: false);
                }
            }

            Registry.CurrentUser.DeleteSubKeyTree(
                $@"{classesRoot}\{ProgId}", throwOnMissingSubKey: false);

            NotifyShell();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException
                                       or System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>Tells Explorer the association table changed. Without it the new icon and
    /// the new verb appear only after the shell is restarted, which reads as the install
    /// not having worked.</summary>
    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            // Nothing to tell: no shell here.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
