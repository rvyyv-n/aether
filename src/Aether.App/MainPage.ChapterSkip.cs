using Aether.Core.Playback;
using Aether.Core.Playback.Chapters;

namespace Aether_App;

/// <summary>What the "Skip" button offers while a chapter to skip is playing: where it ends.</summary>
internal readonly record struct ChapterSkipOffer(double Target, string Title);

/// <summary>Optional chapter auto-skip (9.7): jumps over chapters named in the settings, with a toast to undo it. UI thread only.</summary>
public sealed partial class MainPage
{
    private readonly ChapterSkipper _chapterSkipper = new();
    private string _chapterSkipNames = "";
    private IReadOnlyList<string> _chapterSkipRules = [];
    private ChapterList _skipperChapters = ChapterList.Empty;

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
            return;
        }

        double duration = vm.Duration;
        if (!App.Settings.Current.ChapterAutoSkip)
        {
            CurrentSkipOffer = OfferAt(vm.Position, duration);
            return;
        }
        CurrentSkipOffer = null;
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
}
