using Aether.Core.Playback;
using Aether_App.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Aether_App;

/// <summary>
/// Shows toasts on the page: feeds requests to the <see cref="ToastQueue"/>, draws what it says is showing in the
/// overlay <see cref="Toast"/>, and arms the timer it asks for. Any code that wants a toast calls <see cref="Show(ToastRequest)"/>.
/// UI thread only.
/// </summary>
internal sealed class ToastService
{
    private const double BottomMargin = 24; // the card's own margin too
    private const double Gap = 8; // above the card, and above the chip

    private readonly ToastQueue _queue;
    private readonly Toast _toast;
    private readonly FrameworkElement _layer;
    private readonly ChromeFader _fader;
    private readonly Action _returnFocus;
    private readonly DispatcherQueueTimer _timer;
    private readonly DispatcherQueueTimer _hideTimer;
    private bool _focused;
    private bool _shown;

    /// <param name="layer">The overlay holding <paramref name="toast"/>: bottom aligned, with 16 side margins.</param>
    /// <param name="returnFocus">Puts keyboard focus back on the page when a toast that had it goes away.</param>
    public ToastService(
        Toast toast, FrameworkElement layer, ChromeFader fader, DispatcherQueue dispatcher, Action returnFocus, TimeProvider? time = null)
    {
        _toast = toast;
        _layer = layer;
        _fader = fader;
        _returnFocus = returnFocus;
        _queue = new ToastQueue(time);
        _queue.Changed += OnQueueChanged;

        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) =>
        {
            _queue.Tick();
            Arm();
        };
        _hideTimer = dispatcher.CreateTimer();
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _layer.Visibility = Visibility.Collapsed;
        };

        toast.HoverChanged += (_, hovered) =>
        {
            _queue.SetHovered(hovered);
            Arm();
        };
        toast.FocusChanged += (_, focused) =>
        {
            _focused = focused;
            _queue.SetFocused(focused);
            Arm();
        };
        toast.ActionInvoked += (_, _) => Close();
        toast.DismissRequested += (_, _) => Close();

        _layer.Visibility = Visibility.Collapsed;
        _fader.Fade(_layer, false);
    }

    public void Show(string key, string text, ToastTone tone = ToastTone.Neutral, string? actionLabel = null, Action? action = null) =>
        Show(new ToastRequest(key, text, tone, actionLabel, action));

    public void Show(ToastRequest request)
    {
        _queue.Show(request);
        Arm();
    }

    /// <summary>Toasts wait, with their timer held, while a dialog or flyout is open.</summary>
    public void SetBlocked(bool blocked)
    {
        _queue.SetBlocked(blocked);
        Arm();
    }

    /// <summary>
    /// Sits 8 above the card while the controls show, 24 from the bottom once they hide. <paramref name="controlsHeight"/>
    /// is the card as it is drawn now (slim, full or growing); <paramref name="chipHeight"/> is the export chip or clip
    /// drawer between the two, which the toast clears by 8.
    /// </summary>
    public void SetControlsShown(bool shown, double controlsHeight, double chipHeight = 0)
    {
        Thickness margin = _layer.Margin;
        double bottom = shown ? BottomMargin + controlsHeight + Gap + (chipHeight > 0 ? chipHeight + Gap : 0) : BottomMargin;
        _layer.Margin = new Thickness(margin.Left, margin.Top, margin.Right, bottom);
    }

    private void Close()
    {
        bool hadFocus = _focused;
        _queue.Dismiss();
        Arm();
        if (hadFocus)
        {
            _returnFocus();
        }
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        if (_queue.Showing is { } request)
        {
            _hideTimer.Stop();
            _toast.Present(request);
            _layer.Visibility = Visibility.Visible;
            _fader.Fade(_layer, true);
            if (!_shown)
            {
                _shown = true;
                _fader.Slide(_toast, true); // a message replacing one on screen stays put
            }
        }
        else
        {
            _focused = false;
            _shown = false;
            _fader.Fade(_layer, false);
            _fader.Slide(_toast, false);
            // Collapsed once the fade is done, so the invisible toast takes no clicks and is left out of the tab order.
            double fade = _fader.DurationMilliseconds(false);
            _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(fade, 1));
            _hideTimer.Start();
        }
    }

    private void Arm()
    {
        if (_queue.DueIn is { } due)
        {
            _timer.Interval = due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1);
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }
}
