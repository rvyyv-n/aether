using Aether.Core.Playback;

namespace Aether_App;

/// <summary>
/// The note that playback came back at the saved position (9.9), with a way to start over. The position itself is
/// restored silently in <see cref="Open"/>. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private double? _pendingResumeOffer;
    private string? _pendingResumeKey;

    /// <param name="restoredAt">Where the saved position put playback, or null when nothing was restored or the engine restart carried the position over.</param>
    private void ResetResumeOffer(double? restoredAt, string resumeKey)
    {
        _pendingResumeOffer = restoredAt;
        _pendingResumeKey = resumeKey;
    }

    private void ShowResumeOfferOnceLoaded()
    {
        if (_pendingResumeOffer is not { } position || _pendingResumeKey is not { } key || !HasVideo)
        {
            return;
        }
        _pendingResumeOffer = null;
        if (IsPictureInPicture)
        {
            return;
        }
        double duration = _session?.ViewModel.Duration ?? 0;
        _toasts?.Show("resume-offer", AppStrings.Format("Toast_ResumedAt", Timecode.FormatClock(position, duration)), ToastTone.Neutral,
            AppStrings.Get("Toast_StartOver"), () =>
            {
                _ = Task.Run(() => App.Resume.Remove(key));
                UserSeekToChapter(0);
            });
    }
}
