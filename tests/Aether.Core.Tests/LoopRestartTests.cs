using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class LoopRestartTests
{
    [Fact]
    public void Restarts_Once_When_The_File_Ends_While_Looping()
    {
        var loop = new LoopRestart();

        Assert.False(loop.ShouldRestart(isAtEnd: false, isLooping: true));
        Assert.True(loop.ShouldRestart(isAtEnd: true, isLooping: true));
        Assert.False(loop.ShouldRestart(isAtEnd: true, isLooping: true));
    }

    [Fact]
    public void Restarts_Again_After_The_Next_End()
    {
        var loop = new LoopRestart();

        Assert.True(loop.ShouldRestart(true, true));
        Assert.False(loop.ShouldRestart(false, true));
        Assert.True(loop.ShouldRestart(true, true));
    }

    [Fact]
    public void Does_Not_Restart_When_Not_Looping()
    {
        var loop = new LoopRestart();

        Assert.False(loop.ShouldRestart(true, false));
    }

    [Fact]
    public void Turning_Loop_On_At_The_End_Waits_For_The_Next_End()
    {
        var loop = new LoopRestart();

        Assert.False(loop.ShouldRestart(true, false));
        Assert.False(loop.ShouldRestart(true, true));
    }

    [Fact]
    public void Restart_Uses_The_Replay_Commands()
    {
        Assert.Equal(["seek 0 absolute", "set pause no"], PlayToggleCommands.For(isAtEnd: true).Select(c => c.ToString()));
    }

    [Fact]
    public void ViewModel_ToggleLoop_Raises_Property_Changed()
    {
        var vm = new PlayerViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.ToggleLoop();
        vm.IsLooping = true;
        vm.ToggleLoop();

        Assert.Equal([nameof(PlayerViewModel.IsLooping), nameof(PlayerViewModel.IsLooping)], raised);
        Assert.False(vm.IsLooping);
    }
}
