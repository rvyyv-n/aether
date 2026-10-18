using System.Diagnostics;
using System.Globalization;
using Aether.Core.Playback;
using Aether.Core.Playback.Clips;
using Aether.Core.Playback.Settings;
using Aether.PvP.Review.Clipping;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
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

    private const int DefaultCustomMb = 100;
    private const int CustomTag = -1;
    private static readonly int[] PresetMb = [0, 10, 25, 50];
    private static readonly long ProgressInterval = Stopwatch.Frequency / 4; // about 4 updates a second

    private readonly List<PendingExport> _pending = [];
    private readonly Dictionary<string, ImageSource?> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private ClipRange _range;
    private string? _sourcePath;
    private double _duration;
    private bool _ffmpegChecked;
    private bool _open;
    private ChromeFader? _fader;
    private ExportMode _mode;
    private int _limitMb; // 0 = no limit
    private int _customMb = DefaultCustomMb;
    private bool _custom;
    private bool _settingUpOptions;
    private long? _tooSmallBytes;

    public ClipDrawer()
    {
        InitializeComponent();
        SetUpOptions();
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
        RenderOptions(); // the range decides whether the size limit fits; this redraws the chip too
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
        ExportButton.IsEnabled &= _tooSmallBytes is null;
        ToolTipService.SetToolTip(ExportButton, ExportTip(mode));

        // With a range the chip is the export controls, and the toggle is just the chevron.
        ClipsToggle.Visibility = Show(rows > 0 || _open);
        ClipsText.Visibility = Show(!export);
        if (mode == ClipChipMode.Hidden && _open)
        {
            SetOpen(false);
        }
    }

    private string? ExportTip(ClipChipMode mode)
    {
        if (mode == ClipChipMode.ExportUnavailable)
        {
            return AppStrings.Get("Clips_NeedsFfmpeg");
        }
        if (_tooSmallBytes is { } tooSmall)
        {
            return TooSmallText(tooSmall);
        }
        if (_mode != ExportMode.Precise)
        {
            return null;
        }
        string limit = _limitMb > 0
            ? string.Format(CultureInfo.CurrentCulture, AppStrings.Get("Clips_ExportTipLimit"), _limitMb)
            : AppStrings.Get("Clips_ExportTipNoLimit");
        return string.Format(CultureInfo.CurrentCulture, AppStrings.Get("Clips_ExportTipPrecise"), limit);
    }

    // ---- Export options ----

    private void SetUpOptions()
    {
        ModeLabel.Text = AppStrings.Get("Clips_Mode");
        SizeLabel.Text = AppStrings.Get("Clips_SizeLimit");
        CustomUnit.Text = AppStrings.Get("Clips_Megabytes");
        AutomationProperties.SetName(ModeList, ModeLabel.Text);
        AutomationProperties.SetName(SizeCombo, SizeLabel.Text);
        AutomationProperties.SetName(CustomBox, SizeLabel.Text);

        ModeList.Items.Add(new ListBoxItem { Content = AppStrings.Get("Clips_ModeFast"), Tag = ExportMode.Fast });
        ModeList.Items.Add(new ListBoxItem { Content = AppStrings.Get("Clips_ModePrecise"), Tag = ExportMode.Precise });
        foreach (int mb in PresetMb)
        {
            string text = mb == 0
                ? AppStrings.Get("Clips_SizeNone")
                : string.Format(CultureInfo.CurrentCulture, AppStrings.Get("Clips_SizePreset"), mb);
            SizeCombo.Items.Add(new ComboBoxItem { Content = text, Tag = mb });
        }
        SizeCombo.Items.Add(new ComboBoxItem { Content = AppStrings.Get("Clips_SizeCustom"), Tag = CustomTag });

        AetherSettings settings = App.Settings.Current;
        _mode = settings.ClipExportMode == "precise" ? ExportMode.Precise : ExportMode.Fast;
        _limitMb = settings.ClipSizeLimitMb;
        _custom = _limitMb > 0 && Array.IndexOf(PresetMb, _limitMb) < 0;
        if (_custom)
        {
            _customMb = _limitMb;
        }

        _settingUpOptions = true;
        ModeList.SelectedIndex = _mode == ExportMode.Precise ? 1 : 0;
        SizeCombo.SelectedIndex = _custom ? PresetMb.Length : Array.IndexOf(PresetMb, _limitMb);
        CustomBox.Text = _customMb.ToString(CultureInfo.CurrentCulture);
        _settingUpOptions = false;
        RenderOptionsBlock();
    }

    private void RenderOptions()
    {
        RenderOptionsBlock();
        RenderChip();
    }

    private void RenderOptionsBlock()
    {
        bool precise = _mode == ExportMode.Precise;
        SizeCombo.IsEnabled = precise;
        CustomBox.IsEnabled = precise;
        CustomRow.Visibility = Show(_custom);
        _tooSmallBytes = precise && _range.IsComplete ? ClipSizeLimit.MinimumIfTooSmall(_limitMb, _range.Length) : null;

        string hint = _tooSmallBytes is { } tooSmall
            ? TooSmallText(tooSmall)
            : AppStrings.Get(precise ? "Clips_HintPrecise" : "Clips_HintFast");
        HintText.Text = hint;
        HintText.Foreground = (Brush)Application.Current.Resources[_tooSmallBytes is null ? "AetherTextSecondaryBrush" : "AetherStatusWarningBrush"];
        AutomationProperties.SetHelpText(ModeList, hint);
    }

    private static string TooSmallText(long minimumBytes) =>
        string.Format(CultureInfo.CurrentCulture, AppStrings.Get("Clips_HintTooSmall"),
            ClipSizeLimit.RoundUpMegabytes(minimumBytes).ToString("0.0", CultureInfo.CurrentCulture));

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingUpOptions || ModeList.SelectedItem is not ListBoxItem { Tag: ExportMode mode })
        {
            return;
        }
        _mode = mode;
        App.Settings.Set(SettingKeys.ClipExportMode, mode == ExportMode.Precise ? "precise" : "fast");
        RenderOptions();
    }

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingUpOptions || SizeCombo.SelectedItem is not ComboBoxItem { Tag: int tag })
        {
            return;
        }
        _custom = tag == CustomTag;
        _limitMb = _custom ? _customMb : tag;
        if (_custom)
        {
            CustomBox.Text = _customMb.ToString(CultureInfo.CurrentCulture);
        }
        App.Settings.Set(SettingKeys.ClipSizeLimitMb, _limitMb);
        RenderOptions();
    }

    private void OnCustomBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args) =>
        args.Cancel = !args.NewText.All(char.IsAsciiDigit);

    private void OnCustomTextChanged(object sender, TextChangedEventArgs e)
    {
        // An empty or out-of-range field keeps the last good value until it loses focus.
        if (_settingUpOptions || !_custom
            || !int.TryParse(CustomBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int mb)
            || mb is < 1 or > ClipSizeLimit.MaxMegabytes)
        {
            return;
        }
        _customMb = mb;
        _limitMb = mb;
        App.Settings.Set(SettingKeys.ClipSizeLimitMb, mb);
        RenderOptions();
    }

    private void OnCustomLostFocus(object sender, RoutedEventArgs e) =>
        CustomBox.Text = _customMb.ToString(CultureInfo.CurrentCulture);

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
        _fader ??= ChromeFader.ForElement(this);
        double width = (double)Application.Current.Resources["AetherSizeClipDrawerWidth"];
        if (open)
        {
            Panel.Width = Math.Min(width, ActualWidth > 0 ? ActualWidth : width); // narrow windows
            _fader.Enter(DrawerArea, ChromeFader.FromBelow);
        }
        else
        {
            // The chip keeps its open width until the rows have gone, so they do not reflow as they fade.
            _fader.Exit(DrawerArea, ChromeFader.FromBelow, () => Panel.Width = double.NaN);
        }

        RowsScroller.ChangeView(null, 0, null, disableAnimation: true);
        RenderChip();
        HoldChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Export ----

    /// <summary>The export shortcut: exports the marked clip, or says nothing is marked.</summary>
    public void RequestExport()
    {
        if (!_range.IsComplete)
        {
            AppToasts.Show("clip-unmarked", AppStrings.Get("Toast_ClipNothingMarked"), ToastTone.Neutral);
            return;
        }
        if (!_ffmpegChecked || App.ClipExport.IsFfmpegAvailable)
        {
            ExportClip();
        }
        else
        {
            AppToasts.Show("clip-ffmpeg", AppStrings.Get("Clips_NeedsFfmpeg"), ToastTone.Neutral);
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e) => ExportClip();

    private async void ExportClip()
    {
        if (_sourcePath is not { } source || !_range.IsComplete)
        {
            return;
        }
        if (WebLink.IsValid(source))
        {
            AppToasts.Show("clip-stream", AppStrings.Get("Toast_ClipStreamNotSupported"), ToastTone.Neutral);
            return;
        }
        if (_tooSmallBytes is { } tooSmall)
        {
            AppToasts.Show("clip-toosmall", TooSmallText(tooSmall), ToastTone.Neutral);
            return;
        }
        bool precise = _mode == ExportMode.Precise;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var pending = new PendingExport(ClipDisplay.NamePrefix(source), cancel);
        _pending.Insert(0, pending);
        SetOpen(true);
        RenderRows();

        var request = new ClipRequest(source, _range.In!.Value, _range.Out!.Value, ClipDisplay.DefaultFolder(), pending.Name,
            _mode, precise ? ClipSizeLimit.ToBytes(_limitMb) : null);
        double length = _range.Length;
        // Created here, so its updates arrive on the UI thread.
        IProgress<ClipProgress>? progress = precise ? new Progress<ClipProgress>(p => OnExportProgress(pending, p)) : null;
        try
        {
            ClipResult result = await Task.Run(() => App.ClipExport.ExportAsync(request, cancel.Token, progress));
            _pending.Remove(pending);
            var clip = new SavedClip(result.OutputPath, result.SizeBytes, length, DateTimeOffset.Now, result.EncoderUsed?.ToString());
            _ = Task.Run(() => App.Clips.Add(clip)); // Changed redraws the rows
            AppToasts.Show("clip-saved", AppStrings.Get("Toast_ClipSaved"), ToastTone.Success,
                AppStrings.Get("Toast_ShowInFolder"), () => _ = ShowInFolderAsync(result.OutputPath));
            Announce(AppStrings.Get("Toast_ClipSaved"));
        }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                _pending.Remove(pending); // cancelled from its row: gone without a word
                RenderRows();
                RenderChip();
            }
            return; // otherwise the page is going away
        }
        catch (ClipTargetTooSmallException ex)
        {
            pending.Failed = true;
            pending.TooSmallBytes = ex.MinimumSizeBytes;
            RenderRows();
            RenderChip();
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

    private void OnExportProgress(PendingExport pending, ClipProgress progress)
    {
        long now = Stopwatch.GetTimestamp();
        if (pending.Failed || now - pending.LastProgressAt < ProgressInterval)
        {
            return;
        }
        pending.LastProgressAt = now;
        pending.Latest = progress;
        pending.Row?.SetProgress(progress);
    }

    // Polite, so a screen reader says it once rather than on every percent.
    private void Announce(string text)
    {
        AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(HintText) ?? FrameworkElementAutomationPeer.CreatePeerForElement(HintText);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent, text, "ClipSaved");
    }

    // ---- Rows ----

    private void RenderRows()
    {
        RowsPanel.Children.Clear();
        foreach (PendingExport pending in _pending)
        {
            var row = new ClipRow(pending.Name, pending.Failed, pending.TooSmallBytes is { } tooSmall ? TooSmallText(tooSmall) : null);
            pending.Row = row;
            if (pending.Latest is { } latest)
            {
                row.SetProgress(latest);
            }
            row.CancelRequested += (_, _) => pending.Cancel.Cancel();
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

    private sealed class PendingExport(string name, CancellationTokenSource cancel)
    {
        public string Name { get; } = name;

        public CancellationTokenSource Cancel { get; } = cancel;

        public bool Failed { get; set; }

        /// <summary>Set when the size limit turned out too small only after the export began.</summary>
        public long? TooSmallBytes { get; set; }

        public ClipProgress? Latest { get; set; }

        public long LastProgressAt { get; set; }

        public ClipRow? Row { get; set; }
    }
}
