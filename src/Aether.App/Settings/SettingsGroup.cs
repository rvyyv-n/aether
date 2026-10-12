using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Aether_App.Settings;

/// <summary>
/// One rounded glass card (6.2) holding a group of <see cref="SettingsRow"/> and <see cref="SettingsExpander"/>
/// items, with a thin divider above every visible item but the first.
/// </summary>
public sealed partial class SettingsGroup : StackPanel
{
    private static readonly Thickness Divider = new(0, 1, 0, 0);

    public SettingsGroup()
    {
        Background = Resource<Brush>("AetherSurfaceGlassBrush");
        BorderBrush = Resource<Brush>("AetherStrokeSurfaceBrush");
        BorderThickness = new Thickness(1);
        CornerRadius = Resource<CornerRadius>("AetherTileCornerRadius");
    }

    // Rows can be hidden at run time (a setting that only applies with another on), so the first visible
    // row is found on every measure. Setting an unchanged thickness is a no-op, so this settles in one pass.
    protected override Size MeasureOverride(Size availableSize)
    {
        bool first = true;
        foreach (UIElement child in Children)
        {
            if (child is Control control && child.Visibility == Visibility.Visible)
            {
                control.BorderThickness = first ? default : Divider;
                control.BorderBrush = Resource<Brush>("AetherStrokeDividerBrush");
                first = false;
            }
        }
        return base.MeasureOverride(availableSize);
    }

    internal static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
