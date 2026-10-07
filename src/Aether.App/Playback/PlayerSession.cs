using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether_App.Controls;
using Aether_App.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

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
    private readonly MpvContext _context;
    private readonly MpvEventLoop _events;
    private readonly PlayerEventDispatcher _dispatcher;
    private readonly FrameStepper _stepper;
    private readonly FrameStepKeyInput _keys;
    private readonly MpvVideoSurface _surface;
    private bool _disposed;

    private static readonly MpvCommand TogglePauseCommand = new("cycle", "pause");

    private PlayerSession(MpvContext context, MpvVideoSurface surface, UIElement keyTarget, DispatcherQueue queue)
    {
        _surface = surface;
        _context = context;
        try
        {
            var scheduler = new DispatcherQueueFlushScheduler(queue);
            _dispatcher = new PlayerEventDispatcher(ViewModel, scheduler);
            _stepper = new FrameStepper(new MpvFrameStepCommandSender(_context));
            _dispatcher.CommandReplies = _stepper;
            scheduler.FlushTarget = Flush;

            _dispatcher.ObserveAll(_context);
            _events = new MpvEventLoop(_context, _dispatcher);
            _events.Start();

            _keys = new FrameStepKeyInput(keyTarget, _stepper, queue);
            surface.AttachPlayer(_context);
        }
        catch
        {
            _keys?.Dispose();
            _events?.Dispose();
            _context.Dispose();
            throw;
        }
    }

    public PlayerViewModel ViewModel { get; } = new();

    public MpvContext Context => _context;

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

    /// <summary>Opens <paramref name="path"/>, replacing the current file. Non-blocking.</summary>
    /// <exception cref="MpvException">The core is shutting down.</exception>
    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _context.CommandAsync(new MpvCommand("loadfile", path, "replace"));
    }

    /// <summary>Toggles pause. Non-blocking.</summary>
    /// <exception cref="MpvException">The core is shutting down.</exception>
    public void TogglePause() => _context.CommandAsync(TogglePauseCommand);

    /// <summary>Sets the volume (100 is unity gain). Non-blocking.</summary>
    /// <exception cref="MpvException">The core is shutting down.</exception>
    public void SetVolume(double volume) =>
        _context.CommandAsync(new MpvCommand("set", "volume", Math.Clamp(volume, 0, 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Releases a held step key, e.g. when the window is deactivated.</summary>
    public void ReleaseKeys() => _keys.ReleaseAll();

    /// <summary>UI thread, once per display refresh while events are pending.</summary>
    private void Flush()
    {
        _dispatcher.Flush();
        PlayerState state = ViewModel.State;
        // The first frame sits at time 0 (or within half a frame of it when the container starts later).
        double halfFrame = state.FramesPerSecond > 0 ? 0.5 / state.FramesPerSecond : 0;
        _stepper.SetBounds(atStart: state.Position <= halfFrame, atEnd: state.IsAtEnd);
    }

    /// <summary>Non-blocking: the event and render threads release the core as they exit.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _keys.Dispose();
        _events.Dispose();
        _surface.DetachPlayer();
        _context.Dispose();
    }
}
