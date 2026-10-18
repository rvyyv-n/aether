using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aether.Core.Interop;
using Aether.Core.Interop.Mpv;
using Xunit;

namespace Aether.Core.Tests;

public class MpvContextTests
{
    [Fact]
    public void MpvContextOptions_Defaults_MatchExpected()
    {
        var options = new MpvContextOptions();

        Assert.Equal("d3d11va", options.HardwareDecoder);
        Assert.Equal("512MiB", options.BackBufferSize);
        Assert.True(options.VideoLatencyHacks);
        Assert.Empty(options.AdditionalOptions);
    }

    [Fact]
    public void BuildOptions_DefaultOptions_ProducesExpectedEntriesInOrder()
    {
        IReadOnlyList<KeyValuePair<string, string>> options = MpvContext.BuildOptions();

        Assert.Equal(12, options.Count);
        Assert.Equal(new KeyValuePair<string, string>("vo", "libmpv"), options[0]);
        Assert.Equal(new KeyValuePair<string, string>("hwdec", "d3d11va"), options[1]);
        Assert.Equal(new KeyValuePair<string, string>("demuxer-max-back-bytes", "512MiB"), options[2]);
        Assert.Equal(new KeyValuePair<string, string>("video-latency-hacks", "yes"), options[3]);
        Assert.Equal(new KeyValuePair<string, string>("config", "no"), options[4]);
        Assert.Equal(new KeyValuePair<string, string>("terminal", "no"), options[5]);
        Assert.Equal(new KeyValuePair<string, string>("input-default-bindings", "no"), options[6]);
        Assert.Equal(new KeyValuePair<string, string>("input-vo-keyboard", "no"), options[7]);
        Assert.Equal(new KeyValuePair<string, string>("osc", "no"), options[8]);
        Assert.Equal(new KeyValuePair<string, string>("idle", "yes"), options[9]);
        Assert.Equal(new KeyValuePair<string, string>("keep-open", "yes"), options[10]);
        Assert.Equal(new KeyValuePair<string, string>("keepaspect", "no"), options[11]);
    }

    [Fact]
    public void BuildOptions_NullOptions_ProducesDefaults()
    {
        IReadOnlyList<KeyValuePair<string, string>> defaultBuild = MpvContext.BuildOptions();
        IReadOnlyList<KeyValuePair<string, string>> nullBuild = MpvContext.BuildOptions(null);

        Assert.Equal(defaultBuild.Count, nullBuild.Count);
        for (int i = 0; i < defaultBuild.Count; i++)
        {
            Assert.Equal(defaultBuild[i].Key, nullBuild[i].Key);
            Assert.Equal(defaultBuild[i].Value, nullBuild[i].Value);
        }
    }

    [Fact]
    public void BuildOptions_CustomOptions_ReflectsProperties()
    {
        var options = new MpvContextOptions
        {
            HardwareDecoder = "nvdec",
            BackBufferSize = "1024MiB",
            VideoLatencyHacks = false,
        };

        IReadOnlyList<KeyValuePair<string, string>> list = MpvContext.BuildOptions(options);

        Assert.Equal("nvdec", list[1].Value);
        Assert.Equal("1024MiB", list[2].Value);
        Assert.Equal("no", list[3].Value);
    }

