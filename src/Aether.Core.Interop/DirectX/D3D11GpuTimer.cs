using System;
using System.Threading;
using Aether.Core.Interop.DirectX.Native;

namespace Aether.Core.Interop.DirectX;

/// <summary>
/// Non-blocking 4-frame Direct3D 11 timestamp query ring measuring true GPU frame execution time
/// on <see cref="D3D11DeviceManager"/> without stalling the immediate context or allocating per frame.
/// <para>
/// Owned and driven exclusively by one render thread per device generation and disposed before
/// <see cref="D3D11DeviceManager.Dispose"/>.
/// </para>
/// </summary>
public sealed unsafe class D3D11GpuTimer : IDisposable
{
    private const int SlotCount = 4;

    private struct QuerySlot
    {
        public nint Disjoint;
        public nint Start;
        public nint End;
        public bool InFlight;
    }

    private readonly nint _context;
    private readonly QuerySlot[] _slots;
    private int _writeIndex;
    private int _activeSlot = -1;
    private bool _disposed;

    private double _totalMs;
    private double _maxMs;
    private long _sampleCount;
    private long _lastMsBits = BitConverter.DoubleToInt64Bits(-1.0);
    private long _totalMsBits = BitConverter.DoubleToInt64Bits(0.0);
    private long _maxMsBits = BitConverter.DoubleToInt64Bits(0.0);

    private D3D11GpuTimer(nint context, QuerySlot[] slots)
    {
        _context = context;
        _slots = slots;
    }

    ~D3D11GpuTimer() => ReleaseQueries();

    /// <summary>Latest smoothed GPU frame duration in milliseconds, or -1 before the first completed sample.</summary>
    public double LastFrameMilliseconds => BitConverter.Int64BitsToDouble(Volatile.Read(ref _lastMsBits));

    /// <summary>Exact unsmoothed sum of completed GPU frame samples in milliseconds.</summary>
    public double TotalFrameMilliseconds => BitConverter.Int64BitsToDouble(Volatile.Read(ref _totalMsBits));

    /// <summary>Exact unsmoothed average GPU frame duration in milliseconds, or -1 before the first completed sample.</summary>
    public double AverageFrameMilliseconds
    {
        get
        {
            long count = Volatile.Read(ref _sampleCount);
            return count > 0 ? TotalFrameMilliseconds / count : -1.0;
        }
    }

    /// <summary>Maximum GPU frame duration in milliseconds, or -1 before the first completed sample.</summary>
    public double MaxFrameMilliseconds
    {
        get
        {
            long count = Volatile.Read(ref _sampleCount);
            return count > 0 ? BitConverter.Int64BitsToDouble(Volatile.Read(ref _maxMsBits)) : -1.0;
        }
    }

    public long SampleCount => Volatile.Read(ref _sampleCount);

