using System.Text;
using LabelForge.Core.Model;

namespace LabelForge.Core.Zpl;

/// <summary>Low-level ZPL text helpers: orientation letters and field-data escaping.</summary>
public static class ZplEncoding
{
    /// <summary>The ZPL orientation letter for an <see cref="Orientation"/>.</summary>
    public static string Letter(this Orientation orientation) => orientation switch
    {
        Orientation.Normal => "N",
        Orientation.Rotated90 => "R",
        Orientation.Rotated180 => "I",
        Orientation.Rotated270 => "B",
        _ => "N",
    };

    /// <summary>
    /// Builds a field-data segment, escaping the ZPL control characters '^', '~', and
    /// the field-hex escape character '_'. When any are present it emits the field
    /// hexadecimal indicator (^FH_) and hex-escapes those characters as _XX. Other
    /// characters (including UTF-8 text handled by ^CI28) pass through unchanged.
    /// </summary>
    /// <returns>Either "^FD{data}^FS" or "^FH_^FD{escaped}^FS".</returns>
    public static string FieldData(string data)
    {
        bool needsHex = false;
        foreach (char c in data)
        {
            if (c is '^' or '~' or '_')
            {
                needsHex = true;
                break;
            }
        }

        if (!needsHex)
        {
            return $"^FD{data}^FS";
        }

        var sb = new StringBuilder(data.Length + 8);
        sb.Append("^FH_^FD");
        foreach (char c in data)
        {
            if (c is '^' or '~' or '_')
            {
                sb.Append('_').Append(((int)c).ToString("X2"));
            }
            else
            {
                sb.Append(c);
            }
        }

        sb.Append("^FS");
        return sb.ToString();
    }
}
