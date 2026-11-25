using System.Text;
using System.Text.Json;
using Aether.Core.Interop.DirectX;
using Aether.Core.Playback;
using Aether.Core.Playback.Clips;
using Aether.Core.Playback.Diagnostics;
using Aether.Core.Playback.Settings;
using Aether_App.Settings;
using Aether_App.Settings.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Aether_App;

/// <summary>
/// Isolated in-app visual snapshot mode (<c>--visual-audit &lt;dir&gt; [--video &lt;path&gt;] [--shots a,b]</c>).
/// Composites the D3D11 video + GPU backdrop blur back buffer with WinUI's XAML visual tree and open
/// popup roots, writes deterministic PNGs plus layout/overlap telemetry to <c>manifest.json</c>, and exits cleanly.
/// </summary>
public sealed partial class MainPage
{
    private sealed record AuditRect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;

        public bool Intersects(AuditRect other) =>
            Width > 0 && Height > 0 && other.Width > 0 && other.Height > 0
            && X < other.Right && Right > other.X
            && Y < other.Bottom && Bottom > other.Y;
    }

    private sealed record AuditShotReport(
        string Id,
        string FileName,
        string Theme,
        double LogicalWidth,
        double LogicalHeight,
        double RasterizationScale,
        int PixelWidth,
        int PixelHeight,
        bool VideoFrameCaptured,
        int OpenPopupCount,
        bool AnyPopupOverlapsSeekBar,
        bool ClipChipVisible,
        double SettingsMaxRightOverflowDip,
        VisualImageStats Stats,
        string? PopupDebug,
        string? Error);

    internal async Task RunVisualAuditAsync(MainWindow window, VisualAuditOptions options)
    {
        var reports = new List<AuditShotReport>();
        string? fatalError = null;
        try
        {
            Directory.CreateDirectory(options.OutputDirectory);

            if (!await WaitUntilAsync(() => _isLoaded && XamlRoot is not null && ActualWidth > 0 && ActualHeight > 0 && _session is not null, 15000))
            {
                fatalError = "Timed out waiting for MainPage and playback engine to initialize.";
                VisualAuditSession.ExitCode = 1;
                return;
            }

            SeedAuditRecents(options.VideoPath);
            await DelayAndFlushAsync(350);

            // 1. Home states (Dark, OLED, Light)
            await CaptureHomeShotsAsync(window, options, reports);

            // 2. Settings states (wide, OLED, Shortcuts resize regression check, compact breakpoint, Video & Playback)
            await CaptureSettingsShotsAsync(window, options, reports);

            // 3. Playback, menus, flyouts, clips drawer, sidebar rail, and queue panel
            if (!string.IsNullOrWhiteSpace(options.VideoPath) && File.Exists(options.VideoPath))
            {
                await CapturePlaybackShotsAsync(window, options, reports);
            }

            VisualAuditSession.ExitCode = reports.Any(r => r.Error is not null) ? 2 : 0;
        }
        catch (Exception ex)
        {
            fatalError = ex.ToString();
            VisualAuditSession.ExitCode = 1;
        }
        finally
        {
            try
            {
                WriteAuditManifest(options, reports, fatalError);
            }
            catch (Exception)
            {
                VisualAuditSession.ExitCode = 1;
            }
            window.Close();
        }
    }

    private void SeedAuditRecents(string? videoPath)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        if (!string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
        {
            App.Recents.Add(videoPath, Path.GetFileName(videoPath), now - TimeSpan.FromMinutes(12));
            App.Recents.SetDuration(videoPath, 15);
            App.Resume.Save(videoPath, 4.5, 15, now - TimeSpan.FromMinutes(12));
        }
        else
        {
            App.Recents.Add(@"C:\Videos\Apex_Ranked_FinalRing_1440p60.mp4", "Apex_Ranked_FinalRing_1440p60.mp4", now - TimeSpan.FromMinutes(12));
            App.Recents.SetDuration(@"C:\Videos\Apex_Ranked_FinalRing_1440p60.mp4", 412);
        }
        App.Recents.Add(@"C:\Videos\Dune_Part_Two_HDR_Sample.mkv", "Dune_Part_Two_HDR_Sample.mkv", now - TimeSpan.FromHours(3));
        App.Recents.SetDuration(@"C:\Videos\Dune_Part_Two_HDR_Sample.mkv", 1840);
        App.Recents.Add("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "4K HDR OLED Reference Demo", now - TimeSpan.FromDays(1));
        App.Recents.SetDuration("https://www.youtube.com/watch?v=dQw4w9WgXcQ", 625);
        RefreshRecents();
    }

    private async Task CaptureHomeShotsAsync(MainWindow window, VisualAuditOptions options, List<AuditShotReport> reports)
    {
        SetSettingsShown(false);
        DismissAuditOverlays();

        if (options.MatchesShot("01-home-dark"))
        {
            await ConfigureWindowAndThemeAsync(window, 1280, 760, ThemeModes.Dark);
            reports.Add(await CaptureShotAsync(options, "01-home-dark", ThemeModes.Dark, expectVideo: false));
        }
        if (options.MatchesShot("02-home-oled"))
        {
            await ConfigureWindowAndThemeAsync(window, 1280, 760, ThemeModes.Oled);
            reports.Add(await CaptureShotAsync(options, "02-home-oled", ThemeModes.Oled, expectVideo: false));
        }
        if (options.MatchesShot("03-home-light"))
        {
            await ConfigureWindowAndThemeAsync(window, 1280, 760, ThemeModes.Light);
            reports.Add(await CaptureShotAsync(options, "03-home-light", ThemeModes.Light, expectVideo: false));
        }
    }

    private async Task CaptureSettingsShotsAsync(MainWindow window, VisualAuditOptions options, List<AuditShotReport> reports)
    {
        bool anySettings = options.MatchesShot("04-settings-general-wide")
            || options.MatchesShot("05-settings-general-oled")
            || options.MatchesShot("06-settings-shortcuts-wide")
            || options.MatchesShot("07-settings-shortcuts-resized")
            || options.MatchesShot("08-settings-shortcuts-compact")
            || options.MatchesShot("09-settings-video")
            || options.MatchesShot("10-settings-playback");
        if (!anySettings)
        {
            return;
        }

        await ConfigureWindowAndThemeAsync(window, 1440, 860, ThemeModes.Dark);
        SetSettingsShown(true);
        await DelayAndFlushAsync(450);

        if (SettingsHost.Child is not SettingsView settingsView)
        {
            return;
        }

        if (options.MatchesShot("04-settings-general-wide"))
        {
            ShowSettingsPage(settingsView, typeof(GeneralPage));
            await DelayAndFlushAsync(350);
            reports.Add(await CaptureShotAsync(options, "04-settings-general-wide", ThemeModes.Dark, expectVideo: false, measureSettingsOverflow: true));
        }
        if (options.MatchesShot("05-settings-general-oled"))
        {
            await ConfigureWindowAndThemeAsync(window, 1440, 860, ThemeModes.Oled);
            ShowSettingsPage(settingsView, typeof(GeneralPage));
            await DelayAndFlushAsync(350);
            reports.Add(await CaptureShotAsync(options, "05-settings-general-oled", ThemeModes.Oled, expectVideo: false, measureSettingsOverflow: true));
            await ConfigureWindowAndThemeAsync(window, 1440, 860, ThemeModes.Dark);
        }
        if (options.MatchesShot("06-settings-shortcuts-wide"))
        {
            ShowSettingsPage(settingsView, typeof(ShortcutsPage));
            await DelayAndFlushAsync(400);
            reports.Add(await CaptureShotAsync(options, "06-settings-shortcuts-wide", ThemeModes.Dark, expectVideo: false, measureSettingsOverflow: true));
        }
        if (options.MatchesShot("07-settings-shortcuts-resized"))
        {
            ShowSettingsPage(settingsView, typeof(ShortcutsPage));
            window.ResizeClientForAudit(1180, 740);
            await DelayAndFlushAsync(450);
            reports.Add(await CaptureShotAsync(options, "07-settings-shortcuts-resized", ThemeModes.Dark, expectVideo: false, measureSettingsOverflow: true));
        }
        if (options.MatchesShot("08-settings-shortcuts-compact"))
        {
            ShowSettingsPage(settingsView, typeof(ShortcutsPage));
            window.ResizeClientForAudit(640, 700);
            await DelayAndFlushAsync(450);
            reports.Add(await CaptureShotAsync(options, "08-settings-shortcuts-compact", ThemeModes.Dark, expectVideo: false, measureSettingsOverflow: true));
        }
        if (options.MatchesShot("09-settings-video"))
        {
            await ConfigureWindowAndThemeAsync(window, 1280, 780, ThemeModes.Dark);
            ShowSettingsPage(settingsView, typeof(VideoPage));
            await DelayAndFlushAsync(400);
            reports.Add(await CaptureShotAsync(options, "09-settings-video", ThemeModes.Dark, expectVideo: false, measureSettingsOverflow: true));
        }
        if (options.MatchesShot("10-settings-playback"))
        {
            await ConfigureWindowAndThemeAsync(window, 1280, 780, ThemeModes.Dark);
            ShowSettingsPage(settingsView, typeof(PlaybackPage));
            await DelayAndFlushAsync(400);
            reports.Add(await CaptureShotAsync(options, "10-settings-playback", ThemeModes.Dark, expectVideo: false, measureSettingsOverflow: true));
        }

        SetSettingsShown(false);
        await DelayAndFlushAsync(350);
    }

    private async Task CapturePlaybackShotsAsync(MainWindow window, VisualAuditOptions options, List<AuditShotReport> reports)
    {
        bool anyPlayback = options.MatchesShot("11-player-transport")
            || options.MatchesShot("12-player-open-menu")
            || options.MatchesShot("13-player-more-menu")
            || options.MatchesShot("14-player-tracks-flyout")
            || options.MatchesShot("15-player-video-flyout")
            || options.MatchesShot("16-player-clips-drawer")
            || options.MatchesShot("17-player-sidebar-open")
            || options.MatchesShot("18-player-queue-panel");
        if (!anyPlayback)
        {
            return;
        }

        await ConfigureWindowAndThemeAsync(window, 1280, 760, ThemeModes.Dark);

        // Seed 2 clips so the Clips chip & drawer states (including Show all in folder) are visibly exercised.
        DateTimeOffset now = DateTimeOffset.Now;
        App.Clips.Add(new SavedClip(options.VideoPath!, 4_820_000, 5.4, now - TimeSpan.FromMinutes(4), "Nvenc"));
        App.Clips.Add(new SavedClip(@"C:\Videos\Missing_Audit_Clip_Sample.mp4", 2_140_000, 3.2, now - TimeSpan.FromMinutes(18)));

        await WaitUntilAsync(() => VideoSurface.IsPlayerReady && _session is not null, 10000);
        Open(options.VideoPath!);

        bool videoReady = await WaitUntilAsync(
            () => HasVideo
                  && (_session?.ViewModel.VideoWidth ?? 0) > 0
                  && (VideoSurface.RenderLoop?.FramesPresented ?? 0) >= 2,
            12000);

        if (!videoReady)
        {
            reports.Add(new AuditShotReport(
                "11-player-transport", "11-player-transport.png", ThemeModes.Dark,
                ActualWidth, ActualHeight, XamlRoot?.RasterizationScale ?? 1.0,
                0, 0, false, 0, false, false, 0, new VisualImageStats(0, 0, 0, 0, 0, true), null,
                "Timed out waiting for decoded video frame."));
            return;
        }

        // Hold on a deterministic paused frame ~25% into the clip at a fixed 1280x760 window size.
        double duration = _session?.ViewModel.Duration ?? 0;
        double targetSec = duration > 4 ? Math.Min(2.0, duration * 0.25) : 0.5;
        TrySend(() =>
        {
            _session?.Seek(targetSec, dragging: false);
            _session?.Pause();
        });
        await DelayAndFlushAsync(450);
        window.ResizeClientForAudit(1280, 760);
        NotifyActivity();
        await DelayAndFlushAsync(500);
        PostBackdropRegion();
        await DelayAndFlushAsync(250);

        if (options.MatchesShot("11-player-transport"))
        {
            DismissAuditOverlays();
            await DelayAndFlushAsync(350);
            reports.Add(await CaptureShotAsync(options, "11-player-transport", ThemeModes.Dark, expectVideo: true));
        }

        if (options.MatchesShot("12-player-open-menu"))
        {
            DismissAuditOverlays();
            await DelayAndFlushAsync(250);
            ShowBarFlyout(OpenMenu, OpenButton, FlyoutPlacementMode.TopEdgeAlignedLeft);
            await WaitForPopupCountAsync(1, 2000);
            await DelayAndFlushAsync(350);
            reports.Add(await CaptureShotAsync(options, "12-player-open-menu", ThemeModes.Dark, expectVideo: true));
            OpenMenu.Hide();
            await DelayAndFlushAsync(250);
        }

        if (options.MatchesShot("13-player-more-menu"))
        {
            DismissAuditOverlays();
            await DelayAndFlushAsync(250);
            ShowBarFlyout(MoreMenu, MoreButton, FlyoutPlacementMode.TopEdgeAlignedRight);
            await WaitForPopupCountAsync(1, 2000);
            await DelayAndFlushAsync(350);
            reports.Add(await CaptureShotAsync(options, "13-player-more-menu", ThemeModes.Dark, expectVideo: true));
            MoreMenu.Hide();
            await DelayAndFlushAsync(250);
        }

        if (options.MatchesShot("14-player-tracks-flyout"))
        {
            DismissAuditOverlays();
            await DelayAndFlushAsync(250);
            ShowTracks();
            await WaitForPopupCountAsync(1, 3000);
            await DelayAndFlushAsync(450);
            reports.Add(await CaptureShotAsync(options, "14-player-tracks-flyout", ThemeModes.Dark, expectVideo: true));
            TracksFlyout.Hide();
            await DelayAndFlushAsync(250);
        }

        if (options.MatchesShot("15-player-video-flyout"))
        {
            DismissAuditOverlays();
            await DelayAndFlushAsync(250);
            ShowVideo();
            await WaitForPopupCountAsync(1, 2000);
            await DelayAndFlushAsync(450);
            reports.Add(await CaptureShotAsync(options, "15-player-video-flyout", ThemeModes.Dark, expectVideo: true));
            VideoFlyout.Hide();
            await DelayAndFlushAsync(250);
        }

        if (options.MatchesShot("16-player-clips-drawer"))
        {
            DismissAuditOverlays();
            await DelayAndFlushAsync(250);
            EnsureClipLayer();
            ClipDrawer?.Open();
            await DelayAndFlushAsync(450);
            reports.Add(await CaptureShotAsync(options, "16-player-clips-drawer", ThemeModes.Dark, expectVideo: true));
            ClipDrawer?.Close();
            await DelayAndFlushAsync(300);
        }

        if (options.MatchesShot("17-player-sidebar-open"))
        {
            DismissAuditOverlays();
            await DelayAndFlushAsync(250);
            NotifyActivity();
            _rail.SetPinned(true);
            SyncRail();
            await DelayAndFlushAsync(500);
            reports.Add(await CaptureShotAsync(options, "17-player-sidebar-open", ThemeModes.Dark, expectVideo: true));
            CloseRail();
            await DelayAndFlushAsync(300);
        }

        if (options.MatchesShot("18-player-queue-panel"))
        {
            DismissAuditOverlays();
            await DelayAndFlushAsync(250);
            ShowSidePanel(SidePanel.Queue);
            await DelayAndFlushAsync(500);
            reports.Add(await CaptureShotAsync(options, "18-player-queue-panel", ThemeModes.Dark, expectVideo: true));
            CloseSidePanel();
            await DelayAndFlushAsync(250);
        }
    }

    private void DismissAuditOverlays()
    {
        OpenMenu.Hide();
        MoreMenu.Hide();
        SpeedMenu.Hide();
        TracksFlyout.Hide();
        VideoFlyout.Hide();
        ContextMenu.Hide();
        ClipDrawer?.Close();
        CloseRail();
        CloseSidePanel();
    }

    private static void ShowSettingsPage(SettingsView settingsView, Type pageType)
    {
        SettingsSection? match = SettingsSection.All.FirstOrDefault(s => s.PageType == pageType);
        if (match is not null)
        {
            settingsView.Show(match);
        }
    }

    private async Task ConfigureWindowAndThemeAsync(MainWindow window, int logicalWidth, int logicalHeight, string themeMode)
    {
        App.Settings.Set(SettingKeys.Theme, themeMode);
        window.ResizeClientForAudit(logicalWidth, logicalHeight);
        await DelayAndFlushAsync(400);
    }

    private async Task<AuditShotReport> CaptureShotAsync(
        VisualAuditOptions options,
        string shotId,
        string themeMode,
        bool expectVideo,
        bool measureSettingsOverflow = false)
    {
        string fileName = shotId + ".png";
        string outputPath = Path.Combine(options.OutputDirectory, fileName);
        double scale = XamlRoot?.RasterizationScale ?? 1.0;

        try
        {
            UpdateLayout();
            PostBackdropRegion();
            await WaitFramesAsync(3);

            int width = Math.Max(1, (int)Math.Round(ActualWidth * scale));
            int height = Math.Max(1, (int)Math.Round(ActualHeight * scale));

            // Clip to exact page bounds while snapshotting so off-screen translated elements (e.g. hidden
            // RailHandle at X = -6) do not widen RenderTargetBitmap or shift XAML coordinates relative to video.
            RectangleGeometry? previousClip = Clip;
            var shellBitmap = new RenderTargetBitmap();
            try
            {
                Clip = new RectangleGeometry { Rect = new Rect(0, 0, ActualWidth, ActualHeight) };
                await WaitFramesAsync(1);
                await shellBitmap.RenderAsync(this);
            }
            finally
            {
                Clip = previousClip;
            }
            if (shellBitmap.PixelWidth <= 0 || shellBitmap.PixelHeight <= 0)
            {
                return new AuditShotReport(
                    shotId, fileName, themeMode, ActualWidth, ActualHeight, scale, 0, 0,
                    false, 0, false, false, 0, new VisualImageStats(0, 0, 0, 0, 0, true), null,
                    "RenderTargetBitmap returned zero dimensions.");
            }

            byte[] shellPixels = await ReadBitmapPixelsAsync(shellBitmap);
            (byte bgR, byte bgG, byte bgB) = BackdropFallbackColor(themeMode, expectVideo);
            byte[] canvas = BgraCompositor.CreateCanvas(width, height, bgR, bgG, bgB);

            bool videoCaptured = false;
            if (expectVideo && VideoSurface.Opacity > 0 && VideoSurface.ActualWidth > 0 && VideoSurface.ActualHeight > 0)
            {
                using var cts = new CancellationTokenSource(2500);
                CapturedVideoFrame? videoFrame = await VideoSurface.CaptureFrameAsync(cts.Token);
                if (videoFrame is not null)
                {
                    Rect videoRect = VideoSurface.TransformToVisual(this)
                        .TransformBounds(new Rect(0, 0, VideoSurface.ActualWidth, VideoSurface.ActualHeight));
                    BgraCompositor.BlitOpaque(
                        canvas,
                        width,
                        height,
                        videoFrame.BgraPixels,
                        videoFrame.Width,
                        videoFrame.Height,
                        (int)Math.Round(videoRect.X * scale),
                        (int)Math.Round(videoRect.Y * scale),
                        (int)Math.Round(videoRect.Width * scale),
                        (int)Math.Round(videoRect.Height * scale));
                    videoCaptured = true;
                }
            }

            BgraCompositor.BlendPremultipliedOver(canvas, width, height, shellPixels, shellBitmap.PixelWidth, shellBitmap.PixelHeight, 0, 0);

            int openPopupCount = 0;
            bool overlapsSeekBar = false;
            AuditRect? seekRect = TryGetElementRect(SeekBar);
            var popupDebug = new StringBuilder();

            if (XamlRoot is not null)
            {
                Point pageOrigin = TransformToVisual(null).TransformPoint(new Point(0, 0));
                IReadOnlyList<Popup> popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot);
                // GetOpenPopupsForXamlRoot returns top-most first; composite bottom-to-top.
                for (int i = popups.Count - 1; i >= 0; i--)
                {
                    Popup popup = popups[i];
                    if (popup.Child is not FrameworkElement child
                        || child.Visibility != Visibility.Visible
                        || child.ActualWidth <= 0
                        || child.ActualHeight <= 0)
                    {
                        popupDebug.Append($"skip[{i}:{popup.Child?.GetType().Name ?? "null"}];");
                        continue;
                    }

                    openPopupCount++;
                    Point origin = child.TransformToVisual(null).TransformPoint(new Point(0, 0));
                    double relX = origin.X - pageOrigin.X;
                    double relY = origin.Y - pageOrigin.Y;
                    var popupRect = new AuditRect(relX, relY, child.ActualWidth, child.ActualHeight);
                    if (seekRect is { } sr && popupRect.Intersects(sr))
                    {
                        overlapsSeekBar = true;
                    }

                    (byte[] popupPixels, int bmpW, int bmpH, double drawX, double drawY, bool needsCardBackdrop, string sourceKind) =
                        await CapturePopupVisualAsync(child, relX, relY);

                    if (popupPixels.Length > 0 && bmpW > 0 && bmpH > 0)
                    {
                        if (needsCardBackdrop)
                        {
                            int cardW = Math.Max(1, (int)Math.Round(child.ActualWidth * scale));
                            int cardH = Math.Max(1, (int)Math.Round(child.ActualHeight * scale));
                            byte[] cardFill = BgraCompositor.CreateCanvas(cardW, cardH, 14, 16, 20, 158);
                            BgraCompositor.BlendPremultipliedOver(
                                canvas, width, height, cardFill, cardW, cardH,
                                (int)Math.Round(relX * scale), (int)Math.Round(relY * scale));
                        }
                        BgraCompositor.BlendPremultipliedOver(
                            canvas,
                            width,
                            height,
                            popupPixels,
                            bmpW,
                            bmpH,
                            (int)Math.Round(drawX * scale),
                            (int)Math.Round(drawY * scale));
                    }
                    popupDebug.Append($"[{i}:{child.GetType().Name}->{sourceKind} xy=({drawX:F0},{drawY:F0}) wh=({child.ActualWidth:F0}x{child.ActualHeight:F0}) bmp=({bmpW}x{bmpH})]");
                }
            }

            bool clipChipVisible = ClipLayer is { Visibility: Visibility.Visible }
                && ClipLayer.IsHitTestVisible
                && _clipLayerShown;
            double settingsOverflow = measureSettingsOverflow ? MeasureSettingsRightOverflowDip() : 0;

            VisualImageStats stats = BgraCompositor.AnalyzePixels(canvas, width, height);
            await SaveBgraPngAsync(outputPath, canvas, width, height, scale);

            return new AuditShotReport(
                shotId,
                fileName,
                themeMode,
                Math.Round(ActualWidth, 1),
                Math.Round(ActualHeight, 1),
                Math.Round(scale, 3),
                width,
                height,
                videoCaptured,
                openPopupCount,
                overlapsSeekBar,
                clipChipVisible,
                Math.Round(settingsOverflow, 2),
                stats,
                popupDebug.Length > 0 ? popupDebug.ToString() : null,
                null);
        }
        catch (Exception ex)
        {
            return new AuditShotReport(
                shotId,
                fileName,
                themeMode,
                Math.Round(ActualWidth, 1),
                Math.Round(ActualHeight, 1),
                Math.Round(scale, 3),
                0,
                0,
                false,
                0,
                false,
                false,
                0,
                new VisualImageStats(0, 0, 0, 0, 0, true),
                null,
                ex.Message);
        }
    }

    private AuditRect? TryGetElementRect(FrameworkElement? element)
    {
        if (element is null || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return null;
        }
        try
        {
            Rect r = element.TransformToVisual(this).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return new AuditRect(r.X, r.Y, r.Width, r.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private double MeasureSettingsRightOverflowDip()
    {
        if (SettingsHost.Child is not FrameworkElement root || ActualWidth <= 0)
        {
            return 0;
        }
        double pageRight = ActualWidth;
        double maxOverflow = 0;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            DependencyObject current = queue.Dequeue();
            if (current is SettingsRow or TextBlock or Button or Border { Name: "ContentColumn" })
            {
                if (current is FrameworkElement fe && fe.Visibility == Visibility.Visible && fe.ActualWidth > 0 && fe.ActualHeight > 0)
                {
                    try
                    {
                        Rect bounds = fe.TransformToVisual(this).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                        double overflow = bounds.Right - pageRight;
                        if (overflow > maxOverflow)
                        {
                            maxOverflow = overflow;
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            int childCount = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < childCount; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }
        return maxOverflow;
    }

    private async Task<(byte[] Pixels, int Width, int Height, double DrawX, double DrawY, bool NeedsCardBackdrop, string SourceKind)>
        CapturePopupVisualAsync(FrameworkElement child, double childRelX, double childRelY)
    {
        // 1. Direct render (works for MenuFlyoutPresenter and standard constrained popups).
        if (await TryRenderElementAsync(child) is { } direct)
        {
            return (direct.Pixels, direct.Width, direct.Height, childRelX, childRelY, false, "child");
        }

        Point pageOrigin = TransformToVisual(null).TransformPoint(new Point(0, 0));

        // 2. WinUI 3 FlyoutPresenter itself often yields 0x0 in RenderTargetBitmap; its template root
        //    (ScrollViewer / Border) includes the frosted background, border stroke, and content.
        if (VisualTreeHelper.GetChildrenCount(child) > 0
            && VisualTreeHelper.GetChild(child, 0) is FrameworkElement templateRoot
            && await TryRenderElementAsync(templateRoot) is { } rootShot)
        {
            Point origin = templateRoot.TransformToVisual(null).TransformPoint(new Point(0, 0));
            return (rootShot.Pixels, rootShot.Width, rootShot.Height, origin.X - pageOrigin.X, origin.Y - pageOrigin.Y, false, templateRoot.GetType().Name);
        }

        // 3. Final fallback: render the Flyout's inner UserControl content (e.g. TracksPanel / VideoPanel).
        if (child is ContentControl { Content: FrameworkElement content }
            && await TryRenderElementAsync(content) is { } contentShot)
        {
            Point origin = content.TransformToVisual(null).TransformPoint(new Point(0, 0));
            return (contentShot.Pixels, contentShot.Width, contentShot.Height, origin.X - pageOrigin.X, origin.Y - pageOrigin.Y, true, content.GetType().Name);
        }

        return ([], 0, 0, childRelX, childRelY, false, "empty");
    }

    private static async Task<(byte[] Pixels, int Width, int Height)?> TryRenderElementAsync(UIElement element)
    {
        try
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(element);
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            {
                return null;
            }
            byte[] pixels = await ReadBitmapPixelsAsync(bitmap);
            return MaxAlpha(pixels) > 0 ? (pixels, bitmap.PixelWidth, bitmap.PixelHeight) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte MaxAlpha(ReadOnlySpan<byte> bgra)
    {
        byte max = 0;
        for (int i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] > max)
            {
                max = bgra[i];
                if (max == 255)
                {
                    break;
                }
            }
        }
        return max;
    }

    private static (byte R, byte G, byte B) BackdropFallbackColor(string themeMode, bool expectVideo)
    {
        if (expectVideo)
        {
            return (12, 14, 18);
        }
        return themeMode switch
        {
            ThemeModes.Oled => (0, 0, 0),
            ThemeModes.Light => (243, 243, 243),
            _ => (28, 30, 34),
        };
    }

    private static async Task<byte[]> ReadBitmapPixelsAsync(RenderTargetBitmap bitmap)
    {
        IBuffer buffer = await bitmap.GetPixelsAsync();
        byte[] bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task SaveBgraPngAsync(string filePath, byte[] bgra, int width, int height, double scale)
    {
        using var stream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            (uint)width,
            (uint)height,
            96.0 * scale,
            96.0 * scale,
            bgra);
        await encoder.FlushAsync();

        stream.Seek(0);
        uint size = checked((uint)stream.Size);
        byte[] png = new byte[size];
        using (var reader = new DataReader(stream))
        {
            await reader.LoadAsync(size);
            reader.ReadBytes(png);
        }
        await File.WriteAllBytesAsync(filePath, png);
    }

    private async Task DelayAndFlushAsync(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await WaitFramesAsync(2);
    }

    private Task WaitFramesAsync(int frameCount)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int remaining = Math.Max(1, frameCount);
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            if (--remaining <= 0)
            {
                CompositionTarget.Rendering -= handler;
                tcs.TrySetResult();
            }
        };
        CompositionTarget.Rendering += handler;
        return tcs.Task;
    }

    private async Task<bool> WaitForPopupCountAsync(int minCount, int timeoutMs) =>
        await WaitUntilAsync(() => XamlRoot is not null && VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Count >= minCount, timeoutMs);

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (predicate())
            {
                return true;
            }
            await Task.Delay(40);
        }
        return predicate();
    }

    private static void WriteAuditManifest(VisualAuditOptions options, IReadOnlyList<AuditShotReport> reports, string? fatalError)
    {
        string manifestPath = Path.Combine(options.OutputDirectory, "manifest.json");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("generatedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            writer.WriteString("outputDirectory", options.OutputDirectory);
            if (options.VideoPath is not null)
            {
                writer.WriteString("videoPath", options.VideoPath);
            }
            else
            {
                writer.WriteNull("videoPath");
            }
            if (fatalError is not null)
            {
                writer.WriteString("fatalError", fatalError);
            }
            else
            {
                writer.WriteNull("fatalError");
            }
            writer.WriteNumber("shotCount", reports.Count);
            writer.WriteNumber("failedCount", reports.Count(r => r.Error is not null));
            writer.WriteNumber("blankCount", reports.Count(r => r.Stats.IsLikelyBlank));
            writer.WriteStartArray("shots");
            foreach (AuditShotReport shot in reports)
            {
                writer.WriteStartObject();
                writer.WriteString("id", shot.Id);
                writer.WriteString("file", shot.FileName);
                writer.WriteString("theme", shot.Theme);
                writer.WriteNumber("logicalWidth", shot.LogicalWidth);
                writer.WriteNumber("logicalHeight", shot.LogicalHeight);
                writer.WriteNumber("rasterizationScale", shot.RasterizationScale);
                writer.WriteNumber("pixelWidth", shot.PixelWidth);
                writer.WriteNumber("pixelHeight", shot.PixelHeight);
                writer.WriteBoolean("videoFrameCaptured", shot.VideoFrameCaptured);
                writer.WriteNumber("openPopupCount", shot.OpenPopupCount);
                writer.WriteBoolean("anyPopupOverlapsSeekBar", shot.AnyPopupOverlapsSeekBar);
                writer.WriteBoolean("clipChipVisible", shot.ClipChipVisible);
                writer.WriteNumber("settingsMaxRightOverflowDip", shot.SettingsMaxRightOverflowDip);
                writer.WriteNumber("meanLuma", shot.Stats.MeanLuma);
                writer.WriteNumber("nonBlackRatio", shot.Stats.NonBlackRatio);
                writer.WriteNumber("distinctSampledColors", shot.Stats.DistinctSampledColors);
                writer.WriteBoolean("isLikelyBlank", shot.Stats.IsLikelyBlank);
                if (shot.PopupDebug is not null)
                {
                    writer.WriteString("popupDebug", shot.PopupDebug);
                }
                if (shot.Error is not null)
                {
                    writer.WriteString("error", shot.Error);
                }
                else
                {
                    writer.WriteNull("error");
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        File.WriteAllText(manifestPath, Encoding.UTF8.GetString(stream.ToArray()), new UTF8Encoding(false));
    }
}
