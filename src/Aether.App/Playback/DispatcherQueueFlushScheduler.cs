using Aether.Core.Playback;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace Aether_App.Playback;

/// <summary>
/// Runs the dispatcher's flush once per display refresh, on the UI thread, without ever blocking.
/// <see cref="RequestFlush"/> (any thread) enqueues a cached handler that subscribes to
/// <see cref="CompositionTarget.Rendering"/> once; the next frame's handler unsubscribes, clears the
/// pending flag and calls <see cref="FlushTarget"/>. Nothing is subscribed while idle, and the
/// handlers are created once, so a request allocates nothing. Rendering pauses while the window is
/// not visible, so flushes wait until it is shown again.
/// </summary>
public sealed class DispatcherQueueFlushScheduler : IUiFlushScheduler
{
    private readonly DispatcherQueue _queue;
    private readonly DispatcherQueueHandler _subscribe;
    private readonly EventHandler<object> _onRendering;
    private int _pending;
    private volatile Action? _flushTarget;

    /// <param name="queue">The UI thread's queue.</param>
    /// <param name="flushTarget">Usually <c>PlayerEventDispatcher.Flush</c>; may be set later (the dispatcher needs this scheduler first).</param>
    public DispatcherQueueFlushScheduler(DispatcherQueue queue, Action? flushTarget = null)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = queue;
        _flushTarget = flushTarget;
        _subscribe = Subscribe;
        _onRendering = OnRendering;
    }

    /// <summary>What to run on the UI thread per flush.</summary>
    public Action? FlushTarget
    {
        get => _flushTarget;
        set => _flushTarget = value;
    }

    /// <inheritdoc />
    public void RequestFlush()
    {
        if (Interlocked.Exchange(ref _pending, 1) != 0)
        {
            return; // a subscription or frame callback is already on its way
        }

        if (!_queue.TryEnqueue(_subscribe))
        {
            // The queue is shutting down; there is no UI left to flush to.
            Volatile.Write(ref _pending, 0);
        }
    }

    private void Subscribe() => CompositionTarget.Rendering += _onRendering;

    private void OnRendering(object? sender, object e)
    {
        CompositionTarget.Rendering -= _onRendering;
        // Clear before flushing: a request raised while flushing schedules the next frame.
        Volatile.Write(ref _pending, 0);
        _flushTarget?.Invoke();
    }
}
