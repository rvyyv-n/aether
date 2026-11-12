using Aether.Core.Playback;
using Aether.Core.Playback.Tracks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Controls;

/// <summary>One transcript line as the list shows it.</summary>
public sealed partial class TranscriptRow(TranscriptLine line, double duration)
{
    public TranscriptLine Line { get; } = line;

    public string Time { get; } = Timecode.FormatClock(line.Start, duration);

    public string Text => Line.Text;
}

/// <summary>
/// The transcript pane (15.3): shows what <see cref="Load"/> is given, narrows it to what is searched for, follows the
/// line playing now and asks the page to seek where a line is clicked. UI thread only.
/// </summary>
public sealed partial class TranscriptPane : UserControl
{
    private IReadOnlyList<TranscriptLine> _lines = [];
    private IReadOnlyList<TranscriptLine> _shown = [];
    private double _duration;
    private bool _pointerInList;
    private int _playing = -1;

    public TranscriptPane()
    {
        InitializeComponent();
        TitleText.Text = AppStrings.Get("Transcript_Title");
        SearchBox.PlaceholderText = AppStrings.Get("Transcript_Search");
        AutomationProperties.SetName(SearchBox, SearchBox.PlaceholderText);
        string close = AppStrings.Get("Queue_Close");
        ToolTipService.SetToolTip(CloseButton, close);
        AutomationProperties.SetName(CloseButton, close);
        LineList.PointerEntered += (_, _) => _pointerInList = true;
        LineList.PointerExited += (_, _) => _pointerInList = false;
    }

    /// <summary>The card, for the blur behind it.</summary>
    public FrameworkElement Surface => Panel;

    /// <summary>A line was clicked: seek to its start, in seconds.</summary>
    public event EventHandler<double>? SeekRequested;

    public event EventHandler? CloseRequested;

    /// <summary>Shows <paramref name="lines"/> from the subtitle named <paramref name="source"/>; no lines shows <paramref name="emptyMessage"/>.</summary>
    public void Load(IReadOnlyList<TranscriptLine> lines, string source, double duration, string emptyMessage)
    {
        _lines = lines;
        _duration = duration;
        _playing = -1;
        SourceText.Text = source;
        MessageText.Text = emptyMessage;
        Show();
    }

    /// <summary>Highlights the line playing at <paramref name="seconds"/> and, while nothing is searched or pointed at, scrolls to it.</summary>
    public void SetPosition(double seconds)
    {
        bool searching = SearchBox.Text.Trim().Length > 0;
        int index = Transcript.IndexAt(_shown, seconds);
        if (searching && index >= 0 && seconds >= _shown[index].End)
        {
            index = -1;
        }
        if (index == _playing)
        {
            return;
        }
        _playing = index;
        LineList.SelectedIndex = index;
        if (index >= 0 && !_pointerInList && !searching)
        {
            LineList.ScrollIntoView(LineList.Items[index]);
        }
    }

    private void Show()
    {
        _shown = Transcript.Search(_lines, SearchBox.Text);
        _playing = -1;
        LineList.ItemsSource = _shown.Select(line => new TranscriptRow(line, _duration)).ToList();
        if (_lines.Count > 0 && _shown.Count == 0)
        {
            MessageText.Text = AppStrings.Get("Transcript_NoMatch");
        }
        MessageText.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Show();

    private void OnLineClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TranscriptRow row)
        {
            SeekRequested?.Invoke(this, row.Line.Start);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
