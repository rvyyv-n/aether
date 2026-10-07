using Aether.Core.Interop;
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

    private PlayerSession(MpvVideoSurface surface, UIElement keyTarget, DispatcherQueue queue)
    {
        _surface = surface;
        _context = MpvContext.Create();
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
            _events?.Dispose();
            _context.Dispose();
            throw;
        }
    }

    public PlayerViewModel ViewModel { get; } = new();

    public MpvContext Context => _context;

    /// <summary>Starts a session, or returns null when libmpv is not installed (the surface stays black).</summary>
    public static PlayerSession? TryCreate(MpvVideoSurface surface, UIElement keyTarget, DispatcherQueue queue)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(keyTarget);
        ArgumentNullException.ThrowIfNull(queue);
        return NativeLibraries.IsMpvAvailable() ? new PlayerSession(surface, keyTarget, queue) : null;
    }

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
