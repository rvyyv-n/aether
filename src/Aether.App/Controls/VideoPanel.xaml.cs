using System.Globalization;
using Aether.Core.Playback.Video;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Aether_App.Controls;

/// <summary>
/// The body of the video flyout (screen 9.3): zoom, rotation, aspect ratio, the four colour sliders and the Look. It
/// draws the <see cref="VideoAdjustments"/> and <see cref="VideoLook"/> it is given and reports the new ones after each
/// change; mpv, the flyout and the looks folder are the page's. UI thread only.
/// </summary>
public sealed partial class VideoPanel : UserControl
{
    private const string AddLookTag = "action:add";
    private const string OpenFolderTag = "action:open";
    private static readonly TimeSpan LookSettleDelay = TimeSpan.FromMilliseconds(150);

    private readonly (Slider Slider, TextBlock Value)[] _colours = new (Slider, TextBlock)[4];
    private readonly DispatcherQueueTimer _lookTimer;
    private VideoAdjustments _current = new();
    private VideoLook _look = VideoLook.Off;
    // Show sets the controls, which must not read back as the user's change. True from the start: the zoom slider
    // reports a change while its XAML loads (Minimum lifts its value to 25), before the other controls exist.
    private bool _showing = true;

    public VideoPanel()
    {
        InitializeComponent();
        ZoomLabel.Text = AppStrings.Get("VideoFlyout_Zoom");
        ZoomResetButton.Content = AppStrings.Get("VideoFlyout_Reset");
        RotateLabel.Text = AppStrings.Get("VideoFlyout_Rotate");
        AspectLabel.Text = AppStrings.Get("VideoFlyout_AspectRatio");
        ResetColoursButton.Content = AppStrings.Get("VideoFlyout_ResetColours");
        AutomationProperties.SetName(ZoomSlider, ZoomLabel.Text);
        AutomationProperties.SetName(RotateList, RotateLabel.Text);
        AutomationProperties.SetName(AspectCombo, AspectLabel.Text);
        LookHeading.Text = AppStrings.Get("Video_Look");
        GradeLabel.Text = AppStrings.Get("Video_LookGrade");
        SharpenLabel.Text = AppStrings.Get("Video_Sharpen");
        GrainLabel.Text = AppStrings.Get("Video_Grain");
        DenoiseLabel.Text = AppStrings.Get("Video_Denoise");
        DenoiseHint.Text = AppStrings.Get("Video_DenoiseHint");
        ResetLookButton.Content = AppStrings.Get("Video_ResetLook");
        AutomationProperties.SetName(DenoiseList, DenoiseLabel.Text);
        AutomationProperties.SetName(GradeCombo, GradeLabel.Text);
        AutomationProperties.SetName(SharpenSlider, SharpenLabel.Text);
        AutomationProperties.SetName(GrainSlider, GrainLabel.Text);

        // A drag changes the slider many times a second; mpv rebuilds its filter chain only once it settles.
        _lookTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _lookTimer.Interval = LookSettleDelay;
        _lookTimer.IsRepeating = false;
        _lookTimer.Tick += (_, _) => LookChanged?.Invoke(this, _look);

        foreach (int degrees in VideoAdjustments.Rotations)
        {
            RotateList.Items.Add(new ListBoxItem { Content = $"{degrees}°", Tag = degrees });
        }
        foreach (DenoiseLevel level in Enum.GetValues<DenoiseLevel>())
        {
            DenoiseList.Items.Add(new ListBoxItem { Content = AppStrings.Get($"Video_Denoise{level}"), Tag = level });
        }
        foreach (string aspect in VideoAdjustments.AspectOptions)
        {
            AspectCombo.Items.Add(new ComboBoxItem
            {
                Content = aspect == VideoAdjustments.AutoAspect ? AppStrings.Get("VideoFlyout_Auto") : aspect,
                Tag = aspect,
            });
        }

        string[] names = ["VideoFlyout_Brightness", "VideoFlyout_Contrast", "VideoFlyout_Gamma", "VideoFlyout_Saturation"];
        for (int i = 0; i < names.Length; i++)
        {
            ColourRows.Children.Add(BuildColourRow(i, AppStrings.Get(names[i])));
        }
        Show(_current);
    }

    /// <summary>The user changed something; the page applies the new state to mpv.</summary>
    public event EventHandler<VideoAdjustments>? Adjusted;

