using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Streams;
using Aether_App.Controls;
using Aether_App.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.System.Power;

namespace Aether_App.Playback;

/// <summary>
/// Composes one mpv core with its event thread, UI dispatch, frame stepping and the video surface.
/// <para>
/// Threads: the core's events arrive on "Aether.MpvEvents" and reach <see cref="ViewModel"/> through
/// <see cref="PlayerEventDispatcher"/> at most once per display refresh; frames render on
/// "Aether.Render". Everything here runs on the UI thread and nothing waits on either thread.
/// </para>
/// </summary>
public sealed class PlayerSession : IDisposable
{
    private const string DisplayFpsOverride = "display-fps-override";

    private readonly MpvContext _context;
    private readonly MpvEventLoop _events;
    private readonly PlayerEventDispatcher _dispatcher;
    private readonly FrameStepper _stepper;
    private readonly SeekCoalescer _seekCoalescer;
    private readonly FrameStepKeyInput _keys;
    private readonly MpvVideoSurface _surface;
    private readonly LoopRestart _loop = new();
    private readonly FileEnd _fileEnd = new();
    private readonly MpvSettingsSync _settingsSync;
    private readonly DisplayRefreshSync? _display;
    private readonly DisplayWakeLock _wake = new();
    private double _subtitleClearance;
    private double _videoHeight;
    private string _subtitleMargin = "";
    private bool _disposed;

    private PlayerSession(MpvContext context, MpvVideoSurface surface, UIElement keyTarget, DispatcherQueue queue)
    {
        _surface = surface;
        _context = context;
        try
        {
            var scheduler = new DispatcherQueueFlushScheduler(queue);
            _dispatcher = new PlayerEventDispatcher(ViewModel, scheduler);
            _stepper = new FrameStepper(new MpvFrameStepCommandSender(_context));
            _seekCoalescer = new SeekCoalescer(new MpvSeekCommandSender(_context));
            _dispatcher.CommandReplies = new CompositeCommandReplyHandler(_stepper, _seekCoalescer);
            scheduler.FlushTarget = Flush;

            _dispatcher.ObserveAll(_context);
            _events = new MpvEventLoop(_context, _dispatcher);
            _events.Start();

            _keys = new FrameStepKeyInput(keyTarget, _stepper, queue);
            surface.AttachPlayer(_context);

            _settingsSync = new MpvSettingsSync(args => _context.CommandAsync(new MpvCommand(args)));
            App.Settings.Changed += OnSettingChanged;
            PowerManager.PowerSourceKindChanged += OnPowerSourceChanged;
            ApplySettings();

            _display = App.DisplaySync;
            if (_display is not null)
            {
                _display.Changed += OnDisplayRefreshChanged;
                SendDisplayRefresh();
            }
        }
        catch
        {
            App.Settings.Changed -= OnSettingChanged;
            PowerManager.PowerSourceKindChanged -= OnPowerSourceChanged;
            if (_display is not null)
            {
                _display.Changed -= OnDisplayRefreshChanged;
            }
            _keys?.Dispose();
            _events?.Dispose();
            _context.Dispose();
            throw;
        }
    }

    public PlayerViewModel ViewModel { get; } = new();

    public MpvContext Context => _context;

    /// <summary>UI thread: the open file played to its end and is not looping, so nothing else will happen on its own.</summary>
    public event EventHandler? FileEnded;

    /// <summary>
    /// Worker thread: creates the engine with the saved start-only options (hardware decoding, passthrough, custom
    /// options). The live settings follow once the session starts.
    /// </summary>
    public static MpvContext CreateEngine(AetherSettings? settings = null)
    {
        if (settings is null)
        {
            App.SettingsLoaded.Wait();
        }
        var s = settings ?? App.Settings.Current;
        IReadOnlyList<KeyValuePair<string, string>> startOptions = MpvSettingsMap.StartOptions(s);
        try
        {
            return MpvContext.Create(new MpvContextOptions
            {
                HardwareDecoder = s.HardwareDecoding,
                VideoLatencyHacks = MpvSettingsMap.VideoLatencyHacks(s),
                BackBufferSize = MpvSettingsMap.BackBufferSize(s),
                AdditionalOptions = startOptions,
            });
        }
        catch (MpvException) when (startOptions.Count > 0)
        {
            // mpv rejected a start-only option (the validator cannot tell whether mpv knows a custom key), and the
            // player must still open, so retry with only Aether's own.
            return MpvContext.Create(new MpvContextOptions
            {
                HardwareDecoder = s.HardwareDecoding,
                VideoLatencyHacks = MpvSettingsMap.VideoLatencyHacks(s),
                BackBufferSize = MpvSettingsMap.BackBufferSize(s),
            });
        }
    }

