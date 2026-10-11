using System.Globalization;
using Aether.Core.Playback.Clips;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Aether_App.Controls;

/// <summary>
/// One row of the clip drawer (5.3): a saved clip, its missing-file state, or an export in progress or
/// failed. UI thread only.
/// </summary>
public sealed partial class ClipRow : UserControl
{
    private const string VideoGlyph = "";
    private const string MissingGlyph = ""; // Warning
    private const uint ThumbnailSize = 96; // the shell picks the nearest cached size; 64 px at 150% scale

    private bool _hovered;
    private bool _focused;

    private ClipRow()
    {
        InitializeComponent();
        Glyph.Glyph = VideoGlyph;
        Label(FolderButton, "Clips_ShowInFolder");
        Label(DismissButton, "Clips_Dismiss");
    }

    /// <summary>A saved clip; <paramref name="missing"/> when its file was deleted or moved.</summary>
    public ClipRow(SavedClip clip, bool missing, DateTimeOffset now, ImageSource? thumbnail)
        : this()
    {
        Saved = clip;
        NameText.Text = clip.Name;
        if (missing)
        {
            Glyph.Glyph = MissingGlyph;
            Glyph.Foreground = Brush("AetherTextDisabledBrush");
            NameText.Foreground = Brush("AetherTextSecondaryBrush");
            DetailsText.Text = AppStrings.Get("Clips_FileNotFound");
            FolderButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            DetailsText.Text = ClipDisplay.Details(clip, now, CultureInfo.CurrentCulture, AppStrings.Get("Clips_Yesterday"));
            Thumbnail.Source = thumbnail;
            Root.CanDrag = true;
        }
        AutomationProperties.SetName(this, $"{NameText.Text}, {DetailsText.Text}");
    }

    /// <summary>An export still running (<paramref name="failed"/> false) or one that failed.</summary>
    public ClipRow(string name, bool failed)
        : this()
    {
        NameText.Text = name;
        FolderButton.Visibility = Visibility.Collapsed;
        if (failed)
        {
            Glyph.Glyph = MissingGlyph;
            DetailsText.Text = AppStrings.Get("Clips_Failed");
        }
        else
        {
            DetailsText.Visibility = Visibility.Collapsed;
            Progress.Visibility = Visibility.Visible;
            DismissButton.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(this, $"{name}, {AppStrings.Get("Clips_Saving")}");
            return;
        }
        AutomationProperties.SetName(this, $"{NameText.Text}, {DetailsText.Text}");
    }

    /// <summary>Fills the thumbnail once it has loaded.</summary>
    public void ShowThumbnail(ImageSource thumbnail) => Thumbnail.Source = thumbnail;

    /// <summary>The saved clip, or null for an export row.</summary>
    public SavedClip? Saved { get; }

    public event EventHandler? DismissRequested;

    public event EventHandler? ShowInFolderRequested;

    /// <summary>True from the start of a drag out until it is dropped or cancelled.</summary>
    public event EventHandler<bool>? DraggingChanged;

    /// <summary>The shell's thumbnail of the clip's first frame (its In point); null when there is none.</summary>
    public static async Task<ImageSource?> LoadThumbnailAsync(string path)
    {
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            using StorageItemThumbnail thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, ThumbnailSize);
            if (thumbnail is null || thumbnail.Type != ThumbnailType.Image)
            {
                return null; // an icon, not a frame: keep the glyph
            }
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);
            return bitmap;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private async void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (Saved is not { } clip)
        {
            args.Cancel = true;
            return;
        }
        var deferral = args.GetDeferral();
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(clip.Path);
            args.Data.SetStorageItems([file], readOnly: true);
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            args.AllowedOperations = DataPackageOperation.Copy;
            DraggingChanged?.Invoke(this, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            args.Cancel = true; // deleted since the list was drawn
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnDropCompleted(UIElement sender, DropCompletedEventArgs args) => DraggingChanged?.Invoke(this, false);

    private void OnFolderClick(object sender, RoutedEventArgs e) => ShowInFolderRequested?.Invoke(this, EventArgs.Empty);

    private void OnDismissClick(object sender, RoutedEventArgs e) => DismissRequested?.Invoke(this, EventArgs.Empty);

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovered = true;
        ShowActions();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovered = false;
        ShowActions();
    }

    private void OnFocusChanged(object sender, RoutedEventArgs e)
    {
        _focused = FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && IsInside(focused);
        ShowActions();
    }

    private void ShowActions()
    {
        bool shown = _hovered || _focused;
        Actions.Opacity = shown ? 1 : 0;
        Root.Background = _hovered ? Brush("AetherFillSubtleHoverBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private bool IsInside(DependencyObject element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, this))
            {
                return true;
            }
        }
        return false;
    }

    private static void Label(Button button, string key)
    {
        string text = AppStrings.Get(key);
        ToolTipService.SetToolTip(button, text);
        AutomationProperties.SetName(button, text);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
