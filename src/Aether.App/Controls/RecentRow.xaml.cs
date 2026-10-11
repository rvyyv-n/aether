using System.Globalization;
using Aether.Core.Playback;
using Aether.Core.Playback.Recents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Aether_App.Controls;

/// <summary>One row of the empty state's recents list (2.10), or its missing-file state (2.18).</summary>
public sealed partial class RecentRow : UserControl
{
    private const string FileGlyph = ""; // Video
    private const string LinkGlyph = ""; // Link

    public RecentRow(RecentItem item, bool missing, DateTimeOffset now)
    {
        InitializeComponent();
        Item = item;

        Icon.Glyph = item.IsLink ? LinkGlyph : FileGlyph;
        NameText.Text = item.DisplayName;
        string source = RecentDisplay.Source(item);
        if (missing)
        {
            Icon.Foreground = Brush("AetherTextDisabledBrush");
            NameText.Foreground = Brush("AetherTextSecondaryBrush");
            MetaText.Text = $"{AppStrings.Get("EmptyState_FileNotFound")} · {source}";
            DateText.Visibility = Visibility.Collapsed;
            RowButton.IsEnabled = false;
            RemoveButton.Visibility = Visibility.Visible;
            string remove = AppStrings.Get("EmptyState_RemoveFromRecent");
            ToolTipService.SetToolTip(RemoveButton, remove);
            AutomationProperties.SetName(RemoveButton, remove);
        }
        else
        {
            MetaText.Text = item.Duration > 0 ? $"{source} · {Timecode.FormatClock(item.Duration, item.Duration)}" : source;
            DateText.Text = RecentDisplay.Day(item.OpenedAt, now) switch
            {
                RecentDay.Today => AppStrings.Get("EmptyState_Today"),
                RecentDay.Yesterday => AppStrings.Get("EmptyState_Yesterday"),
                _ => item.OpenedAt.ToOffset(now.Offset).ToString("d MMM", CultureInfo.CurrentCulture),
            };
        }
        AutomationProperties.SetName(RowButton, $"{NameText.Text}, {MetaText.Text}");
    }

    public RecentItem Item { get; }

    public event EventHandler? OpenRequested;

    public event EventHandler? RemoveRequested;

    private void OnRowClick(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnRemoveClick(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, EventArgs.Empty);

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
