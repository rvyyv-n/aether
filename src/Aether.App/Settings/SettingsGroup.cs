using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Aether_App.Settings;

/// <summary>
/// A stack of <see cref="SettingsRow"/> and <see cref="SettingsExpander"/> items (6.2). By default every item is its
/// own rounded glass card, 4 apart. <see cref="Divided"/> puts them all in one card with a thin divider above every
/// visible item but the first, for long uniform lists such as shortcuts.
/// </summary>
public sealed partial class SettingsGroup : StackPanel
{
    private static readonly Thickness Divider = new(0, 1, 0, 0);
    private static readonly Thickness CardBorder = new(1);

    private bool _divided;

    public SettingsGroup()
    {
        Spacing = 4;
    }

    public bool Divided
    {
        get => _divided;
        set
        {
            _divided = value;
            Spacing = value ? 0 : 4;
            Background = value ? Resource<Brush>("AetherSurfaceGlassBrush") : null;
            BorderBrush = value ? Resource<Brush>("AetherStrokeSurfaceBrush") : null;
            BorderThickness = value ? CardBorder : default;
            CornerRadius = value ? Resource<CornerRadius>("AetherOverlayCornerRadius") : default;
            InvalidateMeasure();
        }
    }

    // Rows can be hidden at run time (a setting that only applies with another on), so the first visible
    // row is found on every measure. Setting an unchanged value is a no-op, so this settles in one pass.
    protected override Size MeasureOverride(Size availableSize)
    {
        bool first = true;
        foreach (UIElement child in Children)
        {
            if (child is not Control control || child.Visibility != Visibility.Visible)
            {
                continue;
            }
            if (_divided)
            {
                control.BorderThickness = first ? default : Divider;
                control.BorderBrush = Resource<Brush>("AetherStrokeDividerBrush");
            }
            else
            {
                control.Background = Resource<Brush>("AetherSurfaceGlassBrush");
                control.BorderBrush = Resource<Brush>("AetherStrokeSurfaceBrush");
                control.BorderThickness = CardBorder;
                control.CornerRadius = Resource<CornerRadius>("AetherOverlayCornerRadius");
            }
            first = false;
        }
        return base.MeasureOverride(availableSize);
    }

    internal static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