    /// <summary>
    /// UI thread: starts a session around <paramref name="context"/>, which may have been created on
    /// another thread (see <see cref="EngineStartup"/>). The session owns the context from here on and
    /// disposes it, also when this throws.
    /// </summary>
    public static PlayerSession Create(MpvContext context, MpvVideoSurface surface, UIElement keyTarget, DispatcherQueue queue)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            ArgumentNullException.ThrowIfNull(surface);
            ArgumentNullException.ThrowIfNull(keyTarget);
            ArgumentNullException.ThrowIfNull(queue);
        }
        catch
        {
            context.Dispose();
            throw;
        }
        return new PlayerSession(context, surface, keyTarget, queue);
    }

    /// <summary>
    /// Opens <paramref name="path"/>, replacing the current file, from <paramref name="startAt"/> seconds
    /// when given, with <paramref name="fileOptions"/> (built by <see cref="MpvLoadOptions"/>) applying to this file only.
    /// Non-blocking.
    /// </summary>
    /// <exception cref="MpvException">The core is shutting down.</exception>
    public void Open(string path, double? startAt = null, string? fileOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        // <c>start</c> is global and sticks, so every open sets it, to "none" when there is nothing to resume.
        string start = startAt is { } seconds
            ? seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            : "none";
        _context.CommandAsync(new MpvCommand("set", "start", start));
        _context.CommandAsync(string.IsNullOrEmpty(fileOptions)
            ? new MpvCommand("loadfile", path, "replace")
            : new MpvCommand("loadfile", path, "replace", "-1", fileOptions)); // -1: the playlist index mpv 0.38+ expects before options
    }

    /// <summary>Toggles pause; on an ended file restarts it from 0. Non-blocking.</summary>
    /// <exception cref="MpvException">The core is shutting down.</exception>
    public void TogglePause()
    {
        foreach (MpvCommand command in PlayToggleCommands.For(ViewModel.State.IsAtEnd))
        {
            _context.CommandAsync(command);
        }
    }

    /// <summary>Pauses. Non-blocking.</summary>
    /// <exception cref="MpvException">The core is shutting down.</exception>
    public void Pause() => _context.CommandAsync(new MpvCommand("set", "pause", "yes"));

    public SeekCoalescer SeekCoalescer => _seekCoalescer;

    /// <summary>Seeks from the seek bar; see <see cref="TransportState.Seek"/>. Non-blocking, coalesced.</summary>
    /// <exception cref="MpvException">The core is shutting down.</exception>
    public void Seek(double seconds, bool dragging) => _seekCoalescer.Seek(seconds, dragging);

    /// <summary>Sets the volume (100 is unity gain). Non-blocking.</summary>
    /// <exception cref="MpvException">The core is shutting down.</exception>
    public void SetVolume(double volume) =>
        _context.CommandAsync(new MpvCommand("set", "volume", Math.Clamp(volume, 0, 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Skips from the pill or the arrow keys; a held key collapses into one seek at a time. Non-blocking.</summary>
    /// <exception cref="MpvException">The core is shutting down.</exception>
    public void SeekBy(double seconds) => _seekCoalescer.SeekBy(seconds);

    /// <summary>Speed from the speed menu, 0.1× to 4.0×. Non-blocking.</summary>
    /// <exception cref="MpvException">The core is shutting down.</exception>
    public void SetSpeed(double speed) => _context.CommandAsync(TransportState.SetSpeed(speed));

    /// <summary>One frame step from the pill's buttons (the , and . keys step on their own).</summary>
    public void StepFrame(FrameStepDirection direction) => _keys.Tap(direction);

    /// <summary>Releases a held step key, e.g. when the window is deactivated.</summary>
    public void ReleaseKeys() => _keys.ReleaseAll();

    /// <summary>
    /// UI thread: lifts the subtitles <paramref name="clearance"/> above the picture's bottom edge (the transport bar's
    /// top, as far as it covers the picture), both in dips, <paramref name="videoHeight"/> being the picture's height.
    /// </summary>
    public void SetSubtitleClearance(double clearance, double videoHeight)
    {
        _subtitleClearance = clearance;
        _videoHeight = videoHeight;
        SendSubtitleMargin();
    }

    /// <summary>Follows the Position setting; a <c>sub-margin-y</c> in the custom options wins.</summary>
    private void SendSubtitleMargin()
    {
        if (_disposed)
        {
            return;
        }
        AetherSettings settings = App.Settings.Current;
        string margin = SubtitleLift.MarginY(settings.SubtitlePosition, _subtitleClearance, _videoHeight);
        if (margin == _subtitleMargin)
        {
            return;
        }
        foreach (KeyValuePair<string, string> option in MpvSettingsMap.CustomOptions(settings.CustomMpvOptions))
        {
            if (option.Key.Equals("sub-margin-y", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        try
        {
            _context.CommandAsync(new MpvCommand("set", "sub-margin-y", margin));
            _subtitleMargin = margin;
        }
        catch (Exception exception) when (exception is MpvException or ObjectDisposedException)
        {
            // The core is shutting down.
        }
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        ApplySettings();
        SendSubtitleMargin();
        UpdateWake();
    }

    /// <summary>UI thread: the screen stays awake only while a file plays and the setting is on.</summary>
    private void UpdateWake() =>
        _wake.Set(!_disposed && App.Settings.Current.PreventSleep && ViewModel.State.Status == PlaybackStatus.Playing);

    private void OnPowerSourceChanged(object? sender, object e) => ApplySettings();

    /// <summary>
    /// Any thread: settings change on the UI thread, the power source on a pool thread. Only sends non-blocking
    /// commands, and what the core refuses is not worth failing for.
    /// </summary>
    private void ApplySettings()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            _settingsSync.Apply(App.Settings.Current, IsOnBattery());
        }
        catch (Exception exception) when (exception is MpvException or ObjectDisposedException)
        {
            // The core is shutting down.
        }
    }

    private void OnDisplayRefreshChanged(object? sender, EventArgs e) => SendDisplayRefresh();

    /// <summary>
    /// UI thread: tells mpv the refresh rate of the window's monitor, so <c>video-sync=display-resample</c> does not
    /// assume 60 Hz and drop frames of a 120 fps clip. Sent as a one-off, since <see cref="MpvSettingsSync"/> only
    /// carries settings; a <c>display-fps-override</c> in the custom options wins.
    /// </summary>
    private void SendDisplayRefresh()
    {
        if (_disposed || _display is null)
        {
            return;
        }
        foreach (KeyValuePair<string, string> option in MpvSettingsMap.CustomOptions(App.Settings.Current.CustomMpvOptions))
        {
            if (option.Key.Equals(DisplayFpsOverride, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        try
        {
            string targetFps = "0";
            if (App.Settings.Current.SmoothMotion && int.TryParse(_display.Current, out int hz) && hz is > 0 and <= 60)
            {
                targetFps = _display.Current;
            }
            _context.CommandAsync(new MpvCommand("set", DisplayFpsOverride, targetFps));
        }
        catch (Exception exception) when (exception is MpvException or ObjectDisposedException)
        {
            // The core is shutting down.
        }
    }

    private static bool IsOnBattery()
    {
        try
        {
            return PowerManager.PowerSourceKind == PowerSourceKind.DC;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <summary>UI thread, once per display refresh while events are pending.</summary>
    private void Flush()
    {
        _dispatcher.Flush();
        PlayerState state = ViewModel.State;
        // The first frame sits at time 0 (or within half a frame of it when the container starts later).
        double halfFrame = state.FramesPerSecond > 0 ? 0.5 / state.FramesPerSecond : 0;
        _stepper.SetBounds(atStart: state.Position <= halfFrame, atEnd: state.IsAtEnd);
        UpdateWake();
        if (_loop.ShouldRestart(state.IsAtEnd, ViewModel.IsLooping))
        {
            try
            {
                TogglePause();
            }
            catch (MpvException)
            {
                // The core is shutting down.
            }
        }
        // Tracked on every flush, also while looping, so turning the loop off at the end does not count as an end.
        if (_fileEnd.Reached(state.IsAtEnd) && !ViewModel.IsLooping)
        {
            FileEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Non-blocking: the event and render threads release the core as they exit.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        App.Settings.Changed -= OnSettingChanged;
        PowerManager.PowerSourceKindChanged -= OnPowerSourceChanged;
        if (_display is not null)
        {
            _display.Changed -= OnDisplayRefreshChanged;
        }
        _wake.Dispose();
        _seekCoalescer.Reset();
        _keys.Dispose();
        _events.Dispose();
        _surface.DetachPlayer();
        _context.Dispose();
    }
}
