using System.Globalization;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether.Core.Playback.Tracks;
using Aether_App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage.Pickers;

namespace Aether_App;

/// <summary>
/// The tracks flyout (screen 9.2) behind Audio and Subtitles in the right-click menu. The track list is read from mpv
/// when the flyout opens and after each change, on a pool thread, never through the event dispatcher: <c>track-list</c>
/// builds a tree on every report. UI thread only, apart from the pool work it starts.
/// </summary>
public sealed partial class MainPage
{
    private static readonly string[] SubtitleExtensions = [".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup", ".smi"];

    // Changes reach mpv one at a time and in the order they were made: two quick presses on a stepper must not swap.
    private readonly SemaphoreSlim _trackWork = new(1, 1);
    private TrackSnapshot _tracks = TrackSnapshot.Empty;
    private int _tracksVersion; // bumped per read, so a slow one never overwrites a newer one
    private bool _showTracksAfterMenu;

    private void InitializeTracks()
    {
        // Light dismiss would otherwise swallow the first click on the pill, as for the volume flyout.
        TracksFlyout.OverlayInputPassThroughElement = Osd;
        TracksPanel.TrackChosen += OnTrackChosen;
        TracksPanel.AddSubtitleRequested += OnAddSubtitleRequested;
        TracksPanel.DelayStepped += OnDelayStepped;
    }

    /// <summary>The menu closes after this click, and its Closed handler opens the flyout, so the two holds do not cross.</summary>
    private void OnTracksMenuClick(object sender, RoutedEventArgs e) => _showTracksAfterMenu = true;

    private void ShowTracksIfRequested()
    {
        if (_showTracksAfterMenu)
        {
            _showTracksAfterMenu = false;
            ShowTracks();
        }
    }

    private async void ShowTracks()
    {
        if (_session is not { } session || XamlRoot is null)
        {
            return;
        }
        int version = ++_tracksVersion;
        TrackSnapshot snapshot = await Task.Run(() => TrackSnapshot.Read(session.Context));
        if (version != _tracksVersion || !_isLoaded || _session != session)
        {
            return;
        }
        ApplyTracks(snapshot);
        // The window less 8 either side (9.5), and the 1 px stroke the presenter adds.
        TracksPanel.MaxPanelHeight = Math.Max(0, XamlRoot.Size.Height - (double)Application.Current.Resources["AetherSizeMenuMaxHeightInset"] - 2);
        TracksFlyout.ShowAt(Osd, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Top, ShowMode = FlyoutShowMode.Standard });
    }

    private void ApplyTracks(TrackSnapshot snapshot)
    {
        _tracks = snapshot;
        TracksPanel.Show(snapshot);
    }

    private void OnTracksFlyoutOpened(object? sender, object e) => HoldForMenu(true);

    private void OnTracksFlyoutClosed(object? sender, object e)
    {
        HoldForMenu(false);
        Focus(FocusState.Programmatic); // keep Space and the frame-step keys on the page
    }

    private void OnTrackChosen(object? sender, TrackChoice choice)
    {
        string property = choice.Kind == TrackKind.Audio ? "aid" : "sid";
        string value = choice.Id is { } id ? id.ToString(CultureInfo.InvariantCulture) : "no";
        _ = ChangeTracks(context => context.SetProperty(property, value));
    }

    private void OnDelayStepped(object? sender, DelayStep step)
    {
        bool audio = step.Kind == TrackKind.Audio;
        double next = TrackDelay.Stepped(audio ? _tracks.AudioDelay : _tracks.SubtitleDelay, step.Direction);
        // Shown at once, so a second press builds on the first instead of on what mpv last reported.
        ApplyTracks(audio ? _tracks with { AudioDelay = next } : _tracks with { SubtitleDelay = next });
        _ = ChangeTracks(context => context.SetProperty(audio ? "audio-delay" : "sub-delay", TrackDelay.ForMpv(next)));
    }

    /// <summary>Applies the change on a pool thread, then redraws from what mpv now says.</summary>
    private async Task ChangeTracks(Action<MpvContext> change)
    {
        if (_session is not { } session)
        {
            return;
        }
        await _trackWork.WaitAsync();
        TrackSnapshot snapshot;
        int version;
        try
        {
            version = ++_tracksVersion;
            snapshot = await Task.Run(() =>
            {
                try
                {
                    change(session.Context);
                }
                catch (Exception exception) when (exception is MpvException or ObjectDisposedException)
                {
                    // The core refused, or is shutting down; the redraw below shows what is true.
                }
                return TrackSnapshot.Read(session.Context);
            });
        }
        finally
        {
            _trackWork.Release();
        }
        if (version == _tracksVersion && _isLoaded && _session == session && TracksFlyout.IsOpen)
        {
            ApplyTracks(snapshot);
        }
    }

    private async void OnAddSubtitleRequested(object? sender, EventArgs e)
    {
        if (_session is null || _host is not Window window)
        {
            return;
        }
        TracksFlyout.Hide(); // the picker takes focus, which would close it anyway

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        foreach (string extension in SubtitleExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

        _chrome.SetHold(ChromeHolds.Interaction, true);
        ApplyChrome();
        Windows.Storage.StorageFile? file;
        try
        {
            file = await picker.PickSingleFileAsync();
        }
        finally
        {
            _chrome.SetHold(ChromeHolds.Interaction, false);
            ApplyChrome();
        }
        if (file is null || _session is null)
        {
            return;
        }
        string path = file.Path;
        await ChangeTracks(context => context.Command("sub-add", path, "select"));
        ShowTracks(); // back to the flyout, now listing the new file
    }
}
