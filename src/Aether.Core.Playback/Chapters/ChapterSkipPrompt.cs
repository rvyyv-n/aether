namespace Aether.Core.Playback.Chapters;

/// <summary>
/// When the discreet Skip button shows while auto-skip is off (9.7): for the first <see cref="PromptSeconds"/> of
/// playback into a chapter to skip, and again whenever the controls are up, so it does not sit over the picture for a
/// whole sponsor read. Once used it stays away until playback leaves that chapter. Feed it every position update;
/// a run of matching chapters is one offer, known by where it ends.
/// </summary>
public sealed class ChapterSkipPrompt
{
    /// <summary>How long the button stays up by itself, in seconds of playback, once a chapter to skip starts.</summary>
    public const double PromptSeconds = 8;

    private double? _target;
    private double _since;
    private double? _used;

    /// <param name="target">Where the offered chapter ends, or null when no chapter to skip is playing.</param>
    /// <param name="controlsShown">The player controls are up (or the pointer is on the button), which brings it back.</param>
    /// <returns>Whether the button should show now.</returns>
    public bool Update(double? target, double position, bool controlsShown)
    {
        if (target is not { } offer)
        {
            _target = null;
            _used = null;
            return false;
        }
        // A new chapter, or a seek back within this one, starts the prompt over.
        if (_target != offer || position < _since)
        {
            _target = offer;
            _since = position;
        }
        if (_used == offer)
        {
            return false;
        }
        return controlsShown || position - _since < PromptSeconds;
    }

    /// <summary>The user skipped: the button goes at once, even for position updates still on the old side of the seek.</summary>
    public void Used()
    {
        _used = _target;
    }
}
