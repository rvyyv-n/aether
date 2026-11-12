using Aether.Core.Playback.Audio;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Tracks;
using Aether_App.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Aether_App.Controls;

/// <summary>
/// The body of the tracks flyout (screen 9.2): the audio and subtitle lists with the current track checked, Add subtitle
/// file, and the two delay steppers. It draws what <see cref="Show"/> is given and reports what was chosen; the flyout,
/// mpv and the file picker are the page's. UI thread only.
/// </summary>
public sealed partial class TracksPanel : UserControl
{
    private List<MixLevel> _mix = [];
    private Dictionary<int, string> _mixLabels = [];
    private bool _settingMix;
    private IReadOnlyList<MediaTrack> _audio = [];

    private const string CheckGlyph = ""; // CheckMark

    public TracksPanel()
    {
        InitializeComponent();
        AudioHeader.Text = AppStrings.Get("Tracks_Audio");
        SubtitleHeader.Text = AppStrings.Get("Tracks_Subtitles");
        SecondaryHeader.Text = AppStrings.Get("Tracks_SecondSubtitle");
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
        MixLabel.Text = AppStrings.Get("Tracks_Mix");
        ToolTipService.SetToolTip(MixToggle, AppStrings.Get("Tracks_MixDescription"));
        AutomationProperties.SetName(MixToggle, MixLabel.Text);
        NightLabel.Text = AppStrings.Get("Settings_NightMode_Title");
        ToolTipService.SetToolTip(NightToggle, AppStrings.Get("Settings_NightMode_Description"));
        AutomationProperties.SetName(NightToggle, NightLabel.Text);
        Loaded += (_, _) =>
        {
            App.Settings.Changed += OnSettingsChanged;
            NightToggle.IsOn = App.Settings.Current.NightMode;
        };
        Unloaded += (_, _) => App.Settings.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key == SettingKeys.NightMode)
        {
            DispatcherQueue.TryEnqueue(() => NightToggle.IsOn = App.Settings.Current.NightMode);
        }
    }

    private void OnNightToggled(object sender, RoutedEventArgs e)
    {
        if (NightToggle.IsOn != App.Settings.Current.NightMode)
        {
            App.Settings.Set(SettingKeys.NightMode, NightToggle.IsOn);
            SettingsSaver.Save();
        }
    }

    /// <summary>A track was picked: <see cref="TrackChoice.Id"/> is null for Off.</summary>
    public event EventHandler<TrackChoice>? TrackChosen;

    /// <summary>The audio mix (14.4) changed: every track's level, or null when the mix was switched off.</summary>
    public event EventHandler<IReadOnlyList<MixLevel>?>? MixChanged;

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
        int[] audioIds = [.. tracks.Audio.Select(t => t.Id)];
        if (!audioIds.SequenceEqual(_mix.Select(m => m.TrackId)))
        {
            _mix = [.. TrackMix.Start(audioIds)];
            SetMixing(false);
        }
        MixToggleRow.Visibility = audioIds.Length >= 2 ? Visibility.Visible : Visibility.Collapsed;
        _mixLabels = tracks.Audio.ToDictionary(t => t.Id, Label);
        _audio = tracks.Audio;
        FillMix();
        Fill(AudioRows, tracks.Audio, offRow: false);
        Fill(SubtitleRows, tracks.Subtitles, offRow: true);
        FillSecondary(tracks.Subtitles);
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
            rows.Children.Add(Row(Label(track), track.Selected && !(kind == TrackKind.Audio && MixToggle.IsOn), new TrackChoice(kind, track.Id)));
        }
    }

    /// <summary>Off and every subtitle but the main one; shown once there are two to choose between.</summary>
    private void FillSecondary(IReadOnlyList<MediaTrack> subtitles)
    {
        SecondaryRows.Children.Clear();
        Visibility shown = subtitles.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        SecondaryHeader.Visibility = SecondaryRows.Visibility = shown;
        if (shown != Visibility.Visible)
        {
            return;
        }
        SecondaryRows.Children.Add(Row(AppStrings.Get("Tracks_Off"), !subtitles.Any(t => t.Secondary), new TrackChoice(TrackKind.Subtitle, null, Secondary: true)));
        foreach (MediaTrack track in subtitles.Where(t => !t.Selected))
        {
            SecondaryRows.Children.Add(Row(Label(track), track.Secondary, new TrackChoice(TrackKind.Subtitle, track.Id, Secondary: true)));
        }
    }

    private string Label(MediaTrack track) =>
        TrackLabels.Format(track, AppStrings.Get("Tracks_TrackN"), AppStrings.Get("Tracks_Sdh"), AppStrings.Get("Tracks_Forced"), LinkTitle);

    /// <summary>A new file: the mix is the old file's, so it goes, without telling the page, which has already moved on.</summary>
    public void EndMix()
    {
        _mix = [];
        SetMixing(false);
    }

    private void SetMixing(bool on)
    {
        _settingMix = true;
        MixToggle.IsOn = on;
        _settingMix = false;
        MixRows.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FillMix()
    {
        MixRows.Children.Clear();
        if (!MixToggle.IsOn)
        {
            return;
        }
        for (int i = 0; i < _mix.Count; i++)
        {
            MixRows.Children.Add(MixRow(i));
        }
    }

    private StackPanel MixRow(int index)
    {
        MixLevel level = _mix[index];
        string label = _mixLabels.GetValueOrDefault(level.TrackId, level.TrackId.ToString(System.Globalization.CultureInfo.CurrentCulture));
        var name = new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["AetherCaptionTextStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var mute = new ToggleButton
        {
            Content = level.Muted ? "\uE74F" : "\uE767",
            IsChecked = level.Muted,
            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            Style = (Style)Application.Current.Resources["AetherIconToggleButtonStyle"],
        };
        NameButton(mute, AppStrings.Format("Tracks_MixMute", label));
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = TrackMix.MaxPercent,
            StepFrequency = 5,
            Value = level.Percent,
            IsThumbToolTipEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(slider, AppStrings.Format("Tracks_MixLevel", label));
        var value = new TextBlock
        {
            Width = 40,
            Text = Percent(level.Percent),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Right,
            Style = (Style)Application.Current.Resources["AetherTimecodeSecondaryTextStyle"],
        };
        mute.Click += (_, _) =>
        {
            _mix[index] = _mix[index] with { Muted = mute.IsChecked == true };
            mute.Content = mute.IsChecked == true ? "\uE74F" : "\uE767";
            MixChanged?.Invoke(this, [.. _mix]);
        };
        slider.ValueChanged += (_, e) =>
        {
            _mix[index] = _mix[index] with { Percent = MixLevel.Clamp((int)Math.Round(e.NewValue)) };
            value.Text = Percent(_mix[index].Percent);
            MixChanged?.Invoke(this, [.. _mix]);
        };
        var controls = new Grid { ColumnSpacing = 8 };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(value, 2);
        controls.Children.Add(mute);
        controls.Children.Add(slider);
        controls.Children.Add(value);
        var row = new StackPanel();
        row.Children.Add(name);
        row.Children.Add(controls);
        return row;
    }

    private static string Percent(int percent) => string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{percent}%");

    private void OnMixToggled(object sender, RoutedEventArgs e)
    {
        if (_settingMix)
        {
            return;
        }
        MixRows.Visibility = MixToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        FillMix();
        Fill(AudioRows, _audio, offRow: false);
        MixChanged?.Invoke(this, MixToggle.IsOn ? [.. _mix] : null);
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
            if (choice.Kind == TrackKind.Audio && MixToggle.IsOn)
            {
                SetMixing(false); // the page ends the mix on the track picked
                FillMix();
            }
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

    private static void NameButton(ButtonBase button, string text)
    {
        AutomationProperties.SetName(button, text);
        ToolTipService.SetToolTip(button, text);
    }
}

/// <param name="Id">The mpv track id, or null for Off.</param>
/// <param name="Secondary">The pick is for the second subtitle (15.2).</param>
public sealed record TrackChoice(TrackKind Kind, int? Id, bool Secondary = false);

/// <param name="Direction">-1 for earlier, +1 for later.</param>
public sealed record DelayStep(TrackKind Kind, int Direction);
