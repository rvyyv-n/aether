using Aether.Core.Interop.Windowing;
using Aether.Core.Playback;

namespace Aether_App.Playback;

/// <summary>
/// Follows the refresh rate of the monitor the window is on, for mpv's <c>display-fps-override</c>. The window calls
/// <see cref="OnWindowMoved"/> when it moves or resizes; a display change (new mode, monitor plugged or unplugged)
/// arrives through the window procedure. UI thread only, including <see cref="Changed"/>.
/// </summary>
internal sealed class DisplayRefreshSync : IDisposable
{
    private readonly nint _hwnd;
    private readonly DisplayRefreshTracker _tracker = new();
    private readonly DisplayChangeWatcher? _watcher;

    public DisplayRefreshSync(nint hwnd)
    {
        _hwnd = hwnd;
        try
        {
            _watcher = new DisplayChangeWatcher(hwnd, () => Refresh(displayChanged: true));
        }
        catch (InvalidOperationException)
        {
            _watcher = null; // moving between monitors is still followed, a mode change on the same one is not
        }
        Refresh(displayChanged: false);
    }

    /// <summary>The <c>display-fps-override</c> value for the window's monitor: whole hertz, or "0" when unknown.</summary>
    public string Current => _tracker.Current;

    /// <summary>Raised when <see cref="Current"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The window moved or resized; reads the display again only if it is on another monitor now.</summary>
    public void OnWindowMoved() => Refresh(displayChanged: false);

    private void Refresh(bool displayChanged)
    {
        nint monitor = DisplayRefresh.MonitorFor(_hwnd);
        if (!_tracker.NeedsReading(monitor, displayChanged))
        {
            return;
        }
        if (_tracker.Accept(monitor, DisplayRefresh.QueryHertz(monitor)) is not null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => _watcher?.Dispose();
}
