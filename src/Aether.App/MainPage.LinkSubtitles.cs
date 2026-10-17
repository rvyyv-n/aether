using Aether.Core.Playback.Streams;
using Aether.Core.Playback.Tracks;

namespace Aether_App;

/// <summary>
/// A link's subtitles (9.9): yt-dlp lists them but the stream address carries none, so they are added to mpv as
/// external tracks once the file has loaded, and show up in the tracks flyout beside any embedded ones. The caption
/// font, size and style are global mpv options, so they apply to these like any other track. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private IReadOnlyList<StreamSubtitle>? _pendingLinkSubtitles;

    /// <summary>
    /// After an engine restart with a track pick the tracks flyout restores them itself (the added tracks are
    /// external files to it), so adding them here as well would double them.
    /// </summary>
    private void ResetLinkSubtitles(IReadOnlyList<StreamSubtitle>? subtitles, PlaybackCarryOver? carry) =>
        _pendingLinkSubtitles = carry?.Tracks is null ? subtitles : null;

    private void AddLinkSubtitlesOnceLoaded()
    {
        if (_pendingLinkSubtitles is not { } subtitles || !HasVideo || _session is null)
        {
            return;
        }
        _pendingLinkSubtitles = null;
        IReadOnlyList<LinkSubtitleTrack> plan = LinkSubtitlePlan.Create(subtitles, App.Settings.Current.PreferredSubtitleLanguage);
        if (plan.Count == 0)
        {
            return;
        }
        _ = ChangeTracks(context =>
        {
            foreach (LinkSubtitleTrack track in plan)
            {
                // "cached" adds the track without turning it on; only the preferred language is selected.
                Send(context, "sub-add", track.Url, track.Select ? "select" : "cached", track.Title, track.Language);
            }
        });
    }
}
