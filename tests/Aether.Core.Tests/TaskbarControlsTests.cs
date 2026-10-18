using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class TaskbarControlsTests
{
    private static readonly FolderNeighbours Both = new("prev.mp4", "next.mp4");

    [Fact]
    public void No_File_Disables_Every_Button()
    {
        TaskbarControls controls = TaskbarControls.From(PlaybackStatus.Idle, Both);

        Assert.False(controls.PreviousEnabled);
        Assert.False(controls.PlayPauseEnabled);
        Assert.False(controls.NextEnabled);
        Assert.Equal(PlayButtonAction.Play, controls.PlayPause);
    }

    [Theory]
    [InlineData(PlaybackStatus.Playing, PlayButtonAction.Pause)]
    [InlineData(PlaybackStatus.Paused, PlayButtonAction.Play)]
    [InlineData(PlaybackStatus.Ended, PlayButtonAction.Replay)]
    public void Middle_Button_Follows_Playback(PlaybackStatus status, PlayButtonAction expected)
    {
        TaskbarControls controls = TaskbarControls.From(status, default);

        Assert.Equal(expected, controls.PlayPause);
        Assert.True(controls.PlayPauseEnabled);
    }

    [Fact]
    public void Play_Pause_Waits_For_The_File_To_Load()
    {
        TaskbarControls controls = TaskbarControls.From(PlaybackStatus.Loading, Both);

        Assert.False(controls.PlayPauseEnabled);
        Assert.True(controls.PreviousEnabled);
        Assert.True(controls.NextEnabled);
    }

    [Fact]
    public void Previous_And_Next_Follow_The_Folder_Neighbours()
    {
        Assert.Equal(
            new TaskbarControls(false, PlayButtonAction.Pause, true, true),
            TaskbarControls.From(PlaybackStatus.Playing, new FolderNeighbours(null, "next.mp4")));
        Assert.Equal(
            new TaskbarControls(true, PlayButtonAction.Pause, true, false),
            TaskbarControls.From(PlaybackStatus.Playing, new FolderNeighbours("prev.mp4", null)));
        Assert.Equal(
            new TaskbarControls(false, PlayButtonAction.Pause, true, false),
            TaskbarControls.From(PlaybackStatus.Playing, default));
    }
}
