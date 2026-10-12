using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Aether_App.Settings;

/// <summary>
/// A row that opens to show sub-rows (6.2). The header is the whole row; sub-rows are indented to the title
/// (52 px), 48 tall, with a divider above each.
/// </summary>
[TemplatePart(Name = "HeaderButton", Type = typeof(ToggleButton))]
[TemplatePart(Name = "Icon", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "DescriptionText", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "Chevron", Type = typeof(FontIcon))]
[TemplatePart(Name = "ItemsHost", Type = typeof(FrameworkElement))]
public sealed partial class SettingsExpander : ItemsControl
{
    private const string ExpandGlyph = ""; // ChevronDown
    private const string CollapseGlyph = ""; // ChevronUp

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsExpander), new PropertyMetadata(null, OnPartChanged));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsExpander), new PropertyMetadata(null, OnPartChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsExpander), new PropertyMetadata(null, OnPartChanged));

    public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
        nameof(HeaderContent), typeof(object), typeof(SettingsExpander), new PropertyMetadata(null));

    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(SettingsExpander), new PropertyMetadata(false, OnPartChanged));

    private ToggleButton? _headerButton;
    private FrameworkElement? _icon;
    private FrameworkElement? _description;
    private FontIcon? _chevron;
    private FrameworkElement? _itemsHost;

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

    /// <summary>A control shown on the header row, left of the chevron (e.g. a toggle that enables the group).</summary>
    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        if (_headerButton is not null)
        {
            _headerButton.Click -= OnHeaderClick;
        }
        base.OnApplyTemplate();
        _headerButton = GetTemplateChild("HeaderButton") as ToggleButton;
        _icon = GetTemplateChild("Icon") as FrameworkElement;
        _description = GetTemplateChild("DescriptionText") as FrameworkElement;
        _chevron = GetTemplateChild("Chevron") as FontIcon;
        _itemsHost = GetTemplateChild("ItemsHost") as FrameworkElement;
        if (_headerButton is not null)
        {
            _headerButton.Click += OnHeaderClick;
        }
        UpdateParts();
    }

    protected override bool IsItemItsOwnContainerOverride(object item) => item is FrameworkElement;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is SettingsRow row)
        {
            row.Padding = new Thickness(52, 8, 16, 8);
            row.MinHeight = (double)Application.Current.Resources["AetherSizeSettingsSubRow"];
            row.BorderThickness = new Thickness(0, 1, 0, 0);
            row.BorderBrush = (Brush)Application.Current.Resources["AetherStrokeDividerBrush"];
        }
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e) => IsExpanded = !IsExpanded;

    private static void OnPartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SettingsExpander)d).UpdateParts();

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
        if (_chevron is not null)
        {
            _chevron.Glyph = IsExpanded ? CollapseGlyph : ExpandGlyph;
        }
        if (_itemsHost is not null)
        {
            _itemsHost.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_headerButton is not null)
        {
            // A toggle button so screen readers announce whether the group is open.
            _headerButton.IsChecked = IsExpanded;
            AutomationProperties.SetName(_headerButton, Header);
        }
    }
}
