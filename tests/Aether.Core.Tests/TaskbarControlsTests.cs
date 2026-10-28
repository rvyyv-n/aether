using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class TaskbarControlsTests
{
    [Fact]
    public void No_File_Disables_Every_Button()
    {
        TaskbarControls controls = TaskbarControls.From(PlaybackStatus.Idle, hasPrevious: true, hasNext: true);

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
        TaskbarControls controls = TaskbarControls.From(status, hasPrevious: false, hasNext: false);

        Assert.Equal(expected, controls.PlayPause);
        Assert.True(controls.PlayPauseEnabled);
    }

    [Fact]
    public void Play_Pause_Waits_For_The_File_To_Load()
    {
        TaskbarControls controls = TaskbarControls.From(PlaybackStatus.Loading, hasPrevious: true, hasNext: true);

        Assert.False(controls.PlayPauseEnabled);
        Assert.True(controls.PreviousEnabled);
        Assert.True(controls.NextEnabled);
    }

    [Fact]
    public void Previous_And_Next_Follow_The_Queue()
    {
        Assert.Equal(
            new TaskbarControls(false, PlayButtonAction.Pause, true, true),
            TaskbarControls.From(PlaybackStatus.Playing, hasPrevious: false, hasNext: true));
        Assert.Equal(
            new TaskbarControls(true, PlayButtonAction.Pause, true, false),
            TaskbarControls.From(PlaybackStatus.Playing, hasPrevious: true, hasNext: false));
        Assert.Equal(
            new TaskbarControls(false, PlayButtonAction.Pause, true, false),
            TaskbarControls.From(PlaybackStatus.Playing, hasPrevious: false, hasNext: false));
    }

    [Fact]
    public void Title_Keeps_The_App_Name_Before_The_File_Name()
    {
        Assert.Equal("Aether - Holiday.mp4", TaskbarControls.Title("Aether", "Holiday.mp4"));
        Assert.Equal("Aether", TaskbarControls.Title("Aether", null));
        Assert.Equal("Aether", TaskbarControls.Title("Aether", ""));
    }
}
