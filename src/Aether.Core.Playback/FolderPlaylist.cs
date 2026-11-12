namespace Aether.Core.Playback;

/// <summary>Finds the videos that queue up: the others in an opened file's folder, or what was dropped.</summary>
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

    /// <summary>
    /// The queue for a drop of several items: the dropped videos in Explorer order, then the videos of each dropped
    /// folder (<paramref name="list"/> reads one), folders in name order.
    /// </summary>
    public static string[] Dropped(IEnumerable<string> files, IEnumerable<string> folders, Func<string, IEnumerable<string>> list)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(list);
        return
        [
            .. Videos(files),
            .. folders.OrderBy(folder => Path.GetFileName(folder), NaturalOrder.Instance).SelectMany(folder => Videos(list(folder))),
        ];
    }
}
