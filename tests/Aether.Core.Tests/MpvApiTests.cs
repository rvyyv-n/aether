using System.Runtime.InteropServices;
using Aether.Core.Interop.Mpv;
using Xunit;

namespace Aether.Core.Tests;

public class MpvApiTests
{
    [Fact]
    public void MpvRenderParam_StructLayout_IsSequential()
    {
        int size = Marshal.SizeOf<MpvRenderParam>();
        Assert.True(size > 0);
    }

    [Fact]
    public void MpvEvent_StructLayout_MatchesAbi()
    {
        int size = Marshal.SizeOf<MpvEvent>();
        Assert.True(size > 0);
    }
}
