using System;
using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Mpv;

/// <summary>
/// One <c>mpv_event</c>, read in place. mpv owns the memory and reuses it on the next
/// <c>mpv_wait_event</c>, so this is a ref struct: it cannot be stored, boxed or captured, and
/// nothing is copied or marshalled unless the caller asks for it. Copy what you need inside
/// <see cref="IMpvEventSink.OnEvent"/>.
/// </summary>
public readonly unsafe ref struct MpvEventView
{
    private readonly MpvEvent* _event;

    /// <param name="mpvEvent">A non-null event; valid for the lifetime of this view.</param>
    public MpvEventView(MpvEvent* mpvEvent)
    {
        ArgumentNullException.ThrowIfNull(mpvEvent);
        _event = mpvEvent;
    }

    public MpvEventId Id => (MpvEventId)_event->EventId;

    /// <summary>The <c>mpv_error</c> of a reply event; 0 otherwise.</summary>
    public int Error => _event->Error;

    /// <summary>The id passed to the async request (or <c>mpv_observe_property</c>) this event answers.</summary>
    public ulong ReplyUserdata => _event->ReplyUserdata;

    /// <summary>Property-change or get-property-reply payload.</summary>
    public bool TryGetProperty(out MpvPropertyChange change)
    {
        if ((Id is MpvEventId.PropertyChange or MpvEventId.GetPropertyReply) && _event->Data != 0)
        {
            change = new MpvPropertyChange(_event->ReplyUserdata, (MpvEventProperty*)_event->Data);
            return true;
        }
        change = default;
        return false;
    }

    public bool TryGetEndFile(out MpvEventEndFile endFile)
    {
        if (Id == MpvEventId.EndFile && _event->Data != 0)
        {
            endFile = *(MpvEventEndFile*)_event->Data;
            return true;
        }
        endFile = default;
        return false;
    }

    public bool TryGetStartFile(out MpvEventStartFile startFile)
    {
        if (Id == MpvEventId.StartFile && _event->Data != 0)
        {
            startFile = *(MpvEventStartFile*)_event->Data;
            return true;
        }
        startFile = default;
        return false;
    }
}

/// <summary>
/// A property value read in place from an <c>mpv_event_property</c>. Register properties with
/// <see cref="MpvContext.ObserveProperty"/> under a numeric id and dispatch on <see cref="Id"/>, so
/// the hot path never compares or allocates names.
/// </summary>
public readonly unsafe ref struct MpvPropertyChange
{
    private readonly MpvEventProperty* _property;

    internal MpvPropertyChange(ulong id, MpvEventProperty* property)
    {
        Id = id;
        _property = property;
    }

    /// <summary>The id the property was observed under.</summary>
    public ulong Id { get; }

    /// <summary>The property name as UTF-8, without copying.</summary>
    public ReadOnlySpan<byte> NameUtf8 =>
        _property->Name == null ? default : MemoryMarshal.CreateReadOnlySpanFromNullTerminated(_property->Name);

    /// <summary><see cref="MpvFormat.None"/> when the property is unavailable (e.g. no file loaded).</summary>
    public MpvFormat Format => _property->Data == null ? MpvFormat.None : _property->Format;

    public bool IsAvailable => Format != MpvFormat.None;

    public bool TryGetDouble(out double value)
    {
        if (Format == MpvFormat.Double)
        {
            value = *(double*)_property->Data;
            return true;
        }
        value = 0;
        return false;
    }

    public bool TryGetInt64(out long value)
    {
        if (Format == MpvFormat.Int64)
        {
            value = *(long*)_property->Data;
            return true;
        }
        value = 0;
        return false;
    }

    public bool TryGetFlag(out bool value)
    {
        if (Format == MpvFormat.Flag)
        {
            value = *(int*)_property->Data != 0;
            return true;
        }
        value = false;
        return false;
    }

    /// <summary>String or OSD-string value as UTF-8, without copying; false for other formats.</summary>
    public bool TryGetStringUtf8(out ReadOnlySpan<byte> value)
    {
        if (Format is MpvFormat.String or MpvFormat.OsdString)
        {
            byte* s = *(byte**)_property->Data;
            value = s == null ? default : MemoryMarshal.CreateReadOnlySpanFromNullTerminated(s);
            return true;
        }
        value = default;
        return false;
    }
}
