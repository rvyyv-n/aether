using System.Globalization;
using Aether.Core.Playback.Recents;

namespace Aether.Core.Playback.Clips;

/// <summary>What the export chip above the pill shows (5.4).</summary>
public enum ClipChipMode
{
    /// <summary>No complete In/Out range and no saved clips.</summary>
    Hidden,

    /// <summary>No range, but saved clips: a small chip that opens the drawer.</summary>
    Clips,

    /// <summary>A complete range: its times, Export clip and Clear.</summary>
    Export,

    /// <summary>A complete range without FFmpeg: Export clip is disabled.</summary>
    ExportUnavailable,
}

/// <summary>The line under the drawer's export options (screen 10.1); the first match wins.</summary>
public enum ClipHint
{
    /// <summary>Fast: explains the keyframe start and that Precise is needed for filters.</summary>
    Fast,

    /// <summary>Precise, but the size limit is too small for the clip.</summary>
    TooSmall,

    /// <summary>Precise with Include Look on, but the Look and sliders are all at rest.</summary>
    LookEmpty,

    Precise,
}

/// <summary>The parts of the clip chip and drawer rows (5.3, 5.4) that do not depend on the strings file.</summary>
public static class ClipDisplay
{
    /// <summary>The drawer shows this many rows, then scrolls.</summary>
    public const int VisibleRows = 3;

    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    public static ClipHint Hint(bool precise, bool tooSmall, bool includeLook, bool lookIsEmpty) =>
        !precise ? ClipHint.Fast
        : tooSmall ? ClipHint.TooSmall
        : includeLook && lookIsEmpty ? ClipHint.LookEmpty
        : ClipHint.Precise;

    public static ClipChipMode Chip(ClipRange range, int savedClips, bool ffmpegAvailable) =>
        range.IsComplete && range.Length > 0
            ? ffmpegAvailable ? ClipChipMode.Export : ClipChipMode.ExportUnavailable
            : savedClips > 0 ? ClipChipMode.Clips : ClipChipMode.Hidden;

    /// <summary>
    /// The grey line under a row's name: "4.2 MB · 00:40 · 18:02", then " · NVIDIA" for a Precise clip
    /// (<paramref name="encoderName"/> is the encoder's short name).
    /// </summary>
    public static string Details(SavedClip clip, DateTimeOffset now, CultureInfo culture, string yesterday, string? encoderName = null)
    {
        string details = $"{FormatSize(clip.SizeBytes, culture)} · {Timecode.FormatClock(clip.Length, clip.Length)} · {FormatSaved(clip.SavedAt, now, culture, yesterday)}";
        return encoderName is { Length: > 0 } ? $"{details} · {encoderName}" : details;
    }

    /// <summary>
    /// Explorer-style sizes in 1024 steps: "512 B", "1.5 KB", "340 KB", "4.2 MB". One decimal below 100,
    /// none from 100 up.
    /// </summary>
    public static string FormatSize(long bytes, CultureInfo culture)
    {
        if (bytes < 1024)
        {
            return string.Create(culture, $"{Math.Max(0, bytes)} B");
        }
        double value = bytes / 1024.0;
        int unit = 0;
        // Moving up on the rounded value keeps 1023.96 KB from reading "1024 KB".
        while (Math.Round(value, value < 100 ? 1 : 0) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        string number = value < 100 ? value.ToString("0.#", culture) : value.ToString("0", culture);
        return $"{number} {Units[unit]}";
    }

    /// <summary>The time for today, <paramref name="yesterday"/>, or the day and month before that.</summary>
    public static string FormatSaved(DateTimeOffset savedAt, DateTimeOffset now, CultureInfo culture, string yesterday) =>
        RecentDisplay.Day(savedAt, now) switch
        {
            RecentDay.Today => savedAt.ToOffset(now.Offset).ToString("t", culture),
            RecentDay.Yesterday => yesterday,
            _ => savedAt.ToOffset(now.Offset).ToString(culture.DateTimeFormat.MonthDayPattern, culture),
        };

    /// <summary>A clip whose file was deleted or moved. <paramref name="fileExists"/> may touch the disk.</summary>
    public static bool IsMissing(SavedClip clip, Func<string, bool> fileExists) => !fileExists(clip.Path);

    /// <summary>
    /// The start of a clip's file name: the source's name, made safe for a file name and kept short.
    /// The exporter adds the date and time.
    /// </summary>
    public static string NamePrefix(string sourcePath)
    {
        string stem = Path.GetFileNameWithoutExtension(sourcePath).Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new(stem.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        safe = safe.Length > 60 ? safe[..60].TrimEnd() : safe;
        return safe.Length > 0 ? safe : "Clip";
    }

    /// <summary><c>Videos\Aether Clips</c>; local app data when the Videos folder is unknown.</summary>
    public static string DefaultFolder()
    {
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return videos.Length > 0
            ? Path.Combine(videos, "Aether Clips")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "Clips");
    }
}
