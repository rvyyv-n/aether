namespace Aether.Core.Playback;

/// <summary>
/// Gets <see cref="PlayerEventDispatcher.Flush"/> called on the UI thread, later. Implementations
/// must not block and must tolerate a call from any thread (the mpv event thread, mostly).
/// </summary>
public interface IUiFlushScheduler
{
    /// <summary>
    /// Any thread. Arranges exactly one later call to the dispatcher's <c>Flush</c> on the UI thread.
    /// The dispatcher only calls this again after that flush has started, so calls do not pile up.
    /// </summary>
    void RequestFlush();
}
