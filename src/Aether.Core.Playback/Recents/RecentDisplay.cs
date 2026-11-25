using System.Security.Cryptography;
using System.Text;

namespace Aether.Core.Playback.Recents;

/// <summary>Which date label a recents row shows.</summary>
public enum RecentDay
{
    Today,
    Yesterday,
    Earlier,
}

/// <summary>The parts of a recents card (2.10, 2.18, 22.4) that do not depend on the strings file.</summary>
public static class RecentDisplay
{
    private static readonly string[] StrippedVideoExtensions =
    [
        ".mp4", ".mkv", ".mov", ".webm", ".avi", ".m4v",
        ".ts", ".mts", ".m2ts", ".wmv", ".flv", ".mpg", ".mpeg", ".ogv", ".3gp",
    ];

    /// <summary>
    /// Card title for a recent item: strips common video container extensions from local file names while leaving
    /// stream titles, non-video names, and dot-only names untouched.
    /// </summary>
    public static string DisplayTitle(RecentItem item)
    {
        string title = item.DisplayName;
        if (item.IsLink || string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        ReadOnlySpan<char> ext = Path.GetExtension(title.AsSpan());
        if (ext.IsEmpty)
        {
            return title;
        }

        foreach (string known in StrippedVideoExtensions)
        {
            if (ext.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                string stem = Path.GetFileNameWithoutExtension(title);
                return string.IsNullOrWhiteSpace(stem) ? title : stem;
            }
        }
        return title;
    }

    /// <summary>Compares calendar days in <paramref name="now"/>'s offset (the local time zone in the app).</summary>
    public static RecentDay Day(DateTimeOffset openedAt, DateTimeOffset now)
    {
        DateTime opened = openedAt.ToOffset(now.Offset).Date;
        DateTime today = now.Date;
        return opened >= today ? RecentDay.Today
            : opened == today.AddDays(-1) ? RecentDay.Yesterday
            : RecentDay.Earlier;
    }

    /// <summary>The folder for files ("D:\Captures"), the site for links ("youtube.com").</summary>
    public static string Source(RecentItem item) =>
        item.IsLink ? WebLink.Host(item.Location) ?? item.Location : Path.GetDirectoryName(item.Location) ?? item.Location;

    /// <summary>Short folder name for compact card captions ("Captures"), or host for links ("youtube.com").</summary>
    public static string ShortSource(RecentItem item)
    {
        if (item.IsLink)
        {
            return WebLink.Host(item.Location) ?? item.Location;
        }
        string? directory = Path.GetDirectoryName(item.Location);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return item.Location;
        }
        string leaf = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return leaf.Length > 0 ? leaf : directory;
    }

    /// <summary>A file that is no longer on disk. Links are never missing. <paramref name="fileExists"/> may touch the disk.</summary>
    public static bool IsMissing(RecentItem item, Func<string, bool> fileExists) => !item.IsLink && !fileExists(item.Location);

    /// <summary>Deterministic lowercase SHA-256 hex filename stem for a media location's cached thumbnail.</summary>
    public static string ThumbnailCacheKey(string location)
    {
        string normalized = WebLink.IsValid(location)
            ? location.Trim()
            : location.Trim().ToLowerInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    /// <summary><c>%LOCALAPPDATA%\Aether\thumbnails</c>, beside recents.json.</summary>
    public static string DefaultThumbnailCacheDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "thumbnails");

    public static string ThumbnailCachePath(string cacheDirectory, string location, string extension = ".jpg") =>
        Path.Combine(cacheDirectory, ThumbnailCacheKey(location) + extension);

    /// <summary>
    /// Removes stale cached thumbnails not referenced by <paramref name="activeLocations"/> once the directory
    /// holds more than <paramref name="maxKeep"/> files, deleting oldest files first.
    /// </summary>
    public static int PruneThumbnailCache(string cacheDirectory, IEnumerable<string> activeLocations, int maxKeep = 24)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory) || !Directory.Exists(cacheDirectory))
        {
            return 0;
        }

        var keep = new HashSet<string>(
            activeLocations.Where(l => !string.IsNullOrWhiteSpace(l)).Select(ThumbnailCacheKey),
            StringComparer.OrdinalIgnoreCase);

        int removed = 0;
        try
        {
            FileInfo[] files = new DirectoryInfo(cacheDirectory)
                .GetFiles("*.jpg")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToArray();

            int kept = 0;
            foreach (FileInfo file in files)
            {
                string stem = Path.GetFileNameWithoutExtension(file.Name);
                if (keep.Contains(stem))
                {
                    kept++;
                    continue;
                }
                if (kept < maxKeep)
                {
                    kept++;
                    continue;
                }
                try
                {
                    file.Delete();
                    removed++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return removed;
    }
}
