using System.Text;

namespace Aether.Core.Playback;

/// <summary>Plain text for a failure panel's "Copy details": title, subtitle, then one "Label: value" line per row.</summary>
public static class FailureDetails
{
    /// <summary>Rows with an empty value are left out; line breaks are <c>\r\n</c> for pasting into Windows apps.</summary>
    public static string Format(string title, string? subtitle, IEnumerable<(string Label, string? Value)> rows)
    {
        var text = new StringBuilder(title);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            text.Append("\r\n").Append(subtitle);
        }
        foreach ((string label, string? value) in rows)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                text.Append("\r\n").Append(label).Append(": ").Append(value);
            }
        }
        return text.ToString();
    }
}
