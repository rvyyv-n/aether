using Aether.Core.Playback;
using Aether.Core.Playback.Tracks;

namespace Aether_App;

/// <summary>
/// Subtitle files beside a local video (15.1): the folder, and a Subs or Subtitles folder in it, are read while the
/// file opens, and the matches are added as tracks once it has loaded. One in the preferred language is turned on
/// when mpv has not already picked an embedded subtitle and subtitles are shown. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private Task<IReadOnlyList<SubtitleFile>>? _subtitleScan;

    /// <summary>After an engine restart the tracks flyout puts back what was loaded, so nothing is added twice.</summary>
    private void ResetSubtitleFiles(string path, bool isLink, PlaybackCarryOver? carry) =>
        _subtitleScan = !isLink && carry?.Tracks is null && Path.IsPathFullyQualified(path)
            ? Task.Run(() => FindSubtitleFiles(path))
            : null;

    private async void AddSubtitleFilesOnceLoaded()
    {
        if (_subtitleScan is not { } scan || !HasVideo)
        {
            return;
        }
        _subtitleScan = null;
        string? path = _currentPath;
        IReadOnlyList<SubtitleFile> files = await scan;
        if (files.Count == 0 || path != _currentPath || _session is null)
        {
            return;
        }
        string preferred = App.Settings.Current.PreferredSubtitleLanguage;
        bool shown = App.Settings.Current.ShowSubtitles;
        _ = ChangeTracks(context =>
        {
            bool nonePicked = context.GetPropertyString("sid") is null or "no";
            foreach (string[] command in SubtitleFiles.Commands(files, preferred, select: shown && nonePicked))
            {
                Send(context, command);
            }
        });
    }

    private static IReadOnlyList<SubtitleFile> FindSubtitleFiles(string video)
    {
        if (Path.GetDirectoryName(video) is not { Length: > 0 } folder)
        {
            return [];
        }
        string[] beside = ListFolder(folder);
        IEnumerable<string> inSubfolders = [];
        try
        {
            inSubfolders = Directory.EnumerateDirectories(folder).Where(SubtitleFiles.IsSubtitleFolder).SelectMany(ListFolder).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return SubtitleFiles.Match(video, beside.Concat(inSubfolders), FolderPlaylist.Videos(beside));
    }
}
