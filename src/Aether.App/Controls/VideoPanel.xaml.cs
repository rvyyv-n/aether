using Aether.Core.Playback.Video;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Aether_App.Controls;

/// <summary>
/// The body of the video flyout (screen 9.3): zoom, rotation, aspect ratio and the four colour sliders. It draws the
/// <see cref="VideoAdjustments"/> it is given and reports the new ones after each change; mpv and the flyout are the
/// page's. UI thread only.
/// </summary>
public sealed partial class VideoPanel : UserControl
{
    private readonly (Slider Slider, TextBlock Value)[] _colours = new (Slider, TextBlock)[4];
    private VideoAdjustments _current = new();
    private bool _showing; // Show sets the controls, which must not read back as the user's change

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

        foreach (int degrees in VideoAdjustments.Rotations)
        {
            RotateList.Items.Add(new ListBoxItem { Content = $"{degrees}°", Tag = degrees });
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
    }

    /// <summary>The user changed something; the page applies the new state to mpv.</summary>
    public event EventHandler<VideoAdjustments>? Adjusted;

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
