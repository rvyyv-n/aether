using System.Globalization;
using Aether.Core.Playback.Audio;
using Aether.Core.Playback.Settings;
using Aether_App.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Aether_App.Controls;

/// <summary>
/// The equalizer's controls (14.2), the same in Settings › Audio and the Audio and Subtitles pop-up. Reads and writes
/// <c>audio.equalizer</c> and <c>audio.equalizerCurve</c>; moving a slider or picking a preset turns the equalizer on.
/// UI thread only.
/// </summary>
public sealed partial class EqualizerPanel : UserControl
{
    private const double SliderHeight = 112;

    private readonly Slider _preamp;
    private readonly Slider[] _bands = new Slider[10];
    private readonly TextBlock[] _readouts = new TextBlock[11];
    private bool _showing;

    public EqualizerPanel()
    {
        InitializeComponent();
        TitleText.Text = AppStrings.Get("Equalizer_Title");
        AutomationProperties.SetName(EnabledToggle, AppStrings.Get("Equalizer_Title"));
        ResetButton.Content = AppStrings.Get("Common_Reset");
        AutomationProperties.SetName(PresetCombo, AppStrings.Get("Equalizer_Preset"));
        foreach (EqualizerPreset preset in EqualizerPresets.All)
        {
            PresetCombo.Items.Add(new ComboBoxItem { Content = AppStrings.Get("Equalizer_Preset_" + preset.Key), Tag = preset.Key });
        }
        PresetCombo.Items.Add(new ComboBoxItem { Content = AppStrings.Get("Equalizer_Preset_custom"), Tag = EqualizerPresets.Custom, IsEnabled = false });

        for (int column = 0; column < 12; column++)
        {
            Bands.ColumnDefinitions.Add(new ColumnDefinition { Width = column == 1 ? new GridLength(8) : new GridLength(1, GridUnitType.Star) });
        }
        Bands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Bands.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SliderHeight) });
        Bands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _preamp = AddColumn(0, AppStrings.Get("Equalizer_PreampShort"), AppStrings.Get("Equalizer_Preamp"));
        for (int band = 0; band < _bands.Length; band++)
        {
            int hz = Equalizer.Frequencies[band];
            string label = hz >= 1000 ? $"{hz / 1000}k" : hz.ToString(CultureInfo.CurrentCulture);
            _bands[band] = AddColumn(band + 2, label, AppStrings.Format("Equalizer_Band", label));
        }

        Loaded += (_, _) =>
        {
            App.Settings.Changed += OnSettingsChanged;
            Show();
        };
        Unloaded += (_, _) => App.Settings.Changed -= OnSettingsChanged;
    }

    /// <summary>Hides the title and switch, for Settings, where the row above holds them.</summary>
    public bool ShowsToggle
    {
        get => ToggleRow.Visibility == Visibility.Visible;
        set => ToggleRow.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private Slider AddColumn(int column, string label, string name)
    {
        var readout = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Style = (Style)Application.Current.Resources["AetherTimecodeSecondaryTextStyle"] };
        var slider = new Slider
        {
            Orientation = Orientation.Vertical,
            Minimum = -Equalizer.MaxGain,
            Maximum = Equalizer.MaxGain,
            StepFrequency = 0.5,
            SmallChange = 0.5,
            LargeChange = 3,
            SnapsTo = SliderSnapsTo.StepValues,
            IsThumbToolTipEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        AutomationProperties.SetName(slider, name);
        slider.ValueChanged += OnSliderChanged;
        var caption = new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, Style = (Style)Application.Current.Resources["AetherCaptionTextStyle"] };
        Grid.SetColumn(readout, column);
        Grid.SetColumn(slider, column);
        Grid.SetRow(slider, 1);
        Grid.SetColumn(caption, column);
        Grid.SetRow(caption, 2);
        Bands.Children.Add(readout);
        Bands.Children.Add(slider);
        Bands.Children.Add(caption);
        _readouts[column == 0 ? 0 : column - 1] = readout;
        return slider;
    }

    private void OnSettingsChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key is SettingKeys.EqualizerEnabled or SettingKeys.EqualizerCurve)
        {
            DispatcherQueue.TryEnqueue(Show);
        }
    }

    private static EqualizerCurve Saved =>
        EqualizerCurve.TryParse(App.Settings.Current.EqualizerCurve, out EqualizerCurve? curve) ? curve : EqualizerCurve.Flat;

    private void Show()
    {
        EqualizerCurve curve = Saved;
        _showing = true;
        try
        {
            EnabledToggle.IsOn = App.Settings.Current.EqualizerEnabled;
            _preamp.Value = curve.Preamp;
            for (int band = 0; band < _bands.Length; band++)
            {
                _bands[band].Value = curve.Gains[band];
            }
            string key = EqualizerPresets.KeyOf(curve);
            PresetCombo.SelectedItem = PresetCombo.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == key);
            ResetButton.IsEnabled = !curve.IsFlat;
        }
        finally
        {
            _showing = false;
        }
        ShowReadouts();
    }

    private void ShowReadouts()
    {
        _readouts[0].Text = Decibels(_preamp.Value);
        for (int band = 0; band < _bands.Length; band++)
        {
            _readouts[band + 1].Text = Decibels(_bands[band].Value);
        }
    }

    private static string Decibels(double value) => value.ToString("+0.#;-0.#;0", CultureInfo.CurrentCulture);

    private static void Save(EqualizerCurve curve, bool turnOn = true)
    {
        App.Settings.Set(SettingKeys.EqualizerCurve, curve.ToString());
        if (turnOn)
        {
            App.Settings.Set(SettingKeys.EqualizerEnabled, true);
        }
        SettingsSaver.Save();
    }

    private void OnSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ShowReadouts();
        if (!_showing)
        {
            Save(new EqualizerCurve(_preamp.Value, _bands.Select(b => b.Value).ToArray()));
        }
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && PresetCombo.SelectedItem is ComboBoxItem { Tag: string key } && EqualizerPresets.Find(key) is { } preset)
        {
            Save(preset.Curve);
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e) => Save(EqualizerCurve.Flat, turnOn: false);

    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            App.Settings.Set(SettingKeys.EqualizerEnabled, EnabledToggle.IsOn);
            SettingsSaver.Save();
        }
    }
}
