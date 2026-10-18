namespace Aether.Core.Playback;

/// <summary>What is open, as far as streaming to a phone is concerned: only a local file can be served.</summary>
public enum LanStreamSource
{
    Nothing,
    Link,
    File,
}

/// <summary>Where the phone stream server is. The last two are off, with the reason the last start failed.</summary>
public enum LanServerState
{
    Off,
    Starting,
    On,
    PortUnavailable,
    Failed,
}

public enum LanStreamTone
{
    Neutral,
    Ready,
    Warning,
}

/// <summary>
/// What the phone pairing card shows (11.3): the switch, the status line under the title, the code and address, and
/// one note line for a reason or a problem. Strings are resource keys; the page formats the watcher count.
/// </summary>
/// <param name="HasAddress">A home-network address was found for the phone to reach this PC on.</param>
/// <param name="Watchers">Phones streaming right now, as the server counts them.</param>
/// <param name="AddressChanged">The address moved while the server was on, so a phone has to scan again.</param>
public readonly record struct LanStreamPanel(
    LanStreamSource Source, LanServerState Server, bool HasAddress, int Watchers, bool AddressChanged)
{
    public bool SwitchOn => Server is LanServerState.Starting or LanServerState.On;

    /// <summary>On only for a local file and a network to share it on; off always works, except mid-start.</summary>
    public bool SwitchEnabled => Server switch
    {
        LanServerState.Starting => false,
        LanServerState.On => true,
        _ => Source == LanStreamSource.File && HasAddress,
    };

    public bool ShowsCode => Server == LanServerState.On && HasAddress;

    public string StatusKey => Server switch
    {
        LanServerState.Starting => "LanStream_StatusStarting",
        LanServerState.On when Watchers == 1 => "LanStream_StatusWatchingOne",
        LanServerState.On when Watchers > 1 => "LanStream_StatusWatchingMany",
        LanServerState.On => "LanStream_StatusWaiting",
        _ => "LanStream_StatusOff",
    };

    public LanStreamTone StatusTone => Server == LanServerState.On ? LanStreamTone.Ready : LanStreamTone.Neutral;

    /// <summary>The one note under the code, most pressing first; null for none.</summary>
    public string? NoteKey
    {
        get
        {
            if (!SwitchOn && Source != LanStreamSource.File)
            {
                return Source == LanStreamSource.Link ? "LanStream_NoteLink" : "LanStream_NoteNoFile";
            }
            return Server switch
            {
                LanServerState.PortUnavailable => "LanStream_NotePortBusy",
                LanServerState.Failed => "LanStream_NoteFailed",
                _ when !HasAddress => "LanStream_NoteNoNetwork",
                LanServerState.On when AddressChanged => "LanStream_NoteAddressChanged",
                _ => null,
            };
        }
    }

    /// <summary>Problems get the warning dot; a plain reason (no file, a link) and the address change get none.</summary>
    public bool NoteIsWarning => NoteKey is "LanStream_NotePortBusy" or "LanStream_NoteFailed" or "LanStream_NoteNoNetwork";
}
