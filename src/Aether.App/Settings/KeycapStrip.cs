using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings;

/// <summary>A key combination drawn as keycaps (6.2, 6.7): one rounded cap per key, e.g. Ctrl, Shift, O.</summary>
public sealed partial class KeycapStrip : StackPanel
{
    public static readonly DependencyProperty KeysProperty = DependencyProperty.Register(
        nameof(Keys), typeof(IReadOnlyList<string>), typeof(KeycapStrip), new PropertyMetadata(null, OnKeysChanged));

    public KeycapStrip()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 4;
        VerticalAlignment = VerticalAlignment.Center;
        ActualThemeChanged += (_, _) => RebindKeys();
    }

    public IReadOnlyList<string>? Keys
    {
        get => (IReadOnlyList<string>?)GetValue(KeysProperty);
        set => SetValue(KeysProperty, value);
    }

    private void RebindKeys()
    {
        Children.Clear();
        IReadOnlyList<string> keys = Keys ?? [];
        foreach (string key in keys)
        {
            Children.Add(new Border
            {
                Style = SettingsGroup.Resource<Style>("AetherKeycapStyle"),
                Child = new TextBlock
                {
                    Style = SettingsGroup.Resource<Style>("AetherCaptionTextStyle"),
                    Foreground = SettingsGroup.Resource<Microsoft.UI.Xaml.Media.Brush>("AetherTextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Text = key,
                },
            });
        }
        AutomationProperties.SetName(this, string.Join(" + ", keys));
    }

    private static void OnKeysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var strip = (KeycapStrip)d;
        strip.RebindKeys();
    }
}
