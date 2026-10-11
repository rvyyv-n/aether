using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class LoopRestartCycleTests
{
    [Fact]
    public void ShouldRestart_SuppressesRapidFlushesWhileAtEnd()
    {
        var loop = new LoopRestart();

        // First transition onto end with looping active triggers restart
        Assert.True(loop.ShouldRestart(isAtEnd: true, isLooping: true));

        // Rapid subsequent flushes while state remains at end must not trigger additional restarts
        for (int i = 0; i < 50; i++)
        {
            Assert.False(loop.ShouldRestart(isAtEnd: true, isLooping: true));
        }
    }

    [Fact]
    public void ShouldRestart_ResetsWhenPlaybackPositionMovesAwayFromEnd()
    {
        var loop = new LoopRestart();

        // 1. Reached end -> restarts once
        Assert.True(loop.ShouldRestart(isAtEnd: true, isLooping: true));
        Assert.False(loop.ShouldRestart(isAtEnd: true, isLooping: true));

        // 2. Playback position moves back (e.g. replay seek landed on frame 0)
        Assert.False(loop.ShouldRestart(isAtEnd: false, isLooping: true));

        // 3. Normal playback proceeds
        for (int i = 0; i < 10; i++)
        {
            Assert.False(loop.ShouldRestart(isAtEnd: false, isLooping: true));
        }

        // 4. File reaches end again -> triggers restart again
        Assert.True(loop.ShouldRestart(isAtEnd: true, isLooping: true));
    }

    [Fact]
    public void ShouldRestart_TogglingLoopOffAndOnAtEnd_WaitsForNextEdge()
    {
        var loop = new LoopRestart();

        // Reached end while loop was off -> no restart
        Assert.False(loop.ShouldRestart(isAtEnd: true, isLooping: false));

        // Toggling loop on while still parked at end does not restart immediately
        Assert.False(loop.ShouldRestart(isAtEnd: true, isLooping: true));

        // Moving away from end and reaching end again enables restart
        Assert.False(loop.ShouldRestart(isAtEnd: false, isLooping: true));
        Assert.True(loop.ShouldRestart(isAtEnd: true, isLooping: true));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ShouldRestart_InitialStateEvaluatesEdge(bool isAtEnd, bool isLooping, bool expected)
    {
        var loop = new LoopRestart();
        Assert.Equal(expected, loop.ShouldRestart(isAtEnd, isLooping));
    }
}