    /// <summary>The Look changed (sliders once they settle, everything else at once); the page applies and saves it.</summary>
    public event EventHandler<VideoLook>? LookChanged;

    /// <summary>The user chose Add a look…; the page opens the file picker and calls <see cref="ShowLook"/> with the result.</summary>
    public event EventHandler? AddLookRequested;

    public event EventHandler? OpenLooksFolderRequested;

    /// <summary>Where the user's looks are listed from; read each time the panel opens.</summary>
    public LookLibrary? Library { get; set; }

    /// <summary>What Reset colours returns the sliders to: the colours in Settings → Video.</summary>
    public VideoAdjustments Baseline { get; set; } = new();

    /// <summary>The most the panel may be tall: the window less 8 either side (9.5); it scrolls beyond that.</summary>
    public double MaxPanelHeight
    {
        get => Scroller.MaxHeight;
        set => Scroller.MaxHeight = value;
    }

    public void Show(VideoAdjustments adjustments)
    {
        _current = adjustments.Clamped();
        _showing = true;
        try
        {
            ZoomSlider.Value = _current.ZoomPercent;
            ZoomValue.Text = VideoAdjustments.FormatZoom(_current.ZoomPercent);
            ZoomResetButton.IsEnabled = _current.ZoomPercent != VideoAdjustments.DefaultZoom;
            RotateList.SelectedIndex = VideoAdjustments.Rotations.ToList().IndexOf(_current.Rotation);
            AspectCombo.SelectedIndex = VideoAdjustments.AspectOptions.ToList().IndexOf(_current.Aspect);
            int[] values = [_current.Brightness, _current.Contrast, _current.Gamma, _current.Saturation];
            for (int i = 0; i < values.Length; i++)
            {
                _colours[i].Slider.Value = values[i];
                _colours[i].Value.Text = VideoAdjustments.FormatColour(values[i]);
            }
            ResetColoursButton.IsEnabled = _current != _current.WithColoursOf(Baseline);
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>Draws the Look. The grade list is rebuilt from the looks folder, so files added by hand show up.</summary>
    public void ShowLook(VideoLook look)
    {
        _look = look.Clamped();
        _showing = true;
        try
        {
            RebuildGrades();
            SharpenSlider.Value = _look.Sharpen;
            SharpenValue.Text = FormatAmount(_look.Sharpen);
            GrainSlider.Value = _look.Grain;
            GrainValue.Text = FormatAmount(_look.Grain);
            DenoiseList.SelectedIndex = (int)_look.Denoise;
            ResetLookButton.IsEnabled = !_look.IsOff;
        }
        finally
        {
            _showing = false;
        }
    }

    private void RebuildGrades()
    {
        GradeCombo.Items.Clear();
        GradeCombo.Items.Add(new ComboBoxItem { Content = AppStrings.Get("Video_GradeNone"), Tag = VideoLook.NoGrade });
        foreach (string id in LookLibrary.BuiltIn)
        {
            GradeCombo.Items.Add(new ComboBoxItem { Content = AppStrings.Get(GradeKey(id)), Tag = id });
        }
        if (Library?.UserGrades() is { Count: > 0 } user)
        {
            GradeCombo.Items.Add(DividerItem());
            foreach (string id in user)
            {
                GradeCombo.Items.Add(new ComboBoxItem { Content = LookLibrary.DisplayName(id), Tag = id });
            }
        }
        GradeCombo.Items.Add(DividerItem());
        GradeCombo.Items.Add(new ComboBoxItem { Content = AppStrings.Get("Video_AddLook"), Tag = AddLookTag });
        GradeCombo.Items.Add(new ComboBoxItem { Content = AppStrings.Get("Video_OpenLooksFolder"), Tag = OpenFolderTag });
        SelectGrade();
    }

    /// <summary>A hairline between the groups of the grade list; it can be neither picked nor focused.</summary>
    private ComboBoxItem DividerItem() => new()
    {
        IsEnabled = false,
        IsTabStop = false,
        MinHeight = 0,
        Padding = new Thickness(0, 4, 0, 4),
        Content = new Border
        {
            Height = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = ThemeTokens.Get<Microsoft.UI.Xaml.Media.Brush>(this, "AetherStrokeDividerBrush"),
        },
    };

    private void SelectGrade() =>
        GradeCombo.SelectedItem = GradeCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, _look.Grade));

