using System.Globalization;
using System.Text;

namespace Aether.Core.Playback.Streams;

/// <summary>
/// The per-file option list for mpv's <c>loadfile</c> command. Each value is written with mpv's
/// <c>%length%</c> quoting, so URLs carrying commas, equals signs or semicolons arrive intact.
/// </summary>
public static class MpvLoadOptions
{
    /// <summary>Options for a resolved link: the separate sound stream when yt-dlp split video and audio.</summary>
    public static string ForStream(string? audioUrl) =>
        string.IsNullOrEmpty(audioUrl) ? string.Empty : Build([new("audio-files-append", audioUrl)]);

    public static string Build(IEnumerable<KeyValuePair<string, string>> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sb = new StringBuilder();
        foreach ((string key, string value) in options)
        {
            if (sb.Length > 0)
            {
                sb.Append(',');
            }
            // mpv counts the length in bytes, not characters.
            sb.Append(key).Append("=%")
              .Append(Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture))
              .Append('%').Append(value);
        }
        return sb.ToString();
    }
}
