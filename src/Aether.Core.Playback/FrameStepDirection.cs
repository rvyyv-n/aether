namespace Aether.Core.Playback;

/// <summary>Which way a frame step moves; the value is the sign of the step.</summary>
public enum FrameStepDirection
{
    /// <summary>One frame back (<c>frame-back-step</c>).</summary>
    Backward = -1,

    /// <summary>One frame forward (<c>frame-step</c>).</summary>
    Forward = 1,
}
