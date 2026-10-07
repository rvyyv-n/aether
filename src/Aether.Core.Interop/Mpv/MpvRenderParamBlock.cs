using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Aether.Core.Interop.Mpv;

/// <summary>
/// One unmanaged allocation holding an <c>INVALID</c>-terminated <c>mpv_render_param</c> array
/// followed by the payloads it points at. The block never moves, so the pointers returned by
/// <see cref="Add{T}"/> stay valid and can be rewritten in place each frame without allocating.
/// <para>Not thread-safe: owned by one thread (the render thread). Freed by <see cref="Dispose"/> or the finalizer.</para>
/// </summary>
public sealed unsafe class MpvRenderParamBlock : IDisposable
{
    private const int Alignment = 16;

    private static long _liveBlocks;

    private byte* _memory;
    private readonly int _paramCapacity;
    private readonly int _payloadOffset;
    private readonly int _payloadCapacity;
    private int _count;
    private int _payloadUsed;

    /// <param name="paramCapacity">Parameters, excluding the terminator.</param>
    /// <param name="payloadCapacity">Bytes reserved for payloads (each payload is 16-byte aligned).</param>
    public MpvRenderParamBlock(int paramCapacity, int payloadCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(paramCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(payloadCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(paramCapacity, 64);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(payloadCapacity, 64 * 1024);

        _paramCapacity = paramCapacity;
        _payloadOffset = AlignUp((paramCapacity + 1) * sizeof(MpvRenderParam));
        _payloadCapacity = payloadCapacity;
        // Zeroed memory: every unused slot, including the terminator, is MPV_RENDER_PARAM_INVALID.
        _memory = (byte*)NativeMemory.AlignedAlloc((nuint)(_payloadOffset + payloadCapacity), Alignment);
        NativeMemory.Clear(_memory, (nuint)(_payloadOffset + payloadCapacity));
        Interlocked.Increment(ref _liveBlocks);
    }

    ~MpvRenderParamBlock() => Free();

    /// <summary>Blocks allocated and not yet freed, process-wide. For leak checks.</summary>
    public static long LiveBlocks => Interlocked.Read(ref _liveBlocks);

    /// <summary>The terminated parameter array, for <c>mpv_render_context_create</c>/<c>render</c>.</summary>
    public MpvRenderParam* Parameters => (MpvRenderParam*)Memory;

    public int Count => _count;

    public int ParamCapacity => _paramCapacity;

    public int PayloadCapacity => _payloadCapacity;

    public int PayloadUsed => _payloadUsed;

    public bool IsDisposed => _memory == null;

    private byte* Memory => _memory != null ? _memory : throw new ObjectDisposedException(nameof(MpvRenderParamBlock));

    /// <summary>Copies <paramref name="value"/> into the payload area and appends a parameter pointing at it.</summary>
    /// <returns>The payload's address inside the block; write through it to update the value later.</returns>
    public T* Add<T>(MpvRenderParamType type, T value) where T : unmanaged
    {
        EnsureSlot(type);
        byte* payload = Reserve(sizeof(T));
        *(T*)payload = value;
        Append(type, payload);
        return (T*)payload;
    }

    /// <summary>Copies a NUL-terminated UTF-8 string (e.g. <c>"opengl"u8</c>) into the block and appends it.</summary>
    public byte* AddString(MpvRenderParamType type, ReadOnlySpan<byte> utf8)
    {
        int length = utf8.IndexOf((byte)0);
        if (length < 0)
        {
            length = utf8.Length;
        }
        EnsureSlot(type);
        byte* payload = Reserve(length + 1);
        utf8[..length].CopyTo(new Span<byte>(payload, length));
        payload[length] = 0;
        Append(type, payload);
        return payload;
    }

    /// <summary>Appends a parameter whose payload lives elsewhere. The caller keeps it alive while the block is used.</summary>
    public void AddExternal(MpvRenderParamType type, void* data)
    {
        EnsureSlot(type);
        Append(type, data);
    }

    private byte* Reserve(int size)
    {
        byte* memory = Memory;
        int offset = AlignUp(_payloadUsed);
        if (size < 0 || offset + size > _payloadCapacity)
        {
            throw new InvalidOperationException($"Render parameter payload exceeds the {_payloadCapacity}-byte block.");
        }
        _payloadUsed = offset + size;
        return memory + _payloadOffset + offset;
    }

    /// <summary>Validates before any payload is reserved, so a rejected add leaves the block unchanged.</summary>
    private void EnsureSlot(MpvRenderParamType type)
    {
        _ = Memory; // ObjectDisposedException first
        if (type == MpvRenderParamType.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(type), "INVALID is the terminator.");
        }
        if (_count >= _paramCapacity)
        {
            throw new InvalidOperationException($"Render parameter block is full ({_paramCapacity}).");
        }
    }

    private void Append(MpvRenderParamType type, void* data)
    {
        MpvRenderParam* parameters = Parameters;
        parameters[_count].Type = type;
        parameters[_count].Data = (nint)data;
        _count++;
    }

    private static int AlignUp(int value) => (value + (Alignment - 1)) & ~(Alignment - 1);

    public void Dispose()
    {
        Free();
        GC.SuppressFinalize(this);
    }

    private void Free()
    {
        byte* memory = _memory;
        if (memory == null)
        {
            return;
        }
        _memory = null;
        NativeMemory.AlignedFree(memory);
        Interlocked.Decrement(ref _liveBlocks);
    }
}
