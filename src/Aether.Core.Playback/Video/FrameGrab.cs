namespace Aether.Core.Playback.Video;

/// <summary>
/// The file side of Copy frame and Save frame (roadmap 7.19). mpv writes the picture; this decides where, and finds
/// the file again afterwards, because the <c>screenshot</c> command does not say what it called it.
/// </summary>
public static class FrameGrab
{
    public const string FolderName = "Aether";

    /// <summary>The mpv command's flags: the video with its captions, without the on-screen display.</summary>
    public const string Flags = "subtitles";

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>
    /// The folder snapshots go to: the one in Settings, or Pictures\Aether when that is empty (mpv would otherwise
    /// use the folder it was started in, which for an installed app is not somewhere to look). Empty when there is
    /// no Pictures folder either.
    /// </summary>
    public static string SaveFolder(string configured, string picturesFolder)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }
        return string.IsNullOrWhiteSpace(picturesFolder) ? "" : Path.Combine(picturesFolder, FolderName);
    }

    /// <summary>mpv expands <c>~/</c> and <c>~~/</c> itself, so the folder cannot be looked at from here.</summary>
    public static bool IsMpvPath(string folder) => folder.StartsWith('~');

    /// <summary>A name for the picture handed to the clipboard, one per request so two quick copies never share a file.</summary>
    public static string TempFile(string tempFolder, Guid id) => Path.Combine(tempFolder, FolderName, $"frame-{id:N}.png");

    /// <summary>
    /// The picture mpv just wrote: the newest image in the folder modified at or after <paramref name="since"/>.
    /// Null when there is none, or the folder cannot be read; the caller then saves without a Show in folder.
    /// </summary>
    public static string? FindSaved(string folder, DateTime sinceUtc)
    {
        try
        {
            string? newest = null;
            DateTime newestTime = DateTime.MinValue;
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                DateTime written = File.GetLastWriteTimeUtc(file);
                if (written >= sinceUtc && written >= newestTime)
                {
                    newest = file;
                    newestTime = written;
                }
            }
            return newest;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