    [Fact]
    public void BuildOptions_AdditionalOptions_AppendedInOrder()
    {
        var options = new MpvContextOptions
        {
            AdditionalOptions =
            [
                new KeyValuePair<string, string>("gpu-api", "d3d11"),
                new KeyValuePair<string, string>("keep-open", "yes"),
            ],
        };

        IReadOnlyList<KeyValuePair<string, string>> list = MpvContext.BuildOptions(options);

        Assert.Equal(14, list.Count);
        Assert.Equal(new KeyValuePair<string, string>("gpu-api", "d3d11"), list[12]);
        Assert.Equal(new KeyValuePair<string, string>("keep-open", "yes"), list[13]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BuildOptions_NullOrEmptyKeyInAdditionalOptions_ThrowsArgumentException(string? invalidKey)
    {
        var options = new MpvContextOptions
        {
            AdditionalOptions =
            [
                new KeyValuePair<string, string>(invalidKey!, "some-value"),
            ],
        };

        Assert.ThrowsAny<ArgumentException>(() => MpvContext.BuildOptions(options));
    }

    [Fact]
    public void BuildOptions_NullValueInAdditionalOptions_ThrowsArgumentNullException()
    {
        var options = new MpvContextOptions
        {
            AdditionalOptions = [new KeyValuePair<string, string>("cache", null!)],
        };

        Assert.Throws<ArgumentNullException>(() => MpvContext.BuildOptions(options));
    }

    [Fact]
    public void Create_WhenMpvUnavailable_ThrowsDllNotFoundException()
    {
        if (NativeLibraries.IsMpvAvailable())
        {
            return; // libmpv is available, tested in Create_WhenMpvAvailable
        }

        Assert.Throws<DllNotFoundException>(() => MpvContext.Create());
    }

    [Fact]
    public void Create_WhenMpvAvailable_InitializesContext_AndDisposes()
    {
        if (!NativeLibraries.IsMpvAvailable())
        {
            return; // libmpv is not installed on this machine
        }

        using var ctx = MpvContext.Create();

        Assert.False(ctx.IsDisposed);
        Assert.False(ctx.Handle.IsInvalid);
        Assert.False(ctx.Handle.IsClosed);
        Assert.Equal(MpvContext.BuildOptions(), ctx.AppliedOptions);
        Assert.Equal("d3d11va", ctx.GetPropertyString("hwdec"));

        ctx.Dispose();

        Assert.True(ctx.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = ctx.Handle);

        // Double dispose is safe
        ctx.Dispose();
        Assert.True(ctx.IsDisposed);
    }

    [Fact]
    public void Create_WhenMpvAvailable_WithInvalidOption_ThrowsMpvException()
    {
        if (!NativeLibraries.IsMpvAvailable())
        {
            return; // libmpv is not installed on this machine
        }

        var options = new MpvContextOptions
        {
            AdditionalOptions =
            [
                new KeyValuePair<string, string>("no-such-option", "x"),
            ],
        };

        var ex = Assert.Throws<MpvException>(() => MpvContext.Create(options));
        Assert.Equal(MpvError.OptionNotFound, ex.Error);
    }

    [Fact]
    public void Command_InvalidArguments_ThrowsExpectedExceptions()
    {
        if (!NativeLibraries.IsMpvAvailable())
        {
            return; // libmpv is not installed on this machine
        }

        using var ctx = MpvContext.Create();

        Assert.Throws<ArgumentNullException>(() => ctx.Command(null!));
        Assert.Throws<ArgumentException>(() => ctx.Command());
        Assert.Throws<ArgumentNullException>(() => ctx.Command("loadfile", null!));
    }

    [Fact]
    public void SetProperty_And_GetPropertyString_Lifecycle()
    {
        if (!NativeLibraries.IsMpvAvailable())
        {
            return; // libmpv is not installed on this machine
        }

        using var ctx = MpvContext.Create();

        ctx.SetProperty("volume", "50");
        string? volume = ctx.GetPropertyString("volume");
        Assert.NotNull(volume);
        Assert.StartsWith("50", volume);

        Assert.Null(ctx.GetPropertyString("nonexistent-property-name-xyz"));
    }

    [Fact]
    public void MpvException_ThrowIfError_NonNegative_ReturnsCode()
    {
        Assert.Equal(0, MpvException.ThrowIfError(0, "success_op"));
        Assert.Equal(42, MpvException.ThrowIfError(42, "positive_code"));
    }

    [Theory]
    [InlineData(-19, MpvError.NotImplemented, "NotImplemented")]
    [InlineData(-20, MpvError.Generic, "Generic")]
    [InlineData(-5, MpvError.OptionNotFound, "OptionNotFound")]
    [InlineData(-4, MpvError.InvalidParameter, "InvalidParameter")]
    public void MpvException_ThrowIfError_Negative_ThrowsMpvException_WithFallbackDescription(
        int errorCode, MpvError expectedError, string expectedEnumName)
    {
        var ex = Assert.Throws<MpvException>(() => MpvException.ThrowIfError(errorCode, "test_operation"));

        Assert.Equal(errorCode, ex.ErrorCode);
        Assert.Equal(expectedError, ex.Error);
        Assert.Contains("test_operation", ex.Message);
        Assert.Contains(errorCode.ToString(), ex.Message);
        Assert.Contains(expectedEnumName, ex.Message);
    }

    [Fact]
    public unsafe void CreateOpenGL_NullMpv_ThrowsArgumentNullException()
    {
        delegate* unmanaged[Cdecl]<void*, byte*, void*> getProcAddress = &DummyGetProcAddress;

        Assert.Throws<ArgumentNullException>(() => MpvRenderContext.CreateOpenGL(null!, getProcAddress, null, 0));
    }

    [Fact]
    public unsafe void CreateOpenGL_NullGetProcAddress_ThrowsArgumentNullException()
    {
        if (!NativeLibraries.IsMpvAvailable())
        {
            return; // libmpv is not installed on this machine
        }

        using var ctx = MpvContext.Create();
        Assert.Throws<ArgumentNullException>(() => MpvRenderContext.CreateOpenGL(ctx, null, null, 0));
    }

    [Fact]
    public void Create_NullArguments_ThrowsArgumentNullException()
    {
        using var block = new MpvRenderParamBlock(2, 64);

        Assert.Throws<ArgumentNullException>(() => MpvRenderContext.Create(null!, block, "opengl", 0));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void* DummyGetProcAddress(void* ctx, byte* name) => null;
}
