using Aether.Core.Playback;
using Aether.Core.Playback.Chapters;
using Aether_App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace Aether_App;

/// <summary>What the "Skip" button offers while a chapter to skip is playing: where it ends.</summary>
internal readonly record struct ChapterSkipOffer(double Target, string Title);

/// <summary>
/// Optional chapter auto-skip (9.7): jumps over chapters named in the settings, with a toast to undo it. With auto-skip
/// off, a Skip button offers the same jump instead while such a chapter plays. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private readonly ChapterSkipper _chapterSkipper = new();
    private string _chapterSkipNames = "";
    private IReadOnlyList<string> _chapterSkipRules = [];
    private ChapterList _skipperChapters = ChapterList.Empty;
    private readonly ChapterSkipPrompt _skipPrompt = new();
    private bool _skipChapterShown;
    private bool _pointerOnSkipChapter;

    /// <summary>The chapter now playing is one to skip while auto-skip is off; null otherwise.</summary>
    internal ChapterSkipOffer? CurrentSkipOffer { get; private set; }

    private IReadOnlyList<string> CurrentSkipRules()
    {
        string names = App.Settings.Current.ChapterSkipNames;
        if (names != _chapterSkipNames)
        {
            _chapterSkipNames = names;
            _chapterSkipRules = ChapterSkipRules.Parse(names);
        }
        return _chapterSkipRules;
    }

    private void OnChapterSkipPosition(PlayerViewModel vm)
    {
        // A new file starts with no chapters and its own arrive a moment after it plays; the names can be edited at any time.
        string namesBefore = _chapterSkipNames;
        IReadOnlyList<string> rules = CurrentSkipRules();
        if (!ReferenceEquals(_skipperChapters, _chapters) || namesBefore != _chapterSkipNames)
        {
            _skipperChapters = _chapters;
            _chapterSkipper.Reset(_chapters, rules);
        }
        if (_chapters.Count == 0 || _session is not { } session || !HasVideo)
        {
            CurrentSkipOffer = null;
            SyncSkipChapter();
            return;
        }

        double duration = vm.Duration;
        if (!App.Settings.Current.ChapterAutoSkip)
        {
            CurrentSkipOffer = OfferAt(vm.Position, duration);
            SyncSkipChapter();
            return;
        }
        CurrentSkipOffer = null;
        SyncSkipChapter();
        if (_chapterSkipper.OnPosition(vm.Position, duration) is not { } decision)
        {
            return;
        }

        int index = _chapters.IndexAt(vm.Position);
        double chapterStart = index >= 0 ? _chapters[index].Start : 0;
        SeekToChapter(decision.Target);
        _toasts?.Show("chapter-skipped", AppStrings.Format("Toast_ChapterSkipped", decision.Title), ToastTone.Neutral,
            AppStrings.Get("Toast_ChapterSkipBack"), () =>
            {
                _chapterSkipper.OnUserSeek(chapterStart);
                SeekToChapter(chapterStart);
            });
    }

    private ChapterSkipOffer? OfferAt(double position, double duration)
    {
        int index = _chapters.IndexAt(position);
        if (index < 0 || !ChapterSkipRules.Matches(_chapters[index].Title, CurrentSkipRules()))
        {
            return null;
        }
        int last = index;
        while (last + 1 < _chapters.Count && ChapterSkipRules.Matches(_chapters[last + 1].Title, CurrentSkipRules()))
        {
            last++;
        }
        double end = last + 1 >= _chapters.Count ? duration : _chapters.EndOf(last, duration);
        return end > position ? new ChapterSkipOffer(end, _chapters[index].Title) : null;
    }

    /// <summary>
    /// Shows or hides the Skip button for <see cref="CurrentSkipOffer"/>. It is a prompt, so it does not need the
    /// controls up: it shows for the first seconds of the chapter and again with the controls (<see cref="ChapterSkipPrompt"/>).
    /// Never in picture in picture or under settings, a menu, a dialog, the tour or the clip drawer.
    /// </summary>
    private void SyncSkipChapter()
    {
        ChapterSkipOffer? offer = CurrentSkipOffer;
        double position = _session?.ViewModel.Position ?? 0;
        bool wanted = _skipPrompt.Update(offer?.Target, position, _chrome.IsVisible || _pointerOnSkipChapter);
        bool blocked = IsPictureInPicture || _settingsShown || Osd.Visibility != Visibility.Visible
            || (_chrome.Holds & (ChromeHolds.Flyout | ChromeHolds.Dialog | ChromeHolds.Tour | ChromeHolds.Drawer)) != 0;
        bool show = wanted && !blocked && offer is not null;
        if (offer is { } current)
        {
            string label = AppStrings.Format("Transport_SkipChapter", current.Title);
            if (SkipChapterText.Text != label)
            {
                SkipChapterText.Text = label;
                AutomationProperties.SetName(SkipChapterButton, label);
            }
        }
        if (show == _skipChapterShown)
        {
            return;
        }
        _skipChapterShown = show;
        if (!show && SkipChapterButton.FocusState != FocusState.Unfocused)
        {
            Focus(FocusState.Programmatic); // the playback keys stay with the page when the button goes
        }
        if (_fader is null)
        {
            SkipChapterLayer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (show)
        {
            _fader.Enter(SkipChapterLayer, ChromeFader.FromBelow);
        }
        else
        {
            _pointerOnSkipChapter = false; // collapsing it sends no pointer exit
            _fader.Exit(SkipChapterLayer, ChromeFader.FromBelow);
        }
    }

    /// <summary>
    /// Bottom right, on the same line as the toasts: above the card and export chip while the controls show, 24 from
    /// the bottom once they hide. A toast it would touch (in a narrow window) lifts it above the toast.
    /// </summary>
    private void PlaceSkipChapter(bool controls, double chip)
    {
        const double BottomMargin = 24;
        double gap = Motion.Token("AetherSizeOsdGap");
        double bottom = controls ? BottomMargin + Osd.Height + gap + (chip > 0 ? chip + gap : 0) : BottomMargin;
        if (ToastLayer.Visibility == Visibility.Visible)
        {
            double toastHalf = Math.Min(ToastLayer.ActualWidth, Motion.Token("AetherSizeToastMaxWidth")) / 2;
            double skipLeft = ActualWidth - SkipChapterLayer.Margin.Right - SkipChapterLayer.ActualWidth;
            if (skipLeft < (ActualWidth / 2) + toastHalf + gap)
            {
                bottom += ToastLayer.ActualHeight + gap;
            }
        }
        Thickness margin = SkipChapterLayer.Margin;
        SkipChapterLayer.Margin = new Thickness(margin.Left, margin.Top, margin.Right, bottom);
    }

    private void OnSkipChapterClick(object sender, RoutedEventArgs e)
    {
        if (CurrentSkipOffer is { } offer && HasVideo)
        {
            _skipPrompt.Used();
            UserSeekToChapter(offer.Target);
            SyncSkipChapter();
        }
    }

    // While the pointer rests on the button it stays, even once the controls hide.
    private void OnSkipChapterPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOnSkipChapter = true;
        SyncSkipChapter();
    }

    private void OnSkipChapterPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOnSkipChapter = false;
        SyncSkipChapter();
    }
}
