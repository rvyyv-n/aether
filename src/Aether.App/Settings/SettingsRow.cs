using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings;

/// <summary>
/// A settings row (6.2): icon, title and description on the left, the control (the row's content) on the right.
/// On a narrow page a wide control drops under the text so the page holds at 405 px; a toggle stays beside it.
/// </summary>
[TemplatePart(Name = "Icon", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "DescriptionText", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "NotePresenter", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "StateTextBlock", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "ControlArea", Type = typeof(FrameworkElement))]
[TemplateVisualState(GroupName = "LayoutStates", Name = "Wide")]
[TemplateVisualState(GroupName = "LayoutStates", Name = "Narrow")]
public sealed partial class SettingsRow : ContentControl
{
    /// <summary>Below this row width a control wider than <see cref="NarrowControlShare"/> of the row wraps under the text.</summary>
    private const double NarrowWidth = 460;

    private const double NarrowControlShare = 0.4;

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsRow), new PropertyMetadata(null, OnPartChanged));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsRow), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsRow), new PropertyMetadata(null, OnPartChanged));

    public static readonly DependencyProperty NoteProperty = DependencyProperty.Register(
        nameof(Note), typeof(object), typeof(SettingsRow), new PropertyMetadata(null, OnPartChanged));

    public static readonly DependencyProperty StateTextProperty = DependencyProperty.Register(
        nameof(StateText), typeof(string), typeof(SettingsRow), new PropertyMetadata(null, OnPartChanged));

    private FrameworkElement? _icon;
    private FrameworkElement? _description;
    private FrameworkElement? _note;
    private FrameworkElement? _state;
    private FrameworkElement? _controlArea;

    public SettingsRow()
    {
        SizeChanged += (_, e) => UpdateLayoutState(e.NewSize.Width);
    }

    /// <summary>The row's icon. A single space keeps the icon's slot empty so the title lines up with its neighbours.</summary>
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

    /// <summary>Extra content under the description, lined up with the title (a restart note, a text box).</summary>
    public object? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    /// <summary>A word shown just left of the control, such as On or Off beside a toggle.</summary>
    public string? StateText
    {
        get => (string?)GetValue(StateTextProperty);
        set => SetValue(StateTextProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _icon = GetTemplateChild("Icon") as FrameworkElement;
        _description = GetTemplateChild("DescriptionText") as FrameworkElement;
        _note = GetTemplateChild("NotePresenter") as FrameworkElement;
        _state = GetTemplateChild("StateTextBlock") as FrameworkElement;
        _controlArea = GetTemplateChild("ControlArea") as FrameworkElement;
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
        if (_note is not null)
        {
            _note.Visibility = Note is null ? Visibility.Collapsed : Visibility.Visible;
        }
        if (_state is not null)
        {
            _state.Visibility = string.IsNullOrEmpty(StateText) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void UpdateLayoutState(double width)
    {
        // Zero before the first layout pass; stay wide rather than flashing narrow.
        double control = _controlArea?.DesiredSize.Width ?? 0;
        bool narrow = width > 0 && width < NarrowWidth && control > width * NarrowControlShare;
        VisualStateManager.GoToState(this, narrow ? "Narrow" : "Wide", false);
    }
}
