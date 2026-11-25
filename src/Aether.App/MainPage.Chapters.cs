using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether.Core.Playback.Chapters;
using Aether.Core.Playback.Streams;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    private bool _showChaptersAfterMenu;
    private FrameworkElement? _chaptersAnchor;

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
        UpdateChapterButtons();
    }

    private void UpdateChapterButtons()
    {
        bool enabled = _chapters.Count > 0 && HasVideo;
        PrevChapterButton.IsEnabled = enabled;
        ChaptersButton.IsEnabled = enabled;
        NextChapterButton.IsEnabled = enabled;
        MoreChaptersItem.IsEnabled = enabled;
    }

    private void OnChaptersMenuClick(object sender, RoutedEventArgs e)
    {
        _showChaptersAfterMenu = true;
        _chaptersAnchor = MoreButton;
    }

    private void OnChaptersButtonClick(object sender, RoutedEventArgs e) =>
        ShowChaptersMenu(showEmptyToast: true, anchor: ChaptersButton);

    private void ShowChaptersIfRequested()
    {
        if (_showChaptersAfterMenu)
        {
            _showChaptersAfterMenu = false;
            ShowChaptersMenu(showEmptyToast: false, anchor: _chaptersAnchor);
        }
    }

    private void ShowChaptersMenu(bool showEmptyToast = false, FrameworkElement? anchor = null)
    {
        if (_session is null || !HasVideo || _chapters.Count == 0)
        {
            if (showEmptyToast && _session is not null)
            {
                _toasts?.Show("chapters-none", AppStrings.Get("Chapters_None"));
            }
            return;
        }

        double position = _session.ViewModel.Position;
        double duration = _session.ViewModel.Duration;
        int activeIndex = _chapters.IndexAt(position);
        string untitled = AppStrings.Get("Chapters_Untitled");
        var itemStyle = (Style)Application.Current.Resources["AetherToggleMenuItemStyle"];

        ChaptersMenu.Items.Clear();
        for (int i = 0; i < _chapters.Count; i++)
        {
            Chapter chapter = _chapters[i];
            var item = new ToggleMenuFlyoutItem
            {
                Text = _chapters.DisplayTitle(i, untitled),
                KeyboardAcceleratorTextOverride = Timecode.FormatClock(chapter.Start, duration),
                IsChecked = i == activeIndex,
                Tag = chapter.Start,
                Style = itemStyle,
            };
            item.Click += OnChapterMenuItemClick;
            ChaptersMenu.Items.Add(item);
        }

        FrameworkElement target = anchor is { Visibility: Visibility.Visible }
            ? anchor
            : (ChaptersButton.Visibility == Visibility.Visible ? ChaptersButton : MoreButton);
        ChaptersMenu.ShowAt(target, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.Top,
            ShowMode = FlyoutShowMode.Standard,
        });
    }

    private void OnChapterMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem { Tag: double target })
        {
            UserSeekToChapter(target);
        }
    }

    private void OnChaptersMenuOpened(object? sender, object e)
    {
        HoldForMenu(true);
        SetMenuSurface(sender, true);
    }

    private void OnChaptersMenuClosed(object? sender, object e)
    {
        HoldForMenu(false);
        SetMenuSurface(sender, false);
        Focus(FocusState.Programmatic);
    }

    private void OnPreviousChapterClick(object sender, RoutedEventArgs e)
    {
        if (_chapters.Count > 0 && _session?.ViewModel.Position is { } position)
        {
            UserSeekToChapter(_chapters.PreviousStart(position));
        }
    }

    private void OnNextChapterClick(object sender, RoutedEventArgs e)
    {
        if (_chapters.Count > 0 && _session?.ViewModel.Position is { } position && _chapters.NextStart(position) is { } target)
        {
            UserSeekToChapter(target);
        }
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
