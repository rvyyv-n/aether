using Aether.Core.Playback.Tracks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Controls;

/// <summary>
/// The body of the tracks flyout (screen 9.2): the audio and subtitle lists with the current track checked, Add subtitle
/// file, and the two delay steppers. It draws what <see cref="Show"/> is given and reports what was chosen; the flyout,
/// mpv and the file picker are the page's. UI thread only.
/// </summary>
public sealed partial class TracksPanel : UserControl
{
    private const string CheckGlyph = ""; // CheckMark

    public TracksPanel()
    {
        InitializeComponent();
        AudioHeader.Text = AppStrings.Get("Tracks_Audio");
        SubtitleHeader.Text = AppStrings.Get("Tracks_Subtitles");
        AudioDelayLabel.Text = AppStrings.Get("Tracks_AudioDelay");
        SubtitleDelayLabel.Text = AppStrings.Get("Tracks_SubtitleDelay");
        AddSubtitleLabel.Text = AppStrings.Get("Tracks_AddSubtitleFile");
        string earlier = AppStrings.Get("Tracks_DelayEarlier");
        string later = AppStrings.Get("Tracks_DelayLater");
        foreach (Button down in new[] { AudioDelayDown, SubtitleDelayDown })
        {
            NameButton(down, earlier);
        }
        foreach (Button up in new[] { AudioDelayUp, SubtitleDelayUp })
        {
            NameButton(up, later);
        }
    }

    /// <summary>A track was picked: <see cref="TrackChoice.Id"/> is null for Off.</summary>
    public event EventHandler<TrackChoice>? TrackChosen;

    /// <summary>The playing link's video title, which names the tracks that arrive from a web address.</summary>
    public string? LinkTitle { get; set; }

    public event EventHandler? AddSubtitleRequested;

    public event EventHandler<DelayStep>? DelayStepped;

    /// <summary>The most the panel may be tall: the window less 8 either side (9.5); it scrolls beyond that.</summary>
    public double MaxPanelHeight
    {
        get => Scroller.MaxHeight;
        set => Scroller.MaxHeight = value;
    }

    public void Show(TrackSnapshot snapshot)
    {
        TrackList tracks = snapshot.Tracks;
        Fill(AudioRows, tracks.Audio, offRow: false);
        Fill(SubtitleRows, tracks.Subtitles, offRow: true);
        AudioDelayValue.Text = TrackDelay.Format(snapshot.AudioDelay);
        SubtitleDelayValue.Text = TrackDelay.Format(snapshot.SubtitleDelay);
    }

    private void Fill(StackPanel rows, IReadOnlyList<MediaTrack> tracks, bool offRow)
    {
        rows.Children.Clear();
        TrackKind kind = offRow ? TrackKind.Subtitle : TrackKind.Audio;
        if (offRow)
        {
            bool off = !tracks.Any(t => t.Selected);
            rows.Children.Add(Row(AppStrings.Get("Tracks_Off"), off, new TrackChoice(kind, null)));
        }
        else if (tracks.Count == 0)
        {
            rows.Children.Add(new TextBlock
            {
                Margin = new Thickness(8, 6, 8, 6),
                Style = (Style)Application.Current.Resources["AetherCaptionTextStyle"],
                Text = AppStrings.Get("Tracks_None"),
            });
        }
        foreach (MediaTrack track in tracks)
        {
            string label = TrackLabels.Format(
                track, AppStrings.Get("Tracks_TrackN"), AppStrings.Get("Tracks_Sdh"), AppStrings.Get("Tracks_Forced"), LinkTitle);
            rows.Children.Add(Row(label, track.Selected, new TrackChoice(kind, track.Id)));
        }
    }

    private Button Row(string label, bool isCurrent, TrackChoice choice)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (isCurrent)
        {
            grid.Children.Add(new FontIcon
            {
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                FontSize = (double)Application.Current.Resources["AetherSizeIcon"],
                Glyph = CheckGlyph,
            });
        }
        var text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["AetherBodyTextStyle"],
            Text = label,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var row = new Button
        {
            Content = grid,
            Style = (Style)Resources["TrackRowButtonStyle"],
            Tag = choice,
        };
        ToolTipService.SetToolTip(row, label); // the full label for one that is cut short
        AutomationProperties.SetName(row, label);
        row.Click += OnRowClick;
        return row;
    }

    private void OnRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrackChoice choice })
        {
            TrackChosen?.Invoke(this, choice);
        }
    }

    private void OnAddSubtitleClick(object sender, RoutedEventArgs e) => AddSubtitleRequested?.Invoke(this, EventArgs.Empty);

    private void OnDelayDownClick(object sender, RoutedEventArgs e) => StepDelay(sender, -1);

    private void OnDelayUpClick(object sender, RoutedEventArgs e) => StepDelay(sender, +1);

    private void StepDelay(object sender, int direction)
    {
        if (sender is Button { Tag: string tag })
        {
            DelayStepped?.Invoke(this, new DelayStep(tag == "Audio" ? TrackKind.Audio : TrackKind.Subtitle, direction));
        }
    }

    private static void NameButton(Button button, string text)
    {
        AutomationProperties.SetName(button, text);
        ToolTipService.SetToolTip(button, text);
    }
}

/// <param name="Id">The mpv track id, or null for Off.</param>
public sealed record TrackChoice(TrackKind Kind, int? Id);

/// <param name="Direction">-1 for earlier, +1 for later.</param>
public sealed record DelayStep(TrackKind Kind, int Direction);
