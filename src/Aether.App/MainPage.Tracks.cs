using System.Globalization;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether.Core.Playback.Audio;
using Aether.Core.Playback.Tracks;
using Aether_App.Controls;
using Microsoft.UI.Dispatching;
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

    // What the user picked for the open file, kept for an engine restart (null: mpv's own picks). Set from what mpv
    // reports after each change, so a subtitle file added with "select" is recorded under the id it was given.
    private TrackChoices? _trackChoices;
    private TrackChoices? _restoreTracks; // waiting for the restarted engine to finish loading the file
    // The audio and subtitle the user last picked, by language, for the next files of the queue (13.4).
    private TrackPreference? _trackPreference;
    private TrackPreference? _followTracks; // waiting for the next file of the queue to finish loading
    // The mix waiting to be sent: a fader sends many changes a second, and each one restarts mpv's audio.
    private IReadOnlyList<MixLevel>? _pendingMix;
    private bool _mixPending;
    private int? _trackBeforeMix;
    private DispatcherQueueTimer? _mixTimer;

    private void InitializeTracks()
    {
        // Light dismiss would otherwise swallow the first click on the pill, as for the volume flyout.
        TracksFlyout.OverlayInputPassThroughElement = Osd;
        TracksPanel.TrackChosen += OnTrackChosen;
        TracksPanel.AddSubtitleRequested += OnAddSubtitleRequested;
        TracksPanel.DelayStepped += OnDelayStepped;
        TracksPanel.MixChanged += OnMixChanged;
        _mixTimer = DispatcherQueue.CreateTimer();
        _mixTimer.Interval = TimeSpan.FromMilliseconds(120);
        _mixTimer.IsRepeating = false;
        _mixTimer.Tick += (_, _) => SendMix();
    }

    private void OnMixChanged(object? sender, IReadOnlyList<MixLevel>? levels)
    {
        if (levels is not null && _pendingMix is null)
        {
            _trackBeforeMix = _tracks.Tracks.Audio.FirstOrDefault(t => t.Selected)?.Id;
        }
        _pendingMix = levels;
        _mixPending = true;
        if (levels is null)
        {
            SendMix(); // ending the mix is one change; no reason to wait
        }
        else if (_mixTimer is { IsRunning: false } timer)
        {
            timer.Start();
        }
    }

    private void SendMix()
    {
        _mixTimer?.Stop();
        if (!_mixPending || _session is not { } session)
        {
            return;
        }
        _mixPending = false;
        IReadOnlyList<MixLevel>? levels = _pendingMix;
        TrySend(() => session.SetMix(levels is null ? null : TrackMix.Graph(levels), _trackBeforeMix));
        if (levels is null)
        {
            _trackBeforeMix = null;
        }
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
        TracksPanel.RequestedTheme = ThemeTokens.AppTheme; // rows built here must match the theme the flyout opens in
        TracksPanel.LinkTitle = _streamTitle;
        TracksPanel.Show(snapshot);
    }

    private void OnTracksFlyoutOpened(object? sender, object e)
    {
        HoldForMenu(true);
        SetPopupSurface(TracksPanel);
    }

    private void OnTracksFlyoutClosed(object? sender, object e)
    {
        HoldForMenu(false);
        SetPopupSurface(null);
        Focus(FocusState.Programmatic); // keep Space and the frame-step keys on the page
    }

    private void OnTrackChosen(object? sender, TrackChoice choice)
    {
        string property = choice.Kind == TrackKind.Audio ? "aid" : choice.Secondary ? "secondary-sid" : "sid";
        if (choice.Kind == TrackKind.Audio)
        {
            _session?.PrepareForAudioRestart();
            if (_pendingMix is not null && _session is { } session)
            {
                _mixTimer?.Stop();
                _pendingMix = null;
                _mixPending = false;
                _trackBeforeMix = null;
                TrySend(() => session.SetMix(null, choice.Id));
            }
        }
        string value = choice.Id is { } id ? id.ToString(CultureInfo.InvariantCulture) : "no";
        _ = ChangeTracks(context => context.SetProperty(property, value), userChoice: true);
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
    private async Task ChangeTracks(Action<MpvContext> change, bool userChoice = false)
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
        if (_isLoaded && _session == session)
        {
            _trackChoices = TrackChoices.From(snapshot);
            if (userChoice)
            {
                _trackPreference = TrackPreference.From(snapshot.Tracks);
            }
            if (version == _tracksVersion && TracksFlyout.IsOpen)
            {
                ApplyTracks(snapshot);
            }
        }
    }

    /// <summary>
    /// After an engine restart, once the file has loaded: loads the subtitle files again, then puts back the audio and
    /// subtitle track and both delays. Runs through the same queue as the flyout's changes.
    /// </summary>
    private void RestoreTracksOnceLoaded()
    {
        if (_restoreTracks is not { } choices || !HasVideo || _session is null)
        {
            return;
        }
        _restoreTracks = null;
        _ = ChangeTracks(context =>
        {
            TrackSnapshot current = TrackSnapshot.Read(context);
            IReadOnlyList<string> missing = choices.MissingFiles(current.Tracks);
            foreach (string file in missing)
            {
                Send(context, "sub-add", file, "auto"); // loaded without taking over; the choice below decides
            }
            if (missing.Count > 0)
            {
                current = TrackSnapshot.Read(context);
            }
            foreach (string[] command in choices.Select(current))
            {
                Send(context, command);
            }
        });
    }

    /// <summary>Once the next file of the queue has loaded, picks the tracks matching what the user chose before it.</summary>
    private void FollowTracksOnceLoaded()
    {
        if (_followTracks is not { } preference || !HasVideo || _session is null)
        {
            return;
        }
        _followTracks = null;
        _ = ChangeTracks(context =>
        {
            foreach (string[] command in preference.Select(TrackSnapshot.Read(context).Tracks))
            {
                Send(context, command);
            }
        });
    }

    /// <summary>One command from a restore; a file that has gone or a track mpv refuses must not stop the rest.</summary>
    private static void Send(MpvContext context, params string[] command)
    {
        try
        {
            context.Command(command);
        }
        catch (MpvException)
        {
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
