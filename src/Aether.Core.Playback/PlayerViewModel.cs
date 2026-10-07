using System.ComponentModel;

namespace Aether.Core.Playback;

/// <summary>
/// Observable player state for the UI: one <see cref="PlayerState"/> value, mirrored by read-only
/// properties. Only <see cref="Apply"/> changes it, on the UI thread; commands to mpv do not go
/// through here.
/// </summary>
public sealed class PlayerViewModel : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs StateArgs = new(nameof(State));
    private static readonly PropertyChangedEventArgs PositionArgs = new(nameof(Position));
    private static readonly PropertyChangedEventArgs DurationArgs = new(nameof(Duration));
    private static readonly PropertyChangedEventArgs SpeedArgs = new(nameof(Speed));
    private static readonly PropertyChangedEventArgs VolumeArgs = new(nameof(Volume));
    private static readonly PropertyChangedEventArgs IsPausedArgs = new(nameof(IsPaused));
    private static readonly PropertyChangedEventArgs IsAtEndArgs = new(nameof(IsAtEnd));
    private static readonly PropertyChangedEventArgs FramesPerSecondArgs = new(nameof(FramesPerSecond));
    private static readonly PropertyChangedEventArgs StatusArgs = new(nameof(Status));

    private PlayerState _state = PlayerState.Initial;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlayerState State => _state;

    /// <summary>Seconds into the file (<c>time-pos</c>).</summary>
    public double Position => _state.Position;

    /// <summary>File length in seconds (<c>duration</c>); 0 when unknown.</summary>
    public double Duration => _state.Duration;

    public double Speed => _state.Speed;

    /// <summary>100 is unity gain.</summary>
    public double Volume => _state.Volume;

    public bool IsPaused => _state.IsPaused;

    /// <summary>The loaded file reached its end (<c>eof-reached</c>).</summary>
    public bool IsAtEnd => _state.IsAtEnd;

    /// <summary>Container frame rate; 0 when unknown.</summary>
    public double FramesPerSecond => _state.FramesPerSecond;

    public PlaybackStatus Status => _state.Status;

    /// <summary>
    /// UI thread. Replaces the state and raises <see cref="PropertyChanged"/> for each field that
    /// differs (and <see cref="State"/> once if any did). Shared event args: no allocation per call.
    /// </summary>
    public void Apply(in PlayerState next)
    {
        PlayerState previous = _state;
        if (previous.Equals(next))
        {
            return;
        }

        _state = next;
        Raise(StateArgs);
        if (!previous.Position.Equals(next.Position)) Raise(PositionArgs);
        if (!previous.Duration.Equals(next.Duration)) Raise(DurationArgs);
        if (!previous.Speed.Equals(next.Speed)) Raise(SpeedArgs);
        if (!previous.Volume.Equals(next.Volume)) Raise(VolumeArgs);
        if (previous.IsPaused != next.IsPaused) Raise(IsPausedArgs);
        if (previous.IsAtEnd != next.IsAtEnd) Raise(IsAtEndArgs);
        if (!previous.FramesPerSecond.Equals(next.FramesPerSecond)) Raise(FramesPerSecondArgs);
        if (previous.Status != next.Status) Raise(StatusArgs);
    }

    private void Raise(PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, args);
}