    /// <summary>
    /// Creates the 4-frame query ring on <paramref name="device"/>. Returns null if the driver refuses
    /// timestamp queries instead of failing video playback.
    /// </summary>
    public static D3D11GpuTimer? TryCreate(D3D11DeviceManager device)
    {
        ArgumentNullException.ThrowIfNull(device);
        nint devPtr = device.DevicePointer;
        nint ctxPtr = device.ImmediateContextPointer;

        var slots = new QuerySlot[SlotCount];
        D3D11QueryDesc disjointDesc = new() { Query = D3D11QueryType.TimestampDisjoint };
        D3D11QueryDesc timestampDesc = new() { Query = D3D11QueryType.Timestamp };

        try
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (CreateQuery(devPtr, &disjointDesc, out slots[i].Disjoint) < 0
                    || CreateQuery(devPtr, &timestampDesc, out slots[i].Start) < 0
                    || CreateQuery(devPtr, &timestampDesc, out slots[i].End) < 0)
                {
                    ReleaseSlots(slots);
                    return null;
                }
            }
            return new D3D11GpuTimer(ctxPtr, slots);
        }
        catch
        {
            ReleaseSlots(slots);
            throw;
        }
    }

    /// <summary>
    /// Drains any completed query slots from previous frames without flushing, then begins a new
    /// frame bracket if the next ring slot is free. Returns false if all slots are still in flight.
    /// Zero allocations.
    /// </summary>
    public bool TryBeginFrame()
    {
        if (_disposed)
        {
            return false;
        }

        PollCompleted();

        if (_activeSlot >= 0)
        {
            return false;
        }

        ref QuerySlot slot = ref _slots[_writeIndex];
        if (slot.InFlight)
        {
            return false;
        }

        BeginQuery(_context, slot.Disjoint);
        EndQuery(_context, slot.Start);
        _activeSlot = _writeIndex;
        return true;
    }

    /// <summary>Closes the bracket started by <see cref="TryBeginFrame"/> and marks the slot in flight. Zero allocations.</summary>
    public void EndFrame()
    {
        if (_disposed || _activeSlot < 0)
        {
            return;
        }

        ref QuerySlot slot = ref _slots[_activeSlot];
        EndQuery(_context, slot.End);
        EndQuery(_context, slot.Disjoint);
        slot.InFlight = true;
        _writeIndex = (_writeIndex + 1) & (SlotCount - 1);
        _activeSlot = -1;
    }

    /// <summary>Non-blocking poll of oldest-first in-flight query slots using <c>D3D11_ASYNC_GETDATA_DONOTFLUSH</c>.</summary>
    public void PollCompleted()
    {
        if (_disposed)
        {
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            int idx = (_writeIndex + i) & (SlotCount - 1);
            ref QuerySlot slot = ref _slots[idx];
            if (!slot.InFlight)
            {
                continue;
            }

            D3D11QueryDataTimestampDisjoint disjoint = default;
            int hrDisjoint = GetData(_context, slot.Disjoint, &disjoint, (uint)sizeof(D3D11QueryDataTimestampDisjoint), DxNative.D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if (hrDisjoint == DxNative.S_FALSE)
            {
                break;
            }
            if (hrDisjoint < 0)
            {
                slot.InFlight = false;
                continue;
            }

            ulong startTs = 0;
            int hrStart = GetData(_context, slot.Start, &startTs, sizeof(ulong), DxNative.D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if (hrStart == DxNative.S_FALSE)
            {
                break;
            }
            if (hrStart < 0)
            {
                slot.InFlight = false;
                continue;
            }

            ulong endTs = 0;
            int hrEnd = GetData(_context, slot.End, &endTs, sizeof(ulong), DxNative.D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if (hrEnd == DxNative.S_FALSE)
            {
                break;
            }

            slot.InFlight = false;
            if (hrEnd < 0 || disjoint.Disjoint != 0 || disjoint.Frequency == 0 || endTs < startTs)
            {
                continue;
            }

            double ms = (endTs - startTs) * 1000.0 / disjoint.Frequency;
            long nextCount = _sampleCount + 1;
            _totalMs += ms;
            if (ms > _maxMs || _sampleCount == 0)
            {
                _maxMs = ms;
            }

            double prevLast = BitConverter.Int64BitsToDouble(Volatile.Read(ref _lastMsBits));
            double smoothed = prevLast < 0 ? ms : (prevLast * 0.8) + (ms * 0.2);

            Volatile.Write(ref _totalMsBits, BitConverter.DoubleToInt64Bits(_totalMs));
            Volatile.Write(ref _maxMsBits, BitConverter.DoubleToInt64Bits(_maxMs));
            Volatile.Write(ref _lastMsBits, BitConverter.DoubleToInt64Bits(smoothed));
            Volatile.Write(ref _sampleCount, nextCount);
        }
    }

    public void Dispose()
    {
        ReleaseQueries();
        GC.SuppressFinalize(this);
    }

    private void ReleaseQueries()
    {
        _disposed = true;
        _activeSlot = -1;
        ReleaseSlots(_slots);
    }

    private static void ReleaseSlots(QuerySlot[] slots)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            Com.Release(ref slots[i].Disjoint);
            Com.Release(ref slots[i].Start);
            Com.Release(ref slots[i].End);
            slots[i].InFlight = false;
        }
    }

    // ID3D11Device::CreateQuery is vtable slot 24.
    private static int CreateQuery(nint device, D3D11QueryDesc* desc, out nint query)
    {
        nint q = 0;
        int hr = ((delegate* unmanaged[Stdcall]<nint, D3D11QueryDesc*, nint*, int>)Com.Vtbl(device)[24])(device, desc, &q);
        query = hr >= 0 ? q : 0;
        return hr;
    }

    // ID3D11DeviceContext::Begin (27), End (28), GetData (29).
    private static void BeginQuery(nint context, nint async) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)Com.Vtbl(context)[27])(context, async);

    private static void EndQuery(nint context, nint async) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)Com.Vtbl(context)[28])(context, async);

    private static int GetData(nint context, nint async, void* data, uint dataSize, uint flags) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, void*, uint, uint, int>)Com.Vtbl(context)[29])(context, async, data, dataSize, flags);
}
