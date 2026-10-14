using Windows.System.Display;

namespace Aether_App.Playback;

/// <summary>Keeps the display from dimming or sleeping while held. UI thread only; releasing twice is harmless.</summary>
internal sealed class DisplayWakeLock : IDisposable
{
    private DisplayRequest? _request;

    public void Set(bool held)
    {
        if (held == (_request is not null))
        {
            return;
        }
        try
        {
            if (held)
            {
                var request = new DisplayRequest();
                request.RequestActive();
                _request = request;
            }
            else
            {
                _request!.RequestRelease();
                _request = null;
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // The system refused; playback goes on and the screen may sleep as it would anyway.
            _request = null;
        }
    }

    public void Dispose() => Set(false);
}
