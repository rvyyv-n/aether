using Aether.Core.Interop.Mpv;

namespace Aether.Core.Playback;

/// <summary>
/// Asks for one property value without waiting for it. The value must come back later on the event thread as a
/// get-property reply carrying <c>replyUserdata</c>.
/// </summary>
public interface IMpvPropertyRequester
{
    /// <summary>Requests one value. Must not block. Throws <see cref="MpvException"/> if the core rejects it outright.</summary>
    void Request(ulong replyUserdata, string name, MpvFormat format);
}

/// <summary>Requests property values from an mpv core with <c>mpv_get_property_async</c>.</summary>
public sealed class MpvPropertyRequester(MpvContext context) : IMpvPropertyRequester
{
    private readonly MpvContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public void Request(ulong replyUserdata, string name, MpvFormat format) => _context.GetPropertyAsync(replyUserdata, name, format);
}
