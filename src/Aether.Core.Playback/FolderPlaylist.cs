namespace Aether.Core.Playback;

/// <summary>Picks which file plays after the current one finishes: the next video in its folder.</summary>
public static class FolderPlaylist
{
    /// <summary>
    /// The path after <paramref name="current"/> when <paramref name="folderFiles"/> are sorted by file name in
    /// Explorer order, keeping only formats the player opens; null at the last file, or when
    /// <paramref name="current"/> is not in the listing (moved or deleted while playing).
    /// </summary>
    public static string? NextAfter(string current, IEnumerable<string> folderFiles)
    {
        ArgumentException.ThrowIfNullOrEmpty(current);
        ArgumentNullException.ThrowIfNull(folderFiles);

        string[] videos = folderFiles
            .Where(MediaFormats.IsSupported)
            .OrderBy(path => Path.GetFileName(path), NaturalOrder.Instance)
            .ToArray();
        int index = Array.FindIndex(videos, path => string.Equals(path, current, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < videos.Length ? videos[index + 1] : null;
    }
}
