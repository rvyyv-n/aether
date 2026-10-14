using Aether.Core.Playback;
using Aether.Core.Playback.Clips;
using Aether.PvP.Review.Clipping;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.System;

namespace Aether_App.Controls;

/// <summary>
/// The export chip above the transport pill (5.4) and the drawer of saved clips it grows into (5.3).
/// Exports run through <see cref="App.ClipExport"/>; finished clips go to <see cref="App.Clips"/>.
/// UI thread only, apart from the shelf's Changed event, which is marshalled here.
/// </summary>
public sealed partial class ClipDrawer : UserControl
{
    private const string ChevronUp = "";
    private const string ChevronDown = "";

    private readonly List<PendingExport> _pending = [];
    private readonly Dictionary<string, ImageSource?> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private ClipRange _range;
    private string? _sourcePath;
    private double _duration;
    private bool _ffmpegChecked;
    private bool _open;

    public ClipDrawer()
    {
        InitializeComponent();
        ExportButton.Content = AppStrings.Get("Transport_ExportClip");
        ShowAllButton.Content = AppStrings.Get("Clips_ShowAllInFolder");
        ClipsText.Text = AppStrings.Get("Clips_Clips");
        string clear = AppStrings.Get("Transport_ClearInOut");
        ToolTipService.SetToolTip(ClearButton, clear);
        AutomationProperties.SetName(ClearButton, clear);
        RowsScroller.MaxHeight = ClipDisplay.VisibleRows * (double)Application.Current.Resources["AetherSizeRow"];
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The drawer is open: the controls stay up until it closes.</summary>
    public bool IsOpen => _open;

    /// <summary>A row is being dragged out; the page must not treat it as a file dropped in.</summary>
    public bool IsDraggingOut { get; private set; }

    /// <summary>Raised when <see cref="IsOpen"/> or <see cref="IsDraggingOut"/> changes.</summary>
    public event EventHandler? HoldChanged;

    /// <summary>Clear in and out was clicked.</summary>
    public event EventHandler? ClearRequested;

    /// <summary>The In/Out range and the file it belongs to; <paramref name="duration"/> shapes the clock.</summary>
    public void Update(ClipRange range, string? sourcePath, double duration)
    {
        _range = range;
        _sourcePath = sourcePath;
        _duration = duration;
        RenderChip();
    }

    /// <summary>Opens the drawer, as the Clips entry of the right-click menu does.</summary>
    public void Open() => SetOpen(true);

    /// <summary>Esc closes an open drawer first. Returns whether it did.</summary>
    public bool Close()
    {
        if (!_open)
        {
            return false;
        }
        SetOpen(false);
        return true;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.Clips.Changed += OnShelfChanged;
        RenderRows();
        RenderChip();
        // Pre-warm once the page is idle, so the first export doesn't pay for a cold start.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = PreWarmAsync());
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.Clips.Changed -= OnShelfChanged;
        _lifetime.Cancel();
    }

    private async Task PreWarmAsync()
    {
        if (_ffmpegChecked)
        {
            return;
        }
        try
        {
            await Task.Run(() => App.ClipExport.PreWarmAsync(ct: _lifetime.Token));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        _ffmpegChecked = true;
        RenderChip();
    }

    private void OnShelfChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        RenderRows();
        RenderChip();
    });

    // ---- Chip ----

