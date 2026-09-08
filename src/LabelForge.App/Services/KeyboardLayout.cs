using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Input;

namespace LabelForge.App.Services;

/// <summary>
/// Which key types a given character on the keyboard layout in force, for the one binding
/// that cannot be written down as a key.
///
/// Ctrl + ] and Ctrl + [ step the stacking order everywhere, but Windows names those keys
/// after a US keyboard: on the ABNT2 layout this app is written for, the key Windows calls
/// OEM_6 types "[" and the one it calls OEM_5 types "]", the reverse of the names. Binding
/// the key would swap the two commands depending on whose keyboard it is.
///
/// Matching the CHARACTER instead was the first answer and it does not work on Windows.
/// Injecting a real Ctrl-held press into a real window on this layout shows why: "[" arrives
/// carrying U+001B, a control character, and "]" arrives carrying no character at all, so
/// <see cref="KeyEventArgs.KeySymbol"/> is never a bracket while Ctrl is held. Mapping the
/// control character back is not an option either, because which one a bracket produces
/// depends on the layout, and for one of the two there is nothing to map.
///
/// So the question is asked the other way round. Avalonia hands a key handler a
/// <see cref="Key"/>, not a virtual key or a scan code, which puts <c>ToUnicodeEx</c> out of
/// reach; <c>VkKeyScanExW</c> is reachable and answers the reverse question, which virtual
/// key types "[" on the layout in force. That virtual key becomes the Avalonia key the same
/// way Avalonia's own Win32 interop maps it, and the comparison is against the key that
/// arrived.
///
/// Resolved at each keydown and never cached: a layout switch happens without a restart.
/// Windows only. Everywhere else the character does arrive and the character is matched.
/// </summary>
public static class KeyboardLayout
{
    /// <summary>
    /// The key that types <paramref name="character"/> unmodified or with Shift on the
    /// active layout, or null when nothing does, when it takes AltGr, or when this is not
    /// Windows. A null answer is not a failure: it is the case Ctrl + Shift + Up and Down
    /// are bound for.
    /// </summary>
    public static Key? KeyThatTypes(char character) =>
        OperatingSystem.IsWindows() ? WindowsKeyThatTypes(character) : null;

    [SupportedOSPlatform("windows")]
    private static Key? WindowsKeyThatTypes(char character)
    {
        short scan = VkKeyScanExW(character, GetKeyboardLayout(0));

        // -1 is "not on this layout at all". The high byte is the shift state the character
        // needs, and only the two a person can hold alongside Ctrl are taken: 0 is a plain
        // press and 1 is Shift. Anything with Ctrl or Alt in it is AltGr territory, which is
        // where German and Spanish layouts keep their brackets, and Ctrl plus an AltGr key
        // is a chord this app has no business claiming.
        if (scan == -1 || (scan >> 8) > 1)
        {
            return null;
        }

        return FromOemVirtualKey(scan & 0xFF);
    }

    /// <summary>
    /// The three OEM virtual keys that carry brackets on the layouts where a bracket is a
    /// key of its own, mapped exactly as Avalonia.Win32's own KeyInterop maps them. A
    /// virtual key outside these three is a layout that puts the bracket somewhere this
    /// binding will not follow, and the answer is null rather than a guess.
    /// </summary>
    internal static Key? FromOemVirtualKey(int virtualKey) => virtualKey switch
    {
        0xDB => Key.OemOpenBrackets,
        0xDC => Key.Oem5,
        0xDD => Key.Oem6,
        _ => null,
    };

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern short VkKeyScanExW(char character, IntPtr keyboardLayout);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetKeyboardLayout(uint threadId);
}
