using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Aether.Core.Playback.Tracks;

/// <summary>One line of dialogue: when it shows, in seconds, and its plain text.</summary>
public sealed record TranscriptLine(double Start, double End, string Text);

/// <summary>
/// A subtitle file read as a transcript (15.3): SRT, WebVTT and ASS/SSA into timed plain-text lines in time order, with
/// styling, positioning and ASS drawings left out, so it can be searched and each line used to seek.
/// </summary>
public static partial class Transcript
{
    /// <summary>The lines of <paramref name="content"/>, read as the format <paramref name="extension"/> names.</summary>
    public static IReadOnlyList<TranscriptLine> Parse(string content, string extension)
    {
        ArgumentNullException.ThrowIfNull(content);
        string text = content.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
        IEnumerable<TranscriptLine> lines = extension.ToLowerInvariant() switch
        {
            ".srt" or ".vtt" => Cues(text),
            ".ass" or ".ssa" => Dialogue(text),
            _ => [],
        };
        return [.. lines.OrderBy(l => l.Start)];
    }

    private static readonly string[] TextCodecs = ["subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text"];
    private static readonly string[] TextFiles = [".srt", ".vtt", ".ass", ".ssa"];

    /// <summary>Whether <paramref name="track"/> holds text to read; picture subtitles (PGS, DVD) do not.</summary>
    public static bool IsText(MediaTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return track.ExternalFile is { } file
            ? TextFiles.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
            : track.Codec is { } codec && TextCodecs.Contains(codec, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The subtitle to show as a transcript: the one on screen when it is text, else a file, else an embedded text track.</summary>
    public static MediaTrack? Source(TrackList tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        return tracks.SelectedSubtitle is { } shown && IsText(shown)
            ? shown
            : tracks.Subtitles.FirstOrDefault(t => t.ExternalFile is not null && IsText(t))
                ?? tracks.Subtitles.FirstOrDefault(t => t.ExternalFile is null && t.StreamIndex is not null && IsText(t));
    }

    /// <summary>The ffmpeg arguments that write an embedded subtitle stream out as SRT on standard output.</summary>
    public static IReadOnlyList<string> ExtractArguments(string video, int streamIndex) =>
        ["-v", "error", "-nostdin", "-i", video, "-map", $"0:{streamIndex.ToString(CultureInfo.InvariantCulture)}", "-f", "srt", "pipe:1"];

    /// <summary>The lines holding every word of <paramref name="query"/>, ignoring case; all of them for an empty one.</summary>
    public static IReadOnlyList<TranscriptLine> Search(IReadOnlyList<TranscriptLine> lines, string? query)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string[] words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0
            ? lines
            : [.. lines.Where(l => words.All(w => l.Text.Contains(w, StringComparison.CurrentCultureIgnoreCase)))];
    }

    /// <summary>The last line that has started by <paramref name="seconds"/>, or -1 before the first.</summary>
    public static int IndexAt(IReadOnlyList<TranscriptLine> lines, double seconds)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int low = 0;
        int high = lines.Count - 1;
        int found = -1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (lines[middle].Start <= seconds)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return found;
    }

    private static IEnumerable<TranscriptLine> Cues(string text)
    {
        foreach (string block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string[] rows = block.Split('\n');
            int timing = Array.FindIndex(rows, r => r.Contains("-->", StringComparison.Ordinal));
            if (timing < 0)
            {
                continue;
            }
            string[] ends = rows[timing].Split("-->");
            string end = ends[1].Trim().Split(' ', 2)[0];
            if (!TryTime(ends[0].Trim(), out double from) || !TryTime(end, out double to))
            {
                continue;
            }
            string spoken = Clean(string.Join(' ', rows.Skip(timing + 1)));
            if (spoken.Length > 0)
            {
                yield return new TranscriptLine(from, to, spoken);
            }
        }
    }

    private static IEnumerable<TranscriptLine> Dialogue(string text)
    {
        string[]? fields = null;
        bool events = false;
        foreach (string raw in text.Split('\n'))
        {
            string row = raw.Trim();
            if (row.StartsWith('['))
            {
                events = row.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!events)
            {
                continue;
            }
            if (row.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                fields = [.. row["Format:".Length..].Split(',').Select(f => f.Trim())];
                continue;
            }
            if (fields is null || !row.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string[] values = row["Dialogue:".Length..].Split(',', fields.Length);
            int start = Array.IndexOf(fields, "Start");
            int end = Array.IndexOf(fields, "End");
            int body = Array.IndexOf(fields, "Text");
            if (values.Length != fields.Length || start < 0 || end < 0 || body < 0 || Drawing().IsMatch(values[body])
                || !TryTime(values[start].Trim(), out double from) || !TryTime(values[end].Trim(), out double to))
            {
                continue;
            }
            string spoken = Clean(values[body].Replace("\\N", " ").Replace("\\n", " ").Replace("\\h", " "));
            if (spoken.Length > 0)
            {
                yield return new TranscriptLine(from, to, spoken);
            }
        }
    }

    /// <summary><c>h:mm:ss.fff</c>, <c>mm:ss.fff</c> or with a comma for the fraction, as each format writes it.</summary>
    private static bool TryTime(string text, out double seconds)
    {
        seconds = 0;
        string[] parts = text.Replace(',', '.').Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }
        double total = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value))
            {
                return false;
            }
            total = (total * 60) + value;
        }
        seconds = total;
        return true;
    }

    private static string Clean(string text) =>
        Spaces().Replace(WebUtility.HtmlDecode(Markup().Replace(text, "")), " ").Trim();

    [GeneratedRegex(@"<[^>]*>|\{[^}]*\}")]
    private static partial Regex Markup();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\\p[1-9]")]
    private static partial Regex Drawing();
}
