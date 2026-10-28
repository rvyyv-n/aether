namespace Aether.Core.Playback;

/// <summary>Finds the videos that queue up with a file opened on its own: the others in its folder.</summary>
public static class FolderPlaylist
{
    /// <summary>The formats the player opens from <paramref name="folderFiles"/>, sorted by file name in Explorer order.</summary>
    public static string[] Videos(IEnumerable<string> folderFiles)
    {
        ArgumentNullException.ThrowIfNull(folderFiles);
        return folderFiles
            .Where(MediaFormats.IsSupported)
            .OrderBy(path => Path.GetFileName(path), NaturalOrder.Instance)
            .ToArray();
    }
}