    private void RenderChip()
    {
        // Until the check has run, Export clip stays enabled; a failed start marks FFmpeg missing then.
        bool ffmpeg = !_ffmpegChecked || App.ClipExport.IsFfmpegAvailable;
        int rows = App.Clips.Items.Count + _pending.Count;
        ClipChipMode mode = ClipDisplay.Chip(_range, rows, ffmpeg);
        bool export = mode is ClipChipMode.Export or ClipChipMode.ExportUnavailable;

        Visibility = mode == ClipChipMode.Hidden && !_open ? Visibility.Collapsed : Visibility.Visible;
        RangeText.Text = export ? _range.Label(_duration) : string.Empty;
        RangeText.Visibility = Show(export);
        ExportButton.Visibility = Show(export);
        ClearButton.Visibility = Show(export);
        ExportButton.IsEnabled = mode == ClipChipMode.Export && _sourcePath is not null;
        ToolTipService.SetToolTip(ExportButton, mode == ClipChipMode.ExportUnavailable ? AppStrings.Get("Clips_NeedsFfmpeg") : null);

        // With a range the chip is the export controls, and the toggle is just the chevron.
        ClipsToggle.Visibility = Show(rows > 0 || _open);
        ClipsText.Visibility = Show(!export);
        if (mode == ClipChipMode.Hidden && _open)
        {
            SetOpen(false);
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);

    private void OnClipsToggleClick(object sender, RoutedEventArgs e) => SetOpen(!_open);

    private void SetOpen(bool open)
    {
        if (open == _open)
        {
            ClipsToggle.IsChecked = open;
            return;
        }
        _open = open;
        ClipsToggle.IsChecked = open;
        Chevron.Glyph = open ? ChevronDown : ChevronUp;
        string label = AppStrings.Get(open ? "Clips_HideClips" : "Clips_ShowClips");
        ToolTipService.SetToolTip(ClipsToggle, label);
        AutomationProperties.SetName(ClipsToggle, label);
        DrawerArea.Visibility = Show(open);
        double width = (double)Application.Current.Resources["AetherSizeClipDrawerWidth"];
        Panel.Width = open ? Math.Min(width, ActualWidth > 0 ? ActualWidth : width) : double.NaN; // narrow windows

        RowsScroller.ChangeView(null, 0, null, disableAnimation: true);
        RenderChip();
        HoldChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Export ----

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_sourcePath is not { } source || !_range.IsComplete)
        {
            return;
        }
        var pending = new PendingExport(ClipDisplay.NamePrefix(source));
        _pending.Insert(0, pending);
        SetOpen(true);
        RenderRows();

        var request = new ClipRequest(source, _range.In!.Value, _range.Out!.Value, ClipDisplay.DefaultFolder(), pending.Name);
        double length = _range.Length;
        try
        {
            ClipResult result = await Task.Run(() => App.ClipExport.ExportAsync(request, _lifetime.Token));
            _pending.Remove(pending);
            var clip = new SavedClip(result.OutputPath, result.SizeBytes, length, DateTimeOffset.Now);
            _ = Task.Run(() => App.Clips.Add(clip)); // Changed redraws the rows
            AppToasts.Show("clip-saved", AppStrings.Get("Toast_ClipSaved"), ToastTone.Success,
                AppStrings.Get("Toast_ShowInFolder"), () => _ = ShowInFolderAsync(result.OutputPath));
        }
        catch (OperationCanceledException)
        {
            return; // the page is going away
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            pending.Failed = true;
            if (ex is FileNotFoundException { FileName: var missing } && missing != source)
            {
                _ffmpegChecked = true; // FFmpeg itself would not start
            }
            RenderRows();
            RenderChip();
            AppToasts.Show("clip-failed", AppStrings.Get("Toast_ClipFailed"), ToastTone.Failure);
        }
    }

    // ---- Rows ----

    private void RenderRows()
    {
        RowsPanel.Children.Clear();
        foreach (PendingExport pending in _pending)
        {
            var row = new ClipRow(pending.Name, pending.Failed);
            row.DismissRequested += (_, _) =>
            {
                _pending.Remove(pending);
                RenderRows();
                RenderChip();
            };
            RowsPanel.Children.Add(row);
        }

        DateTimeOffset now = DateTimeOffset.Now;
        foreach (SavedClip clip in App.Clips.Items)
        {
            bool missing = ClipDisplay.IsMissing(clip, File.Exists);
            _thumbnails.TryGetValue(clip.Path, out ImageSource? thumbnail);
            var row = new ClipRow(clip, missing, now, thumbnail);
            row.DismissRequested += OnDismissRequested;
            row.ShowInFolderRequested += OnShowInFolderRequested;
            row.DraggingChanged += OnRowDraggingChanged;
            RowsPanel.Children.Add(row);
            if (!missing && !_thumbnails.ContainsKey(clip.Path))
            {
                _ = LoadThumbnailAsync(clip.Path);
            }
        }
        ShowAllButton.Visibility = Show(App.Clips.Items.Count > 0);
    }

    private async Task LoadThumbnailAsync(string path)
    {
        _thumbnails[path] = null; // one attempt per path
        ImageSource? thumbnail = await ClipRow.LoadThumbnailAsync(path);
        if (thumbnail is null)
        {
            return;
        }
        _thumbnails[path] = thumbnail;
        foreach (UIElement child in RowsPanel.Children)
        {
            if (child is ClipRow { Saved: { } clip } row && string.Equals(clip.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                row.ShowThumbnail(thumbnail);
            }
        }
    }

    private void OnDismissRequested(object? sender, EventArgs e)
    {
        if (sender is ClipRow { Saved: { } clip })
        {
            _thumbnails.Remove(clip.Path);
            _ = Task.Run(() => App.Clips.Remove(clip.Path));
        }
    }

    private async void OnShowInFolderRequested(object? sender, EventArgs e)
    {
        if (sender is ClipRow { Saved: { } clip })
        {
            await ShowInFolderAsync(clip.Path);
        }
    }

    private async Task ShowInFolderAsync(string path)
    {
        if (!await Aether_App.ShowInFolder.TryAsync(path))
        {
            RenderRows(); // gone since the list was drawn: show it as missing
        }
    }

    private async void OnShowAllClick(object sender, RoutedEventArgs e)
    {
        string folder = ClipDisplay.DefaultFolder();
        try
        {
            Directory.CreateDirectory(folder);
            await Launcher.LaunchFolderPathAsync(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Nothing to show; the drawer stays as it is.
        }
    }

    private void OnRowDraggingChanged(object? sender, bool dragging)
    {
        IsDraggingOut = dragging;
        HoldChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private sealed class PendingExport(string name)
    {
        public string Name { get; } = name;

        public bool Failed { get; set; }
    }
}
