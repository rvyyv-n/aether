using System;
using System.Runtime.InteropServices;
using System.Text;
using Aether.Core.Interop.Mpv;
using Xunit;

namespace Aether.Core.Tests;

public unsafe class MpvCommandTests
{
    [Fact]
    public void FrameStep_And_FrameBackStep_StaticInstances_HaveCorrectNamesAndArguments()
    {
        Assert.Equal("frame-step", MpvCommand.FrameStep.Name);
        Assert.Single(MpvCommand.FrameStep.Arguments);
        Assert.Equal("frame-step", MpvCommand.FrameStep.Arguments[0]);
        Assert.Equal("frame-step", MpvCommand.FrameStep.ToString());

        Assert.Equal("frame-back-step", MpvCommand.FrameBackStep.Name);
        Assert.Single(MpvCommand.FrameBackStep.Arguments);
        Assert.Equal("frame-back-step", MpvCommand.FrameBackStep.Arguments[0]);
        Assert.Equal("frame-back-step", MpvCommand.FrameBackStep.ToString());
    }

    [Fact]
    public void Constructor_NullArray_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MpvCommand(null!));
    }

    [Fact]
    public void Constructor_EmptyArgs_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new MpvCommand());
        Assert.Throws<ArgumentException>(() => new MpvCommand(Array.Empty<string>()));
    }

    [Fact]
    public void Constructor_NullElement_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MpvCommand("set", null!, "yes"));
        Assert.Throws<ArgumentNullException>(() => new MpvCommand(null!, "pause"));
    }

    [Fact]
    public void Constructor_ArgContainingNul_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new MpvCommand("set\0pause", "yes"));
        Assert.Throws<ArgumentException>(() => new MpvCommand("set", "pause\0"));
        Assert.Throws<ArgumentException>(() => new MpvCommand("\0"));
    }

    [Fact]
    public void Arguments_And_ToString_ReturnExpectedValues()
    {
        var cmd = new MpvCommand("set", "pause", "yes");

        Assert.Equal("set", cmd.Name);
        Assert.Equal(3, cmd.Arguments.Count);
        Assert.Equal("set", cmd.Arguments[0]);
        Assert.Equal("pause", cmd.Arguments[1]);
        Assert.Equal("yes", cmd.Arguments[2]);
        Assert.Equal("set pause yes", cmd.ToString());
    }

    [Fact]
    public void Argv_Layout_NullTerminatedPointerTable_And_NullTerminatedUtf8_IncludingNonAscii()
    {
        string[] args = ["loadfile", "c:\\media\\日本語_файл_🎬.mkv", "replace", "title=Aether ▶"];
        var cmd = new MpvCommand(args);

        byte** argv = cmd.Argv;
        Assert.True(argv != null);

        for (int i = 0; i < args.Length; i++)
        {
            byte* argPtr = argv[i];
            Assert.True(argPtr != null);

            ReadOnlySpan<byte> span = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(argPtr);
            string decoded = Encoding.UTF8.GetString(span);
            Assert.Equal(args[i], decoded);
        }

        Assert.True(argv[args.Length] == null);
    }
}
