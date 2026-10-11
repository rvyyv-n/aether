namespace Aether.Core.Playback;

/// <summary>Where the Open URL flow is (screen 2.5, 2.6, 2.12).</summary>
public enum OpenUrlStage
{
    Closed,

    /// <summary>The dialog is open.</summary>
    Editing,

    /// <summary>The dialog has closed and the home view shows the progress ring with the site name.</summary>
    GettingStream,

    /// <summary>Replaces <see cref="GettingStream"/> after <see cref="OpenUrlFlow.StillWorkingAfter"/>.</summary>
    StillWorking,
}

/// <summary>The line under the dialog's field.</summary>
public enum OpenUrlNote
{
    Hint,

    /// <summary>Amber dot and "That isn't a web link".</summary>
    Invalid,

    /// <summary>Play was pressed but links cannot play yet (no streaming).</summary>
    Unavailable,
}

/// <summary>What <see cref="OpenUrlFlow.Submit"/> did.</summary>
public enum OpenUrlSubmitResult
{
    /// <summary>The text is not a web link; nothing changed.</summary>
    Rejected,

    /// <summary>Streaming is not available; the dialog stays open with <see cref="OpenUrlNote.Unavailable"/>.</summary>
    Unavailable,

    /// <summary>The dialog closed and the flow is getting the stream.</summary>
    Started,
}

/// <summary>
/// Open URL dialog and link progress. The page owns the timer: it re-arms it to <see cref="DueIn"/> after
/// every change and calls <see cref="Tick"/> when it fires. Not thread-safe (UI thread only).
/// </summary>
public sealed class OpenUrlFlow(TimeProvider time, bool streamingAvailable)
{
    public static readonly TimeSpan StillWorkingAfter = TimeSpan.FromSeconds(10);

    private long _startedAt;
    private bool _unavailable;

    public OpenUrlStage Stage { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>The link being fetched, set by <see cref="Submit"/>.</summary>
    public string? Link { get; private set; }

    /// <summary>The site shown under the progress title.</summary>
    public string? Host => WebLink.Host(Link);

    /// <summary>Play is enabled once the text starts with http:// or https://.</summary>
    public bool CanSubmit => WebLink.IsValid(Text);

    public OpenUrlNote Note =>
        _unavailable ? OpenUrlNote.Unavailable
        : Text.Length > 0 && !CanSubmit ? OpenUrlNote.Invalid
        : OpenUrlNote.Hint;

    /// <summary>When <see cref="Tick"/> next has work to do, or null.</summary>
    public TimeSpan? DueIn => Stage == OpenUrlStage.GettingStream
        ? StillWorkingAfter - time.GetElapsedTime(_startedAt)
        : null;

    /// <summary>Opens the dialog, prefilled with the clipboard only when it holds a web link.</summary>
    public void Open(string? clipboardText)
    {
        Stage = OpenUrlStage.Editing;
        Text = WebLink.IsValid(clipboardText) ? clipboardText!.Trim() : string.Empty;
        Link = null;
        _unavailable = false;
    }

    public void SetText(string? text)
    {
        Text = text ?? string.Empty;
        _unavailable = false;
    }

    public OpenUrlSubmitResult Submit()
    {
        if (Stage != OpenUrlStage.Editing || !CanSubmit)
        {
            return OpenUrlSubmitResult.Rejected;
        }
        if (!streamingAvailable)
        {
            _unavailable = true;
            return OpenUrlSubmitResult.Unavailable;
        }
        Link = Text.Trim();
        Stage = OpenUrlStage.GettingStream;
        _startedAt = time.GetTimestamp();
        return OpenUrlSubmitResult.Started;
    }

    public void Tick()
    {
        if (Stage == OpenUrlStage.GettingStream && time.GetElapsedTime(_startedAt) >= StillWorkingAfter)
        {
            Stage = OpenUrlStage.StillWorking;
        }
    }

    /// <summary>Cancel in the dialog or on the progress view; also the end of a fetch.</summary>
    public void Close()
    {
        Stage = OpenUrlStage.Closed;
        Link = null;
        _unavailable = false;
    }
}
