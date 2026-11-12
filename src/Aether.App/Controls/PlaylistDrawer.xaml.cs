using System.Collections.ObjectModel;
using System.ComponentModel;
using Aether.Core.Playback;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.UI.Text;

namespace Aether_App.Controls;

/// <summary>One queued video as the drawer's list shows it; its length fills in once the shell has read it.</summary>
public sealed partial class PlaylistDrawerRow(QueueRow row, string? duration) : INotifyPropertyChanged
{
    private string _duration = duration ?? "";

    public int Index { get; } = row.Index;

    public string Item { get; } = row.Item;

    public string Title { get; } = row.Title;

    public Visibility PlayingVisibility { get; } = row.IsPlaying ? Visibility.Visible : Visibility.Collapsed;

    public FontWeight TitleWeight { get; } = row.IsPlaying ? FontWeights.SemiBold : FontWeights.Normal;

    public string RemoveLabel { get; } = AppStrings.Get("Queue_Remove");

    public string Duration
    {
        get => _duration;
        set
        {
            if (_duration != value)
            {
                _duration = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Duration)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The queue drawer (13.3): the play queue with the playing video marked, a filter, drag to reorder, remove, clear,
/// shuffle and repeat. It edits <see cref="Queue"/> itself and tells the page what to play. UI thread only.
/// </summary>
public sealed partial class PlaylistDrawer : UserControl
{
    // Lengths the shell has read, by path; a link or a file it cannot read is stored as empty.
    private static readonly Dictionary<string, string> Durations = new(StringComparer.OrdinalIgnoreCase);

    private readonly ObservableCollection<PlaylistDrawerRow> _rows = [];
    private PlaylistDrawerRow[] _dragStart = [];

    public PlaylistDrawer()
    {
        InitializeComponent();
        RowList.ItemsSource = _rows;
        TitleText.Text = AppStrings.Get("Queue_Title");
        FilterBox.PlaceholderText = AppStrings.Get("Queue_Filter");
        AutomationProperties.SetName(FilterBox, AppStrings.Get("Queue_Filter"));
        Label(ShuffleButton, "Queue_Shuffle");
        Label(ClearButton, "Queue_Clear");
        Label(CloseButton, "Queue_Close");
        RowList.DragItemsStarting += (_, _) => _dragStart = [.. _rows];
    }

    public PlaylistStore? Queue { get; set; }

    /// <summary>The card, for the blur behind it.</summary>
    public FrameworkElement Surface => Panel;

    /// <summary>A row was clicked: the queue has moved to it, and the page should play it.</summary>
    public event EventHandler<string>? PlayRequested;

    /// <summary>The queue's items or their order changed here, so it is no longer the folder's.</summary>
    public event EventHandler? Edited;

    /// <summary>Repeat or shuffle changed, which changes where previous and next go.</summary>
    public event EventHandler? ModeChanged;

    public event EventHandler? CloseRequested;

    /// <summary>Redraws the list from the queue; cheap enough to call on every change.</summary>
    public void Refresh()
    {
        if (Queue is not { } queue)
        {
            return;
        }
        IReadOnlyList<string> items = queue.Items;
        IReadOnlyList<QueueRow> rows = QueueRows.Build(items, queue.CurrentIndex, FilterBox.Text);
        _rows.Clear();
        foreach (QueueRow row in rows)
        {
            var view = new PlaylistDrawerRow(row, Durations.GetValueOrDefault(row.Item));
            _rows.Add(view);
            if (!Durations.ContainsKey(row.Item))
            {
                ReadDuration(view);
            }
        }

        bool filtering = FilterBox.Text.Trim().Length > 0;
        RowList.CanReorderItems = RowList.CanDragItems = !filtering;
        CountText.Text = items.Count == 1 ? AppStrings.Get("Queue_CountOne") : AppStrings.Format("Queue_Count", items.Count);
        EmptyText.Text = AppStrings.Get(items.Count == 0 ? "Queue_Empty" : "Queue_NoMatch");
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = items.Count > 0;
        ShuffleButton.IsChecked = queue.Shuffle;
        RepeatButton.IsChecked = queue.Repeat != RepeatMode.None;
        RepeatButton.Content = queue.Repeat == RepeatMode.One ? "" : "";
        Label(RepeatButton, queue.Repeat switch
        {
            RepeatMode.All => "Queue_RepeatAll",
            RepeatMode.One => "Queue_RepeatOne",
            _ => "Queue_RepeatOff",
        });
    }

    /// <summary>Scrolls the playing row into view. The keyboard stays with the player, so Space still pauses.</summary>
    public void ShowPlaying()
    {
        if (_rows.FirstOrDefault(r => r.PlayingVisibility == Visibility.Visible) is { } playing)
        {
            RowList.ScrollIntoView(playing);
        }
    }

    private async void ReadDuration(PlaylistDrawerRow row)
    {
        string path = row.Item;
        Durations[path] = "";
        if (!Path.IsPathFullyQualified(path))
        {
            return;
        }
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            TimeSpan length = (await file.Properties.GetVideoPropertiesAsync()).Duration;
            if (length > TimeSpan.Zero)
            {
                Durations[path] = Timecode.FormatClock(length.TotalSeconds, length.TotalSeconds);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return; // gone or unreadable: no length
        }
        row.Duration = Durations[path];
    }

    private static void Label(FrameworkElement element, string key)
    {
        string label = AppStrings.Get(key);
        ToolTipService.SetToolTip(element, label);
        AutomationProperties.SetName(element, label);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (Queue is { } queue && e.ClickedItem is PlaylistDrawerRow row && row.Index < queue.Items.Count)
        {
            PlayRequested?.Invoke(this, queue.Select(row.Index));
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (Queue is { } queue && sender is FrameworkElement { Tag: PlaylistDrawerRow row } && row.Index < queue.Items.Count)
        {
            queue.RemoveAt(row.Index);
            Edited?.Invoke(this, EventArgs.Empty);
            Refresh();
        }
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (Queue is { } queue && QueueRows.FindMove(_dragStart, [.. _rows]) is (int from, int to))
        {
            queue.Move(_dragStart[from].Index, _dragStart[to].Index);
            Edited?.Invoke(this, EventArgs.Empty);
        }
        _dragStart = [];
        Refresh();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Queue?.Clear();
        Edited?.Invoke(this, EventArgs.Empty);
        Refresh();
    }

    private void OnShuffleClick(object sender, RoutedEventArgs e)
    {
        if (Queue is { } queue)
        {
            queue.Shuffle = ShuffleButton.IsChecked == true;
            ModeChanged?.Invoke(this, EventArgs.Empty);
            Refresh();
        }
    }

    private void OnRepeatClick(object sender, RoutedEventArgs e)
    {
        if (Queue is { } queue)
        {
            queue.Repeat = QueueRows.NextRepeat(queue.Repeat);
            ModeChanged?.Invoke(this, EventArgs.Empty);
            Refresh();
        }
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
