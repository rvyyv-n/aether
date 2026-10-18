using Aether.Core.Playback.Chapters;
using Xunit;

namespace Aether.Core.Tests;

public class ChapterSkipPromptTests
{
    [Fact]
    public void No_Offer_Hides()
    {
        Assert.False(new ChapterSkipPrompt().Update(null, 10, controlsShown: true));
    }

    [Fact]
    public void Shows_For_The_First_Seconds_Of_The_Chapter_Then_Hides()
    {
        var prompt = new ChapterSkipPrompt();

        Assert.True(prompt.Update(60, 30, controlsShown: false));
        Assert.True(prompt.Update(60, 37.9, controlsShown: false));
        Assert.False(prompt.Update(60, 38, controlsShown: false));
    }

    [Fact]
    public void Controls_Bring_It_Back_After_The_Prompt_Time()
    {
        var prompt = new ChapterSkipPrompt();
        prompt.Update(60, 30, controlsShown: false);

        Assert.False(prompt.Update(60, 45, controlsShown: false));
        Assert.True(prompt.Update(60, 46, controlsShown: true));
    }

    [Fact]
    public void Seeking_Back_Within_The_Chapter_Starts_The_Prompt_Again()
    {
        var prompt = new ChapterSkipPrompt();
        prompt.Update(60, 30, controlsShown: false);
        Assert.False(prompt.Update(60, 50, controlsShown: false));

        Assert.True(prompt.Update(60, 32, controlsShown: false));
    }

    [Fact]
    public void A_New_Chapter_To_Skip_Starts_Its_Own_Prompt()
    {
        var prompt = new ChapterSkipPrompt();
        prompt.Update(60, 30, controlsShown: false);
        prompt.Update(60, 50, controlsShown: false);

        Assert.True(prompt.Update(120, 100, controlsShown: false));
    }

    [Fact]
    public void Used_Hides_Until_Playback_Leaves_The_Chapter()
    {
        var prompt = new ChapterSkipPrompt();
        prompt.Update(60, 30, controlsShown: true);
        prompt.Used();

        Assert.False(prompt.Update(60, 31, controlsShown: true)); // a position update from before the seek landed
        Assert.False(prompt.Update(null, 60, controlsShown: true));
        Assert.True(prompt.Update(60, 40, controlsShown: false)); // the user came back into it
    }
}
