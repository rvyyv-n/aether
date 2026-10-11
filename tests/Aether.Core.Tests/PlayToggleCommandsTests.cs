using System.Linq;
using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class PlayToggleCommandsTests
{
    [Fact]
    public void Mid_File_Cycles_Pause()
    {
        var commands = PlayToggleCommands.For(isAtEnd: false);

        Assert.Equal(["cycle pause"], commands.Select(c => c.ToString()));
    }

    [Fact]
    public void At_End_Seeks_To_Start_Then_Unpauses()
    {
        var commands = PlayToggleCommands.For(isAtEnd: true);

        Assert.Equal(["seek 0 absolute", "set pause no"], commands.Select(c => c.ToString()));
    }
}
