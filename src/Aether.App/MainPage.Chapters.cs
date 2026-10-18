using Aether.Core.Interop.Mpv;
using Aether.Core.Playback.Chapters;
using Aether.Core.Playback.Streams;
using Microsoft.UI.Xaml.Input;

namespace Aether_App;

/// <summary>
/// Chapters of the open file (9.6): from yt-dlp for a link, since mpv only sees the bare stream, and from mpv's
/// <c>chapter-list</c> for a file, read once on a pool thread when it has loaded. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private ChapterList _chapters = ChapterList.Empty;
    private IReadOnlyList<StreamChapter>? _linkChapters;
    private bool _chaptersPending;

    /// <summary>Forgets the last file's chapters; the new ones load once the file plays.</summary>
    private void ResetChapters(IReadOnlyList<StreamChapter>? linkChapters)
    {
        ShowChapters(ChapterList.Empty);
        _linkChapters = linkChapters;
        _chaptersPending = true;
    }

    private void LoadChaptersOnceLoaded()
    {
        if (!_chaptersPending || !HasVideo || _session is not { } session)
        {
            return;
        }
        _chaptersPending = false;
        if (_linkChapters is { } linkChapters)
        {
            ShowChapters(ChapterList.Create(linkChapters.Select(c => (c.Start, c.Title))));
            return;
        }
        string? path = _currentPath;
        _ = Task.Run(() =>
        {
            string? json = null;
            try
            {
                json = session.Context.GetPropertyString("chapter-list");
            }
            catch (Exception exception) when (exception is MpvException or ObjectDisposedException)
            {
                // The core is shutting down; the file has no chapters as far as anyone can tell.
            }
            ChapterList chapters = ChapterList.Parse(json);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_session == session && _currentPath == path)
                {
                    ShowChapters(chapters);
                }
            });
        });
    }

    /// <summary>Keeps the seek bar's chapter marks in step with the chapters the keys and auto-skip use.</summary>
    private void ShowChapters(ChapterList chapters)
    {
        _chapters = chapters;
        SeekBar.Chapters = chapters;
    }

    private void OnNextChapterAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_chapters.Count > 0 && _session?.ViewModel.Position is { } position && _chapters.NextStart(position) is { } target)
        {
            OnTransportKey(args, () => UserSeekToChapter(target));
        }
    }

    private void OnPreviousChapterAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_chapters.Count > 0 && _session?.ViewModel.Position is { } position)
        {
            OnTransportKey(args, () => UserSeekToChapter(_chapters.PreviousStart(position)));
        }
    }

    // Jumping into a chapter on purpose must not be undone by auto-skip.
    private void UserSeekToChapter(double target)
    {
        _chapterSkipper.OnUserSeek(target);
        SeekToChapter(target);
    }

    private void SeekToChapter(double target)
    {
        if (_session is { } session)
        {
            TrySend(() => session.Seek(target, dragging: false));
        }
    }
}
