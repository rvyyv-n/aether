using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings;

/// <summary>
/// A settings row (6.2): icon, title and description on the left, the control (the row's content) on the right.
/// On a narrow page the control drops under the text so the page holds at 405 px.
/// </summary>
[TemplatePart(Name = "Icon", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "DescriptionText", Type = typeof(FrameworkElement))]
[TemplateVisualState(GroupName = "LayoutStates", Name = "Wide")]
[TemplateVisualState(GroupName = "LayoutStates", Name = "Narrow")]
public sealed partial class SettingsRow : ContentControl
{
    /// <summary>Below this row width the control wraps under the text.</summary>
    private const double NarrowWidth = 460;

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsRow), new PropertyMetadata(null, OnPartChanged));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsRow), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsRow), new PropertyMetadata(null, OnPartChanged));

    private FrameworkElement? _icon;
    private FrameworkElement? _description;

    public SettingsRow()
    {
        SizeChanged += (_, e) => UpdateLayoutState(e.NewSize.Width);
    }

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _icon = GetTemplateChild("Icon") as FrameworkElement;
        _description = GetTemplateChild("DescriptionText") as FrameworkElement;
        UpdateParts();
        UpdateLayoutState(ActualWidth);
    }

    private static void OnPartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SettingsRow)d).UpdateParts();

    private void UpdateParts()
    {
        if (_icon is not null)
        {
            _icon.Visibility = string.IsNullOrEmpty(Glyph) ? Visibility.Collapsed : Visibility.Visible;
        }
        if (_description is not null)
        {
            _description.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void UpdateLayoutState(double width)
    {
        // Zero before the first layout pass; stay wide rather than flashing narrow.
        bool narrow = width > 0 && width < NarrowWidth;
        VisualStateManager.GoToState(this, narrow ? "Narrow" : "Wide", false);
    }
}
