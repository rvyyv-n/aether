using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Aether.Core.Interop.Mpv;

namespace Aether.Core.Tests.TestSupport;

public sealed unsafe class NativeMpvEvents : IDisposable
{
    private readonly List<IntPtr> _allocations = new();
    private bool _disposed;

    public void* Alloc(nuint byteCount)
    {
        void* ptr = NativeMemory.AllocZeroed(byteCount);
        _allocations.Add((IntPtr)ptr);
        return ptr;
    }

    public byte* CreateUtf8String(string? text)
    {
        if (text is null)
        {
            return null;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text + '\0');
        byte* ptr = (byte*)Alloc((nuint)bytes.Length);
        fixed (byte* src = bytes)
        {
            Buffer.MemoryCopy(src, ptr, bytes.Length, bytes.Length);
        }
        return ptr;
    }

    public MpvEvent* CreatePropertyDouble(ulong propertyId, double value, string? name = null)
    {
        double* data = (double*)Alloc((nuint)sizeof(double));
        *data = value;

        MpvEventProperty* prop = (MpvEventProperty*)Alloc((nuint)sizeof(MpvEventProperty));
        prop->Name = CreateUtf8String(name);
        prop->Format = MpvFormat.Double;
        prop->Data = data;

        MpvEvent* evt = (MpvEvent*)Alloc((nuint)sizeof(MpvEvent));
        evt->EventId = (int)MpvEventId.PropertyChange;
        evt->Error = 0;
        evt->ReplyUserdata = propertyId;
        evt->Data = (IntPtr)prop;
        return evt;
    }

    public MpvEvent* CreatePropertyFlag(ulong propertyId, bool value, string? name = null)
    {
        int* data = (int*)Alloc((nuint)sizeof(int));
        *data = value ? 1 : 0;

        MpvEventProperty* prop = (MpvEventProperty*)Alloc((nuint)sizeof(MpvEventProperty));
        prop->Name = CreateUtf8String(name);
        prop->Format = MpvFormat.Flag;
        prop->Data = data;

        MpvEvent* evt = (MpvEvent*)Alloc((nuint)sizeof(MpvEvent));
        evt->EventId = (int)MpvEventId.PropertyChange;
        evt->Error = 0;
        evt->ReplyUserdata = propertyId;
        evt->Data = (IntPtr)prop;
        return evt;
    }

    public MpvEvent* CreatePropertyUnavailable(ulong propertyId, string? name = null)
    {
        MpvEventProperty* prop = (MpvEventProperty*)Alloc((nuint)sizeof(MpvEventProperty));
        prop->Name = CreateUtf8String(name);
        prop->Format = MpvFormat.None;
        prop->Data = null;

        MpvEvent* evt = (MpvEvent*)Alloc((nuint)sizeof(MpvEvent));
        evt->EventId = (int)MpvEventId.PropertyChange;
        evt->Error = 0;
        evt->ReplyUserdata = propertyId;
        evt->Data = (IntPtr)prop;
        return evt;
    }

    public MpvEvent* CreateStartFile(long playlistEntryId = 1)
    {
        MpvEventStartFile* payload = (MpvEventStartFile*)Alloc((nuint)sizeof(MpvEventStartFile));
        payload->PlaylistEntryId = playlistEntryId;

        MpvEvent* evt = (MpvEvent*)Alloc((nuint)sizeof(MpvEvent));
        evt->EventId = (int)MpvEventId.StartFile;
        evt->Error = 0;
        evt->ReplyUserdata = 0;
        evt->Data = (IntPtr)payload;
        return evt;
    }

    public MpvEvent* CreateFileLoaded()
    {
        MpvEvent* evt = (MpvEvent*)Alloc((nuint)sizeof(MpvEvent));
        evt->EventId = (int)MpvEventId.FileLoaded;
        evt->Error = 0;
        evt->ReplyUserdata = 0;
        evt->Data = IntPtr.Zero;
        return evt;
    }

    public MpvEvent* CreateEndFile(MpvEndFileReason reason = MpvEndFileReason.Eof, int error = 0, long playlistEntryId = 1)
    {
        MpvEventEndFile* payload = (MpvEventEndFile*)Alloc((nuint)sizeof(MpvEventEndFile));
        payload->Reason = reason;
        payload->Error = error;
        payload->PlaylistEntryId = playlistEntryId;

        MpvEvent* evt = (MpvEvent*)Alloc((nuint)sizeof(MpvEvent));
        evt->EventId = (int)MpvEventId.EndFile;
        evt->Error = 0;
        evt->ReplyUserdata = 0;
        evt->Data = (IntPtr)payload;
        return evt;
    }

    public MpvEvent* CreateShutdown()
    {
        MpvEvent* evt = (MpvEvent*)Alloc((nuint)sizeof(MpvEvent));
        evt->EventId = (int)MpvEventId.Shutdown;
        evt->Error = 0;
        evt->ReplyUserdata = 0;
        evt->Data = IntPtr.Zero;
        return evt;
    }

    public MpvEvent* CreateQueueOverflow()
    {
        MpvEvent* evt = (MpvEvent*)Alloc((nuint)sizeof(MpvEvent));
        evt->EventId = (int)MpvEventId.QueueOverflow;
        evt->Error = 0;
        evt->ReplyUserdata = 0;
        evt->Data = IntPtr.Zero;
        return evt;
    }

    public MpvEvent* CreateCommandReply(ulong replyUserdata, int error = 0)
    {
        MpvEvent* evt = (MpvEvent*)Alloc((nuint)sizeof(MpvEvent));
        evt->EventId = (int)MpvEventId.CommandReply;
        evt->Error = error;
        evt->ReplyUserdata = replyUserdata;
        evt->Data = IntPtr.Zero;
        return evt;
    }

    public static void UpdatePropertyDouble(MpvEvent* evt, double newValue)
    {
        MpvEventProperty* prop = (MpvEventProperty*)evt->Data;
        *(double*)prop->Data = newValue;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        for (int i = 0; i < _allocations.Count; i++)
        {
            NativeMemory.Free((void*)_allocations[i]);
        }
        _allocations.Clear();
    }
}
