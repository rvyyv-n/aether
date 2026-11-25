using System.Globalization;
using System.IO;
using Aether.Core.Playback;
using Aether.Core.Playback.Recents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Aether_App.Controls;

/// <summary>One minimalist 16:9 glass card of the Home recents grid (22.4), or its missing-file state (2.18).</summary>
public sealed partial class RecentRow : UserControl
{
    private const string FileGlyph = ""; // Video
    private const string LinkGlyph = ""; // Link

    public RecentRow(RecentItem item, bool missing, DateTimeOffset now, double resumeProgress = 0)
    {
        InitializeComponent();
        Item = item;

        Icon.Glyph = item.IsLink ? LinkGlyph : FileGlyph;
        NameText.Text = item.DisplayName;
        ToolTipService.SetToolTip(RowButton, item.Location);

        string source = RecentDisplay.ShortSource(item);
        if (missing)
        {
            // The card is not in the tree yet, so its theme is only known once it loads.
            Loaded += (_, _) => ApplyMissingBrushes();
            ActualThemeChanged += (_, _) => ApplyMissingBrushes();
            MetaText.Text = AppStrings.Get("EmptyState_FileNotFound");
            RowButton.IsEnabled = false;
            RemoveButton.Visibility = Visibility.Visible;
            string remove = AppStrings.Get("EmptyState_RemoveFromRecent");
            ToolTipService.SetToolTip(RemoveButton, remove);
            AutomationProperties.SetName(RemoveButton, remove);
        }
        else
        {
            if (item.Duration > 0)
            {
                DurationText.Text = Timecode.FormatClock(item.Duration, item.Duration);
                DurationBadge.Visibility = Visibility.Visible;
            }

            string dayText = RecentDisplay.Day(item.OpenedAt, now) switch
            {
                RecentDay.Today => AppStrings.Get("EmptyState_Today"),
                RecentDay.Yesterday => AppStrings.Get("EmptyState_Yesterday"),
                _ => item.OpenedAt.ToOffset(now.Offset).ToString("d MMM", CultureInfo.CurrentCulture),
            };

            double clampedProgress = double.IsFinite(resumeProgress) ? Math.Clamp(resumeProgress, 0, 1) : 0;
            if (clampedProgress > 0)
            {
                ResumeFilledCol.Width = new GridLength(clampedProgress, GridUnitType.Star);
                ResumeEmptyCol.Width = new GridLength(Math.Max(0.001, 1 - clampedProgress), GridUnitType.Star);
                ResumeTrack.Visibility = Visibility.Visible;
                int percent = Math.Clamp((int)Math.Round(clampedProgress * 100), 1, 99);
                MetaText.Text = $"{dayText} · {percent}%";
            }
            else
            {
                MetaText.Text = $"{dayText} · {source}";
            }
        }

        AutomationProperties.SetName(RowButton, $"{NameText.Text}, {MetaText.Text}");
    }

    public RecentItem Item { get; }

    public event EventHandler? OpenRequested;

    public event EventHandler? RemoveRequested;

    /// <summary>Displays cached JPEG/PNG bytes without holding a file lock on the thumbnail cache.</summary>
    public async Task SetThumbnailBytesAsync(byte[] encodedBytes)
    {
        if (encodedBytes.Length == 0)
        {
            return;
        }
        try
        {
            using var ms = new MemoryStream(encodedBytes, writable: false);
            using IRandomAccessStream stream = ms.AsRandomAccessStream();
            var bitmap = new BitmapImage { DecodePixelWidth = 320 };
            await bitmap.SetSourceAsync(stream);
            ThumbnailImage.Source = bitmap;
            ThumbnailImage.Visibility = Visibility.Visible;
            Icon.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            // Leave the clean glyph fallback in place if the cached image cannot be decoded.
        }
    }

    private void OnRowClick(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnRemoveClick(object sender, RoutedEventArgs e) => RemoveRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyMissingBrushes()
    {
        Icon.Foreground = Brush("AetherTextDisabledBrush");
        NameText.Foreground = Brush("AetherTextSecondaryBrush");
    }

    private Brush Brush(string key) => ThemeTokens.Get<Brush>(this, key);
}
