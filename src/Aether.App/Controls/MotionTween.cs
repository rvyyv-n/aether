using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Xaml.Media;

namespace Aether_App.Controls;

/// <summary>
/// A value moved towards a target once per rendered frame on the UI thread, for motion that has to go through
/// layout (the transport card's height, which the render thread's blur and the chip above it follow). Quick eases
/// over a fixed time; Springy runs <see cref="Spring"/> and keeps its speed when the target changes midway.
/// With Windows animations off it jumps. UI thread only.
/// </summary>
internal sealed class MotionTween
{
    private readonly Stopwatch _clock = new();
    private bool _running;
    private bool _springy;
    private Spring _spring;
    private double _velocity;
    private double _from;
    private double _durationSeconds;
    private Vector4 _curve;
    private TimeSpan _last;

    public MotionTween(double value) => Value = Target = value;

    public double Value { get; private set; }

    public double Target { get; private set; }

    /// <summary>Raised after <see cref="Value"/> changes, once per frame while it moves.</summary>
    public event EventHandler? Changed;

    /// <param name="durationMilliseconds">How long the Quick profile takes.</param>
    /// <param name="curve">The Quick profile's easing.</param>
    public void AnimateTo(double target, double durationMilliseconds, Vector4 curve)
    {
        Target = target;
        if (!Motion.AnimationsEnabled)
        {
            JumpTo(target);
            return;
        }
        bool springy = Motion.IsSpringy;
        if (!springy || !_springy)
        {
            _velocity = 0; // only a spring carries its speed into the new target
        }
        _springy = springy;
        _spring = Spring.FromTokens();
        _from = Value;
        _durationSeconds = durationMilliseconds / 1000;
        _curve = curve;
        _clock.Restart();
        _last = TimeSpan.Zero;
        if (!_running)
        {
            _running = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    public void JumpTo(double value)
    {
        Stop();
        Target = value;
        _velocity = 0;
        Value = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (_running)
        {
            _running = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, object e)
    {
        TimeSpan now = _clock.Elapsed;
        bool done;
        if (_springy)
        {
            double value = Value;
            done = _spring.Advance(ref value, ref _velocity, Target, (now - _last).TotalSeconds);
            Value = value;
        }
        else
        {
            double t = _durationSeconds > 0 ? now.TotalSeconds / _durationSeconds : 1;
            done = t >= 1;
            Value = done ? Target : _from + ((Target - _from) * Motion.Ease(_curve, t));
        }
        _last = now;
        if (done)
        {
            Stop();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
