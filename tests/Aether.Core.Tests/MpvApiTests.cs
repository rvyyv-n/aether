using System;
using System.Runtime.InteropServices;
using Aether.Core.Interop.Mpv;
using Xunit;

namespace Aether.Core.Tests;

public class MpvApiTests
{
    [Fact]
    public unsafe void MpvRenderParam_StructLayout_MatchesAbi()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<MpvRenderParam>(nameof(MpvRenderParam.Type)));
        if (IntPtr.Size == 8)
        {
            Assert.Equal(16, sizeof(MpvRenderParam));
            Assert.Equal(8, (int)Marshal.OffsetOf<MpvRenderParam>(nameof(MpvRenderParam.Data)));
        }
    }

    [Fact]
    public unsafe void MpvOpenGLFbo_StructLayout_MatchesAbi()
    {
        Assert.Equal(16, sizeof(MpvOpenGLFbo));
        Assert.Equal(0, (int)Marshal.OffsetOf<MpvOpenGLFbo>(nameof(MpvOpenGLFbo.Fbo)));
        Assert.Equal(4, (int)Marshal.OffsetOf<MpvOpenGLFbo>(nameof(MpvOpenGLFbo.Width)));
        Assert.Equal(8, (int)Marshal.OffsetOf<MpvOpenGLFbo>(nameof(MpvOpenGLFbo.Height)));
        Assert.Equal(12, (int)Marshal.OffsetOf<MpvOpenGLFbo>(nameof(MpvOpenGLFbo.InternalFormat)));
    }

    [Fact]
    public unsafe void MpvOpenGLInitParams_StructLayout_MatchesAbi()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<MpvOpenGLInitParams>(nameof(MpvOpenGLInitParams.GetProcAddress)));
        if (IntPtr.Size == 8)
        {
            Assert.Equal(16, sizeof(MpvOpenGLInitParams));
            Assert.Equal(8, (int)Marshal.OffsetOf<MpvOpenGLInitParams>(nameof(MpvOpenGLInitParams.GetProcAddressContext)));
        }
    }

    [Fact]
    public unsafe void MpvEvent_StructLayout_MatchesAbi()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<MpvEvent>(nameof(MpvEvent.EventId)));
        Assert.Equal(4, (int)Marshal.OffsetOf<MpvEvent>(nameof(MpvEvent.Error)));
        Assert.Equal(8, (int)Marshal.OffsetOf<MpvEvent>(nameof(MpvEvent.ReplyUserdata)));
        if (IntPtr.Size == 8)
        {
            Assert.Equal(24, sizeof(MpvEvent));
            Assert.Equal(16, (int)Marshal.OffsetOf<MpvEvent>(nameof(MpvEvent.Data)));
        }
    }

    [Fact]
    public void MpvRenderParamType_EnumValues_MatchExpectedValues()
    {
        Assert.Equal(0, (int)MpvRenderParamType.Invalid);
        Assert.Equal(1, (int)MpvRenderParamType.ApiType);
        Assert.Equal(2, (int)MpvRenderParamType.OpenGLInitParams);
        Assert.Equal(3, (int)MpvRenderParamType.OpenGLFbo);
        Assert.Equal(4, (int)MpvRenderParamType.FlipY);
        Assert.Equal(10, (int)MpvRenderParamType.AdvancedControl);
        Assert.Equal(12, (int)MpvRenderParamType.BlockForTargetTime);
        Assert.Equal(20, (int)MpvRenderParamType.SwPointer);
    }

    [Fact]
    public void MpvError_EnumValues_MatchExpectedValues()
    {
        Assert.Equal(0, (int)MpvError.Success);
        Assert.Equal(-19, (int)MpvError.NotImplemented);
        Assert.Equal(-20, (int)MpvError.Generic);
    }

    [Fact]
    public void MpvRenderUpdateFlags_EnumValues_MatchExpectedValues()
    {
        Assert.Equal(1uL, (ulong)MpvRenderUpdateFlags.Frame);
    }
}
