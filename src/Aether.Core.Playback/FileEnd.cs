namespace Aether.Core.Playback;

/// <summary>
/// Spots a file playing to its end. <c>keep-open</c> holds the last frame, so a natural end shows up as
/// <c>eof-reached</c> turning true; stopping, an error or opening another file never sets it. Reporting once per
/// edge keeps a slow flush from advancing twice.
/// </summary>
public sealed class FileEnd
{
    private bool _wasAtEnd;

    /// <summary>Call with the latest state on each flush; true once when the file has just ended.</summary>
    public bool Reached(bool isAtEnd)
    {
        bool reached = isAtEnd && !_wasAtEnd;
        _wasAtEnd = isAtEnd;
        return reached;
    }
}
