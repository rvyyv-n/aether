namespace Aether.Core.Playback;

/// <summary>The files either side of the one that is playing; null at an end of the folder.</summary>
public readonly record struct FolderNeighbours(string? Previous, string? Next);

/// <summary>Picks which file plays around the current one: the next or previous video in its folder.</summary>
public static class FolderPlaylist
{
    /// <summary>
    /// The path after <paramref name="current"/> when <paramref name="folderFiles"/> are sorted by file name in
    /// Explorer order, keeping only formats the player opens; null at the last file, or when
    /// <paramref name="current"/> is not in the listing (moved or deleted while playing).
    /// </summary>
    public static string? NextAfter(string current, IEnumerable<string> folderFiles) =>
        NeighboursOf(current, folderFiles).Next;

    /// <summary>The path before <paramref name="current"/>, in the same order as <see cref="NextAfter"/>.</summary>
    public static string? PreviousBefore(string current, IEnumerable<string> folderFiles) =>
        NeighboursOf(current, folderFiles).Previous;

    /// <summary>Both neighbours in one pass over the listing; both null when <paramref name="current"/> is not in it.</summary>
    public static FolderNeighbours NeighboursOf(string current, IEnumerable<string> folderFiles)
    {
        ArgumentException.ThrowIfNullOrEmpty(current);
        ArgumentNullException.ThrowIfNull(folderFiles);

        string[] videos = folderFiles
            .Where(MediaFormats.IsSupported)
            .OrderBy(path => Path.GetFileName(path), NaturalOrder.Instance)
            .ToArray();
        int index = Array.FindIndex(videos, path => string.Equals(path, current, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return default;
        }
        return new FolderNeighbours(
            index > 0 ? videos[index - 1] : null,
            index + 1 < videos.Length ? videos[index + 1] : null);
    }
}
