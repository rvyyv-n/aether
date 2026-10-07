using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Aether.Core.Interop.Mpv;

namespace Aether.Core.Tests.TestSupport;

internal sealed unsafe class FakeMpvEventSource : IMpvEventSource, IDisposable
{
    private readonly object _lock = new();
    private readonly object _allocLock = new();
    private readonly List<nint> _allocations = new();

    private MpvEvent*[] _queue = new MpvEvent*[128];
    private int _head;
    private int _tail;
    private int _count;

    private bool _wakeupLatched;
    private bool _isClosed;
    private bool _failAcquire;
    private int _acquireCount;
    private int _releaseCount;
    private readonly MpvEvent* _noneEvent;

    public FakeMpvEventSource()
    {
        _noneEvent = (MpvEvent*)AllocTracked((nuint)sizeof(MpvEvent));
        _noneEvent->EventId = (int)MpvEventId.None;
    }

    public int AcquireCount => Volatile.Read(ref _acquireCount);
    public int ReleaseCount => Volatile.Read(ref _releaseCount);

    public bool FailAcquire
    {
        get
        {
            lock (_lock) return _failAcquire;
        }
        set
        {
            lock (_lock) _failAcquire = value;
        }
    }

    public bool IsClosed
    {
        get
        {
            lock (_lock) return _isClosed;
        }
        set
        {
            lock (_lock)
            {
                _isClosed = value;
                Monitor.PulseAll(_lock);
            }
        }
    }

    public bool TryAcquire()
    {
        lock (_lock)
        {
            if (_failAcquire || _isClosed)
            {
                return false;
            }

            _acquireCount++;
            return true;
        }
    }

    public void Release()
    {
        Interlocked.Increment(ref _releaseCount);
    }

    public MpvEvent* Wait(double timeoutSeconds)
    {
        lock (_lock)
        {
            if (_wakeupLatched)
            {
                _wakeupLatched = false;
                return _noneEvent;
            }

            if (_count > 0)
            {
                return Dequeue();
            }

            if (_isClosed)
            {
                return _noneEvent;
            }

            if (timeoutSeconds == 0)
            {
                return _noneEvent;
            }

            int timeoutMs = timeoutSeconds < 0 ? Timeout.Infinite : (int)(timeoutSeconds * 1000);

            while (_count == 0 && !_wakeupLatched && !_isClosed)
            {
                if (!Monitor.Wait(_lock, timeoutMs))
                {
                    return _noneEvent;
                }
            }

            if (_wakeupLatched)
            {
                _wakeupLatched = false;
                return _noneEvent;
            }

            if (_count > 0)
            {
                return Dequeue();
            }

            return _noneEvent;
        }
    }

    public bool TryWakeup()
    {
        lock (_lock)
        {
            _wakeupLatched = true;
            Monitor.PulseAll(_lock);
            return !_isClosed;
        }
    }

    public void EnqueueRaw(MpvEvent* evt)
    {
        lock (_lock)
        {
            EnsureCapacity(_count + 1);
            _queue[_tail] = evt;
            _tail = (_tail + 1) % _queue.Length;
            _count++;
            Monitor.Pulse(_lock);
        }
    }

    public void Enqueue(MpvEventId id, int error = 0, ulong replyUserdata = 0)
    {
        MpvEvent* evt = (MpvEvent*)AllocTracked((nuint)sizeof(MpvEvent));
        evt->EventId = (int)id;
        evt->Error = error;
        evt->ReplyUserdata = replyUserdata;
        EnqueueRaw(evt);
    }

    public void EnqueueShutdown(int error = 0, ulong replyUserdata = 0)
    {
        Enqueue(MpvEventId.Shutdown, error, replyUserdata);
    }

    public void EnqueueNone()
    {
        Enqueue(MpvEventId.None);
    }

    public void EnqueuePropertyDouble(ulong id, string name, double value, int error = 0, MpvEventId eventId = MpvEventId.PropertyChange)
    {
        double* d = (double*)AllocTracked((nuint)sizeof(double));
        *d = value;
        EnqueuePropertyRaw(id, name, MpvFormat.Double, d, error, eventId);
    }

    public void EnqueuePropertyInt64(ulong id, string name, long value, int error = 0, MpvEventId eventId = MpvEventId.PropertyChange)
    {
        long* l = (long*)AllocTracked((nuint)sizeof(long));
        *l = value;
        EnqueuePropertyRaw(id, name, MpvFormat.Int64, l, error, eventId);
    }

    public void EnqueuePropertyFlag(ulong id, string name, bool value, int error = 0, MpvEventId eventId = MpvEventId.PropertyChange)
    {
        int* f = (int*)AllocTracked((nuint)sizeof(int));
        *f = value ? 1 : 0;
        EnqueuePropertyRaw(id, name, MpvFormat.Flag, f, error, eventId);
    }

