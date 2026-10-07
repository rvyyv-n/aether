using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aether.Core.Interop.Mpv;
using Xunit;

namespace Aether.Core.Tests;

[CollectionDefinition("MpvRenderParamBlock.LiveBlocks", DisableParallelization = true)]
public class MpvRenderParamBlockLiveBlocksCollection
{
}

[Collection("MpvRenderParamBlock.LiveBlocks")]
public unsafe class MpvRenderParamBlockTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(64, 65536)]
    [InlineData(2, 64)]
    [InlineData(16, 1024)]
    public void Constructor_ValidCapacities_InitializesCorrectly(int paramCapacity, int payloadCapacity)
    {
        using var block = new MpvRenderParamBlock(paramCapacity, payloadCapacity);

        Assert.False(block.IsDisposed);
        Assert.Equal(0, block.Count);
        Assert.Equal(paramCapacity, block.ParamCapacity);
        Assert.Equal(payloadCapacity, block.PayloadCapacity);
        Assert.Equal(0, block.PayloadUsed);
        Assert.True((nint)block.Parameters != 0);
        Assert.Equal(MpvRenderParamType.Invalid, block.Parameters[0].Type);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(65)]
    [InlineData(100)]
    public void Constructor_InvalidParamCapacity_ThrowsArgumentOutOfRangeException(int paramCapacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MpvRenderParamBlock(paramCapacity, 64));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-64)]
    [InlineData(65537)]
    [InlineData(100000)]
    public void Constructor_InvalidPayloadCapacity_ThrowsArgumentOutOfRangeException(int payloadCapacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MpvRenderParamBlock(2, payloadCapacity));
    }

    [Fact]
    public void LiveBlocks_TracksAllocatedAndFreedBlocks_AndDoubleDisposeDecrementsOnce()
    {
        long baseline = MpvRenderParamBlock.LiveBlocks;

        var block1 = new MpvRenderParamBlock(2, 64);
        Assert.Equal(baseline + 1, MpvRenderParamBlock.LiveBlocks);

        var block2 = new MpvRenderParamBlock(2, 64);
        Assert.Equal(baseline + 2, MpvRenderParamBlock.LiveBlocks);

        block1.Dispose();
        Assert.Equal(baseline + 1, MpvRenderParamBlock.LiveBlocks);
        Assert.True(block1.IsDisposed);

        // Double dispose is idempotent and decrements only once
        block1.Dispose();
        Assert.Equal(baseline + 1, MpvRenderParamBlock.LiveBlocks);
        Assert.True(block1.IsDisposed);

        block2.Dispose();
        Assert.Equal(baseline, MpvRenderParamBlock.LiveBlocks);
        Assert.True(block2.IsDisposed);
    }

    [Fact]
    public void Finalizer_ReleasesUnmanagedMemory_AndDecrementsLiveBlocks()
    {
        long baseline = MpvRenderParamBlock.LiveBlocks;

        AllocateBlockWithoutDisposing();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(baseline, MpvRenderParamBlock.LiveBlocks);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateBlockWithoutDisposing()
    {
        _ = new MpvRenderParamBlock(2, 64);
    }

    [Fact]
    public void Dispose_SetsIsDisposed_AndParametersThrowsObjectDisposedException()
    {
        var block = new MpvRenderParamBlock(2, 64);
        Assert.False(block.IsDisposed);
        Assert.True((nint)block.Parameters != 0);

        block.Dispose();

        Assert.True(block.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = (nint)block.Parameters);

        // Double dispose remains disposed
        block.Dispose();
        Assert.True(block.IsDisposed);
    }

    [Fact]
    public void Add_AfterDispose_ThrowsObjectDisposedException()
    {
        var block = new MpvRenderParamBlock(2, 64);
        block.Dispose();

        Assert.Throws<ObjectDisposedException>(() => block.Add(MpvRenderParamType.FlipY, 1));
        Assert.Throws<ObjectDisposedException>(() => block.AddString(MpvRenderParamType.ApiType, "opengl"u8));
        Assert.Throws<ObjectDisposedException>(() => block.AddExternal(MpvRenderParamType.AdvancedControl, (void*)0x1234));
    }

    [Fact]
    public void Add_StoresValue_Aligned16_AndIsVisibleViaParameters()
    {
        using var block = new MpvRenderParamBlock(4, 128);
        var fbo = new MpvOpenGLFbo
        {
            Fbo = 3,
            Width = 1920,
            Height = 1080,
            InternalFormat = 0x8058,
        };

        MpvOpenGLFbo* ptr = block.Add(MpvRenderParamType.OpenGLFbo, fbo);

        Assert.Equal(1, block.Count);
        Assert.Equal(MpvRenderParamType.OpenGLFbo, block.Parameters[0].Type);
        Assert.Equal((nint)ptr, block.Parameters[0].Data);
        Assert.Equal(MpvRenderParamType.Invalid, block.Parameters[1].Type);

        // Pointer is 16-byte aligned
        Assert.Equal(0, (long)(nuint)ptr % 16);

        // Payload pointer lies inside the block payload area
        byte* blockStart = (byte*)block.Parameters;
        int minPayloadOffset = (block.ParamCapacity + 1) * sizeof(MpvRenderParam);
        Assert.True((byte*)ptr >= blockStart + minPayloadOffset);
        Assert.True((byte*)ptr + sizeof(MpvOpenGLFbo) <= blockStart + minPayloadOffset + block.PayloadCapacity);

        // Value is readable via Parameters[0].Data
        MpvOpenGLFbo* fromData = (MpvOpenGLFbo*)block.Parameters[0].Data;
        Assert.Equal(3, fromData->Fbo);
        Assert.Equal(1920, fromData->Width);
        Assert.Equal(1080, fromData->Height);
        Assert.Equal(0x8058, fromData->InternalFormat);

        // In-place rewrite through returned pointer is visible via Data
        ptr->Width = 3840;
        ptr->Height = 2160;
        Assert.Equal(3840, fromData->Width);
        Assert.Equal(2160, fromData->Height);
    }

    [Fact]
    public void AddString_CopiesAndAppendsNulTerminatedString()
    {
        using var block = new MpvRenderParamBlock(2, 64);
        byte* ptr = block.AddString(MpvRenderParamType.ApiType, "opengl"u8);

        Assert.Equal(1, block.Count);
        Assert.Equal(MpvRenderParamType.ApiType, block.Parameters[0].Type);
        Assert.Equal((nint)ptr, block.Parameters[0].Data);
        Assert.Equal(MpvRenderParamType.Invalid, block.Parameters[1].Type);

        // 16-byte aligned
        Assert.Equal(0, (long)(nuint)ptr % 16);

        // String is NUL-terminated and equals "opengl"
        string? result = Marshal.PtrToStringUTF8((nint)ptr);
        Assert.Equal("opengl", result);
        Assert.Equal((byte)'o', ptr[0]);
        Assert.Equal((byte)'p', ptr[1]);
        Assert.Equal((byte)'e', ptr[2]);
        Assert.Equal((byte)'n', ptr[3]);
        Assert.Equal((byte)'g', ptr[4]);
        Assert.Equal((byte)'l', ptr[5]);
        Assert.Equal(0, ptr[6]);
    }

    [Fact]
    public void AddString_SpanWithEmbeddedNul_TruncatesAtNul()
    {
        using var block = new MpvRenderParamBlock(2, 64);
        ReadOnlySpan<byte> spanWithNul = [(byte)'o', (byte)'p', (byte)'e', (byte)'n', 0, (byte)'g', (byte)'l'];
        byte* ptr = block.AddString(MpvRenderParamType.ApiType, spanWithNul);

        Assert.Equal("open", Marshal.PtrToStringUTF8((nint)ptr));
        Assert.Equal(0, ptr[4]);
        Assert.Equal(MpvRenderParamType.ApiType, block.Parameters[0].Type);
        Assert.Equal(MpvRenderParamType.Invalid, block.Parameters[1].Type);
    }

    [Fact]
    public void AddExternal_AppendsParameterWithoutAffectingPayload()
    {
        using var block = new MpvRenderParamBlock(2, 64);
        int payloadUsedBefore = block.PayloadUsed;
        void* externalPtr = (void*)0xDEADBEEF;

        block.AddExternal(MpvRenderParamType.AdvancedControl, externalPtr);

        Assert.Equal(1, block.Count);
        Assert.Equal(payloadUsedBefore, block.PayloadUsed);
        Assert.Equal(MpvRenderParamType.AdvancedControl, block.Parameters[0].Type);
        Assert.Equal((nint)externalPtr, block.Parameters[0].Data);
        Assert.Equal(MpvRenderParamType.Invalid, block.Parameters[1].Type);
    }

    [Fact]
    public void Add_MultipleElements_MaintainsInvalidTerminatorAndPayloadOffsets()
    {
        using var block = new MpvRenderParamBlock(3, 128);

        byte* s = block.AddString(MpvRenderParamType.ApiType, "opengl"u8);
        MpvOpenGLFbo* fbo = block.Add(MpvRenderParamType.OpenGLFbo, new MpvOpenGLFbo { Fbo = 1, Width = 1280, Height = 720 });
        int* flip = block.Add(MpvRenderParamType.FlipY, 1);

        Assert.Equal(3, block.Count);
        Assert.Equal(MpvRenderParamType.ApiType, block.Parameters[0].Type);
        Assert.Equal((nint)s, block.Parameters[0].Data);
        Assert.Equal(MpvRenderParamType.OpenGLFbo, block.Parameters[1].Type);
        Assert.Equal((nint)fbo, block.Parameters[1].Data);
        Assert.Equal(MpvRenderParamType.FlipY, block.Parameters[2].Type);
        Assert.Equal((nint)flip, block.Parameters[2].Data);
        Assert.Equal(MpvRenderParamType.Invalid, block.Parameters[3].Type);

        Assert.Equal("opengl", Marshal.PtrToStringUTF8((nint)s));
        Assert.Equal(1280, fbo->Width);
        Assert.Equal(1, *flip);
    }

    [Fact]
    public void Add_TypeInvalid_ThrowsArgumentOutOfRangeException()
    {
        using var block = new MpvRenderParamBlock(2, 64);

        Assert.Throws<ArgumentOutOfRangeException>(() => block.Add(MpvRenderParamType.Invalid, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => block.AddString(MpvRenderParamType.Invalid, "opengl"u8));
        Assert.Throws<ArgumentOutOfRangeException>(() => block.AddExternal(MpvRenderParamType.Invalid, (void*)0x1));
    }

    [Fact]
    public void RejectedAdd_LeavesPayloadAndCountUnchanged()
    {
        using var block = new MpvRenderParamBlock(1, 64);
        Assert.Throws<ArgumentOutOfRangeException>(() => block.Add(MpvRenderParamType.Invalid, 7L));
        Assert.Equal(0, block.PayloadUsed);
        Assert.Equal(0, block.Count);

        block.Add(MpvRenderParamType.FlipY, 1);
        int used = block.PayloadUsed;
        Assert.Throws<InvalidOperationException>(() => block.AddString(MpvRenderParamType.ApiType, "opengl"u8));
        Assert.Equal(used, block.PayloadUsed);
        Assert.Equal(1, block.Count);
    }

    [Fact]
    public void Add_ExceedingParamCapacity_ThrowsInvalidOperationException()
    {
        using var block = new MpvRenderParamBlock(1, 64);
        block.Add(MpvRenderParamType.FlipY, 1);

        Assert.Throws<InvalidOperationException>(() => block.Add(MpvRenderParamType.FlipY, 2));
    }

    [Fact]
    public void AddExternal_ExceedingParamCapacity_ThrowsInvalidOperationException()
    {
        using var block = new MpvRenderParamBlock(1, 64);
        block.AddExternal(MpvRenderParamType.AdvancedControl, (void*)0x1);

        Assert.Throws<InvalidOperationException>(() => block.AddExternal(MpvRenderParamType.AdvancedControl, (void*)0x2));
    }

    [Fact]
    public void Add_ExceedingPayloadCapacity_ThrowsInvalidOperationException()
    {
        // 8 bytes capacity cannot hold 16-byte MpvOpenGLFbo
        using var block = new MpvRenderParamBlock(2, 8);
        Assert.Throws<InvalidOperationException>(() => block.Add(MpvRenderParamType.OpenGLFbo, default(MpvOpenGLFbo)));
    }

    [Fact]
    public void AddString_ExceedingPayloadCapacity_ThrowsInvalidOperationException()
    {
        // "opengl" requires 7 bytes with NUL terminator, exceeding 4 bytes capacity
        using var block = new MpvRenderParamBlock(2, 4);
        Assert.Throws<InvalidOperationException>(() => block.AddString(MpvRenderParamType.ApiType, "opengl"u8));
    }

    [Fact]
    public void ZeroAllocation_RewritingPayloadsInPlace()
    {
        using var block = new MpvRenderParamBlock(paramCapacity: 2, payloadCapacity: 64);
        MpvOpenGLFbo* fbo = block.Add(MpvRenderParamType.OpenGLFbo, default(MpvOpenGLFbo));
        int* flipY = block.Add(MpvRenderParamType.FlipY, 0);

        // Warm up to eliminate any one-time JIT or static initialization allocations
        for (int i = 0; i < 20; i++)
        {
            fbo->Fbo = i;
            fbo->Width = 1920;
            fbo->Height = 1080;
            fbo->InternalFormat = 0;
            *flipY = i & 1;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            fbo->Fbo = i;
            fbo->Width = 1920 + (i & 1);
            fbo->Height = 1080;
            fbo->InternalFormat = 0;
            *flipY = i & 1;
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
