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
    private static readonly PropertyChangedEventArgs VideoWidthArgs = new(nameof(VideoWidth));
    private static readonly PropertyChangedEventArgs VideoHeightArgs = new(nameof(VideoHeight));
    private static readonly PropertyChangedEventArgs DecoderArgs = new(nameof(Decoder));
    private static readonly PropertyChangedEventArgs VideoFormatArgs = new(nameof(VideoFormat));
    private static readonly PropertyChangedEventArgs CodedWidthArgs = new(nameof(CodedWidth));
    private static readonly PropertyChangedEventArgs CodedHeightArgs = new(nameof(CodedHeight));
    private static readonly PropertyChangedEventArgs EstimatedFpsArgs = new(nameof(EstimatedFps));
    private static readonly PropertyChangedEventArgs DisplayFpsArgs = new(nameof(DisplayFps));
    private static readonly PropertyChangedEventArgs DecoderDroppedFramesArgs = new(nameof(DecoderDroppedFrames));
    private static readonly PropertyChangedEventArgs DroppedFramesArgs = new(nameof(DroppedFrames));
    private static readonly PropertyChangedEventArgs CacheDurationArgs = new(nameof(CacheDuration));
    private static readonly PropertyChangedEventArgs IsLoopingArgs = new(nameof(IsLooping));

    private PlayerState _state = PlayerState.Initial;
    private bool _isLooping;

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

    /// <summary>Display width in pixels after aspect correction (<c>dwidth</c>); 0 without video.</summary>
    public int VideoWidth => _state.VideoWidth;

    /// <summary>Display height in pixels (<c>dheight</c>); 0 without video.</summary>
    public int VideoHeight => _state.VideoHeight;

    /// <summary>Active hardware decoder (<c>hwdec-current</c>), "no" for software; null when unavailable.</summary>
    public string? Decoder => _state.Decoder;

    /// <summary>Video codec name (<c>video-format</c>); null when unavailable.</summary>
    public string? VideoFormat => _state.VideoFormat;

    /// <summary>Decoded picture width in pixels (<c>video-params/w</c>); 0 without video.</summary>
    public int CodedWidth => _state.CodedWidth;

    /// <summary>Decoded picture height in pixels (<c>video-params/h</c>); 0 without video.</summary>
    public int CodedHeight => _state.CodedHeight;

    /// <summary>Measured output frame rate (<c>estimated-vf-fps</c>); 0 when unknown.</summary>
    public double EstimatedFps => _state.EstimatedFps;

    /// <summary>Display refresh rate mpv sees (<c>display-fps</c>); 0 when unknown.</summary>
    public double DisplayFps => _state.DisplayFps;

    /// <summary>Frames the decoder dropped (<c>decoder-frame-drop-count</c>).</summary>
    public long DecoderDroppedFrames => _state.DecoderDroppedFrames;

    /// <summary>Frames dropped at output (<c>frame-drop-count</c>).</summary>
    public long DroppedFrames => _state.DroppedFrames;

    /// <summary>Seconds buffered ahead (<c>demuxer-cache-duration</c>); negative when unavailable.</summary>
    public double CacheDuration => _state.CacheDuration;

    /// <summary>Restart from 0 when the file ends (see <see cref="LoopRestart"/>). UI thread; not part of <see cref="State"/>.</summary>
    public bool IsLooping
    {
        get => _isLooping;
        set
        {
            if (_isLooping != value)
            {
                _isLooping = value;
                Raise(IsLoopingArgs);
            }
        }
    }

    public void ToggleLoop() => IsLooping = !IsLooping;

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
        if (previous.VideoWidth != next.VideoWidth) Raise(VideoWidthArgs);
        if (previous.VideoHeight != next.VideoHeight) Raise(VideoHeightArgs);
        if (!string.Equals(previous.Decoder, next.Decoder)) Raise(DecoderArgs);
        if (!string.Equals(previous.VideoFormat, next.VideoFormat)) Raise(VideoFormatArgs);
        if (previous.CodedWidth != next.CodedWidth) Raise(CodedWidthArgs);
        if (previous.CodedHeight != next.CodedHeight) Raise(CodedHeightArgs);
        if (!previous.EstimatedFps.Equals(next.EstimatedFps)) Raise(EstimatedFpsArgs);
        if (!previous.DisplayFps.Equals(next.DisplayFps)) Raise(DisplayFpsArgs);
        if (previous.DecoderDroppedFrames != next.DecoderDroppedFrames) Raise(DecoderDroppedFramesArgs);
        if (previous.DroppedFrames != next.DroppedFrames) Raise(DroppedFramesArgs);
        if (!previous.CacheDuration.Equals(next.CacheDuration)) Raise(CacheDurationArgs);
    }

    private void Raise(PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, args);
}