    public void EnqueuePropertyString(ulong id, string name, string value, int error = 0, MpvFormat format = MpvFormat.String, MpvEventId eventId = MpvEventId.PropertyChange)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        byte* strPtr = (byte*)AllocTracked((nuint)(utf8.Length + 1));
        Marshal.Copy(utf8, 0, (nint)strPtr, utf8.Length);
        strPtr[utf8.Length] = 0;

        byte** ptrToChar = (byte**)AllocTracked((nuint)sizeof(byte*));
        *ptrToChar = strPtr;
        EnqueuePropertyRaw(id, name, format, ptrToChar, error, eventId);
    }

    public void EnqueuePropertyUnavailable(ulong id, string name, int error = 0, MpvEventId eventId = MpvEventId.PropertyChange)
    {
        EnqueuePropertyRaw(id, name, MpvFormat.None, null, error, eventId);
    }

    public void EnqueueEndFile(MpvEndFileReason reason, int error = 0, long playlistEntryId = 0, long playlistInsertId = 0, int playlistInsertNumEntries = 0, ulong replyUserdata = 0)
    {
        MpvEventEndFile* endFile = (MpvEventEndFile*)AllocTracked((nuint)sizeof(MpvEventEndFile));
        endFile->Reason = reason;
        endFile->Error = error;
        endFile->PlaylistEntryId = playlistEntryId;
        endFile->PlaylistInsertId = playlistInsertId;
        endFile->PlaylistInsertNumEntries = playlistInsertNumEntries;

        MpvEvent* evt = (MpvEvent*)AllocTracked((nuint)sizeof(MpvEvent));
        evt->EventId = (int)MpvEventId.EndFile;
        evt->Error = error;
        evt->ReplyUserdata = replyUserdata;
        evt->Data = (IntPtr)endFile;

        EnqueueRaw(evt);
    }

    public void EnqueueStartFile(long playlistEntryId, int error = 0, ulong replyUserdata = 0)
    {
        MpvEventStartFile* startFile = (MpvEventStartFile*)AllocTracked((nuint)sizeof(MpvEventStartFile));
        startFile->PlaylistEntryId = playlistEntryId;

        MpvEvent* evt = (MpvEvent*)AllocTracked((nuint)sizeof(MpvEvent));
        evt->EventId = (int)MpvEventId.StartFile;
        evt->Error = error;
        evt->ReplyUserdata = replyUserdata;
        evt->Data = (IntPtr)startFile;

        EnqueueRaw(evt);
    }

    private void EnqueuePropertyRaw(ulong id, string name, MpvFormat format, void* data, int error, MpvEventId eventId)
    {
        byte[] nameUtf8 = Encoding.UTF8.GetBytes(name);
        byte* namePtr = (byte*)AllocTracked((nuint)(nameUtf8.Length + 1));
        Marshal.Copy(nameUtf8, 0, (nint)namePtr, nameUtf8.Length);
        namePtr[nameUtf8.Length] = 0;

        MpvEventProperty* prop = (MpvEventProperty*)AllocTracked((nuint)sizeof(MpvEventProperty));
        prop->Name = namePtr;
        prop->Format = format;
        prop->Data = data;

        MpvEvent* evt = (MpvEvent*)AllocTracked((nuint)sizeof(MpvEvent));
        evt->EventId = (int)eventId;
        evt->Error = error;
        evt->ReplyUserdata = id;
        evt->Data = (IntPtr)prop;

        EnqueueRaw(evt);
    }

    private MpvEvent* Dequeue()
    {
        MpvEvent* item = _queue[_head];
        _queue[_head] = null;
        _head = (_head + 1) % _queue.Length;
        _count--;
        return item;
    }

    private void EnsureCapacity(int min)
    {
        if (min <= _queue.Length)
        {
            return;
        }

        int newCap = Math.Max(_queue.Length * 2, min);
        MpvEvent*[] newQueue = new MpvEvent*[newCap];
        for (int i = 0; i < _count; i++)
        {
            newQueue[i] = _queue[(_head + i) % _queue.Length];
        }
        _queue = newQueue;
        _head = 0;
        _tail = _count;
    }

    private void* AllocTracked(nuint bytes)
    {
        void* ptr = NativeMemory.AllocZeroed(bytes);
        lock (_allocLock)
        {
            _allocations.Add((nint)ptr);
        }
        return ptr;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _isClosed = true;
            Monitor.PulseAll(_lock);
        }

        lock (_allocLock)
        {
            foreach (nint ptr in _allocations)
            {
                NativeMemory.Free((void*)ptr);
            }
            _allocations.Clear();
        }
    }
}
