using Aether.Core.Playback;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VirtualKey = Windows.System.VirtualKey;

namespace Aether_App.Input;

/// <summary>
/// Connects the <c>,</c> (frame back) and <c>.</c> (frame forward) keys to a <see cref="FrameStepper"/>.
/// <para>
/// Key-down steps at once; the OS key auto-repeat is ignored because the stepper paces its own repeats.
/// A <see cref="DispatcherQueueTimer"/> (single-shot, re-armed with the delay <see cref="FrameStepper.Tick"/>
/// returns, stopped when it returns null) drives the repeats. Nothing here blocks the UI thread.
/// </para>
/// <para>
/// A held key is released when the element loses focus (a key-up would never arrive). The caller should
/// also call <see cref="ReleaseAll"/> when the window is deactivated. Must be created on the UI thread.
/// </para>
/// </summary>
public sealed class FrameStepKeyInput : IDisposable
{
    private const VirtualKey OemComma = (VirtualKey)188;
    private const VirtualKey OemPeriod = (VirtualKey)190;

    private readonly UIElement _element;
    private readonly FrameStepper _stepper;
    private readonly DispatcherQueueTimer _timer;
    private bool _disposed;

    public FrameStepKeyInput(UIElement element, FrameStepper stepper, DispatcherQueue dispatcherQueue)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));
        _stepper = stepper ?? throw new ArgumentNullException(nameof(stepper));
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _timer = dispatcherQueue.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += OnTimerTick;

        _element.KeyDown += OnKeyDown;
        _element.KeyUp += OnKeyUp;
        _element.LostFocus += OnLostFocus;
    }

    /// <summary>Releases whichever step key is held and stops the timer (e.g. on window deactivation).</summary>
    public void ReleaseAll()
    {
        _stepper.ReleaseAll();
        _timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _element.KeyDown -= OnKeyDown;
        _element.KeyUp -= OnKeyUp;
        _element.LostFocus -= OnLostFocus;
        _timer.Tick -= OnTimerTick;
        ReleaseAll();
    }

    private static bool TryGetDirection(VirtualKey key, out FrameStepDirection direction)
    {
        switch (key)
        {
            case OemComma:
                direction = FrameStepDirection.Backward;
                return true;
            case OemPeriod:
                direction = FrameStepDirection.Forward;
                return true;
            default:
                direction = default;
                return false;
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // A comma or full stop typed into a field (the Open URL link) is text, not a frame step.
        if (e.OriginalSource is TextBox || !TryGetDirection(e.Key, out FrameStepDirection direction))
        {
            return;
        }
        e.Handled = true;
        if (e.KeyStatus.WasKeyDown)
        {
            return; // OS auto-repeat; the stepper paces its own repeats
        }
        _stepper.Press(direction);
        Rearm();
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (!TryGetDirection(e.Key, out FrameStepDirection direction))
        {
            return;
        }
        e.Handled = true;
        _stepper.Release(direction);
        Rearm();
    }

    private void OnLostFocus(object sender, RoutedEventArgs e) => ReleaseAll();

    private void OnTimerTick(DispatcherQueueTimer sender, object args) => Rearm();

    /// <summary>Ticks the stepper and re-arms the timer for the next due repeat, or stops it.</summary>
    private void Rearm()
    {
        TimeSpan? delay = _stepper.Tick();
        if (delay is { } next)
        {
            _timer.Interval = next;
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }
}