    private static string GradeKey(string id) => id switch
    {
        "warm" => "Video_GradeWarm",
        "cool" => "Video_GradeCool",
        "vivid" => "Video_GradeVivid",
        "teal-orange" => "Video_GradeTealOrange",
        "faded-film" => "Video_GradeFadedFilm",
        _ => "Video_GradeBlackWhite",
    };

    private static string FormatAmount(int value) =>
        value == 0 ? AppStrings.Get("Video_Off") : value.ToString(CultureInfo.CurrentCulture);

    private void OnGradeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || GradeCombo.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }
        if (tag is AddLookTag or OpenFolderTag)
        {
            // These are actions, not grades: the combo goes back to the grade in use.
            _showing = true;
            try
            {
                SelectGrade();
            }
            finally
            {
                _showing = false;
            }
            (tag == AddLookTag ? AddLookRequested : OpenLooksFolderRequested)?.Invoke(this, EventArgs.Empty);
            return;
        }
        ChangeLook(_look with { Grade = tag }, immediate: true);
    }

    private void OnDenoiseChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && DenoiseList.SelectedItem is ListBoxItem { Tag: DenoiseLevel level })
        {
            ChangeLook(_look with { Denoise = level }, immediate: true);
        }
    }

    private void OnAmountChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_showing)
        {
            return;
        }
        int value = VideoLook.ClampAmount(e.NewValue);
        ChangeLook(ReferenceEquals(sender, SharpenSlider) ? _look with { Sharpen = value } : _look with { Grain = value }, immediate: false);
    }

    private void OnResetLookClick(object sender, RoutedEventArgs e) => ChangeLook(VideoLook.Off, immediate: true);

    private void ChangeLook(VideoLook next, bool immediate)
    {
        if (next == _look)
        {
            return;
        }
        ShowLook(next);
        _lookTimer.Stop();
        if (immediate)
        {
            LookChanged?.Invoke(this, _look);
        }
        else
        {
            _lookTimer.Start();
        }
    }

    private UIElement BuildColourRow(int index, string label)
    {
        var heading = new Grid { ColumnSpacing = 8 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["AetherCaptionStrongTextStyle"],
        };
        var value = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["AetherBodyTextStyle"],
        };
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        Grid.SetColumn(value, 1);
        heading.Children.Add(text);
        heading.Children.Add(value);

        var slider = new Slider
        {
            Minimum = -VideoAdjustments.ColourLimit,
            Maximum = VideoAdjustments.ColourLimit,
            StepFrequency = 1,
            IsThumbToolTipEnabled = false,
            Style = (Style)Application.Current.Resources["AetherSettingsSliderStyle"],
            Width = double.NaN,
            Tag = index,
        };
        AutomationProperties.SetName(slider, label);
        slider.ValueChanged += OnColourChanged;
        _colours[index] = (slider, value);

        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(heading);
        row.Children.Add(slider);
        return row;
    }

    private void OnZoomChanged(object sender, RangeBaseValueChangedEventArgs e) =>
        Change(_current with { ZoomPercent = VideoAdjustments.ClampZoom(e.NewValue) });

    private void OnZoomResetClick(object sender, RoutedEventArgs e) =>
        Change(_current with { ZoomPercent = VideoAdjustments.DefaultZoom });

    private void OnRotateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RotateList.SelectedItem is ListBoxItem { Tag: int degrees })
        {
            Change(_current with { Rotation = degrees });
        }
    }

    private void OnAspectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AspectCombo.SelectedItem is ComboBoxItem { Tag: string aspect })
        {
            Change(_current with { Aspect = aspect });
        }
    }

    private void OnColourChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider { Tag: int index })
        {
            return;
        }
        int value = VideoAdjustments.ClampColour(e.NewValue);
        Change(index switch
        {
            0 => _current with { Brightness = value },
            1 => _current with { Contrast = value },
            2 => _current with { Gamma = value },
            _ => _current with { Saturation = value },
        });
    }

    private void OnResetColoursClick(object sender, RoutedEventArgs e) => Change(_current.WithColoursOf(Baseline));

    /// <summary>Redraws from the new state, so the readouts and Reset buttons follow, then tells the page.</summary>
    private void Change(VideoAdjustments next)
    {
        if (_showing || next == _current)
        {
            return;
        }
        Show(next);
        Adjusted?.Invoke(this, _current);
    }
}
