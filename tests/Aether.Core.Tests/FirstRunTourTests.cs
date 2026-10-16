using Aether.Core.Playback;
using Aether.Core.Playback.Input;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Tour;
using Xunit;

namespace Aether.Core.Tests;

public class FirstRunTourTests
{
    [Fact]
    public void Steps_RunInTheSpecOrder()
    {
        Assert.Equal(
            [TourStep.Open, TourStep.Controls, TourStep.Rail, TourStep.Menu, TourStep.Shortcuts, TourStep.Settings],
            FirstRunTour.Steps);
    }

    [Fact]
    public void Start_BeginsAtTheFirstStep_AndOnlyOnce()
    {
        var tour = new FirstRunTour();
        Assert.False(tour.IsActive);

        Assert.True(tour.Start());
        Assert.True(tour.IsActive);
        Assert.Equal(TourStep.Open, tour.Current);
        Assert.True(tour.IsFirst);

        tour.Next();
        Assert.False(tour.Start());
        Assert.Equal(TourStep.Controls, tour.Current);
    }

    [Fact]
    public void Next_WalksEveryStep_ThenEnds()
    {
        var tour = new FirstRunTour();
        tour.Start();
        var seen = new List<TourStep> { tour.Current };
        while (tour.Next())
        {
            seen.Add(tour.Current);
        }

        Assert.Equal(FirstRunTour.Steps, seen);
        Assert.False(tour.IsActive);
    }

    [Fact]
    public void Back_StopsAtTheFirstStep()
    {
        var tour = new FirstRunTour();
        tour.Start();
        Assert.False(tour.Back());

        tour.Next();
        tour.Next();
        Assert.True(tour.Back());
        Assert.Equal(TourStep.Controls, tour.Current);
    }

    [Fact]
    public void LastStep_IsFlagged_SoTheCardSaysDone()
    {
        var tour = new FirstRunTour();
        tour.Start();
        for (int i = 1; i < tour.Count; i++)
        {
            Assert.False(tour.IsLast);
            tour.Next();
        }
        Assert.True(tour.IsLast);
        Assert.Equal(TourStep.Settings, tour.Current);
    }

    [Fact]
    public void End_SkipsFromAnyStep_AndAReplayStartsOver()
    {
        var tour = new FirstRunTour();
        tour.Start();
        tour.Next();
        tour.Next();

        Assert.True(tour.End());
        Assert.False(tour.IsActive);
        Assert.False(tour.End());
        Assert.False(tour.Next());
        Assert.False(tour.Back());

        Assert.True(tour.Start());
        Assert.Equal(TourStep.Open, tour.Current);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    public void StartsOnLaunch_OnlyWhenUnseenAndTheHomeScreenIsIdle(bool seen, bool homeIdle, bool expected) =>
        Assert.Equal(expected, FirstRunTour.ShouldStartOnLaunch(seen, homeIdle));

    [Fact]
    public void ShortcutsStep_ListsPlaybackAndTheClipKeys()
    {
        Assert.Contains(ShortcutAction.SetIn, FirstRunTour.Keys);
        Assert.Contains(ShortcutAction.SetOut, FirstRunTour.Keys);
        Assert.Contains(ShortcutAction.ExportClip, FirstRunTour.Keys);
        Assert.Contains(ShortcutAction.PlayPause, FirstRunTour.Keys);
        Assert.Equal(FirstRunTour.Keys.Count, FirstRunTour.Keys.Distinct().Count());
    }

    [Fact]
    public void OnlyTheMenuAndShortcutsSteps_HaveNoSpotlight()
    {
        Assert.Equal(
            [TourStep.Menu, TourStep.Shortcuts],
            FirstRunTour.Steps.Where(s => !FirstRunTour.HasSpotlight(s)));
        Assert.Equal(TourPlacement.Centre, FirstRunTour.PlacementOf(TourStep.Shortcuts));
    }

    [Fact]
    public void SeenFlag_DefaultsToUnseen_AndIsSaved()
    {
        Assert.False(new AetherSettings().TourSeen);
        Assert.Contains(SettingKeys.TourSeen, SettingDefinitions.Keys);
    }
}

public class TourLayoutTests
{
    private const double CardWidth = 320;
    private const double CardHeight = 180;

    [Fact]
    public void Below_CentresUnderTheTarget()
    {
        var target = new TourRect(300, 200, 200, 40);
        (double x, double y) = TourLayout.PlaceCard(target, CardWidth, CardHeight, 1000, 700, TourPlacement.Below);

        Assert.Equal(240, x);
        Assert.Equal(target.Bottom + TourLayout.Gap, y);
    }

    [Fact]
    public void Below_FlipsAbove_WhenTheBottomIsShort()
    {
        var target = new TourRect(300, 560, 200, 40);
        (_, double y) = TourLayout.PlaceCard(target, CardWidth, CardHeight, 1000, 700, TourPlacement.Below);

        Assert.Equal(target.Y - TourLayout.Gap - CardHeight, y);
    }

    [Fact]
    public void Above_FlipsBelow_WhenTheTopIsShort()
    {
        var target = new TourRect(300, 40, 200, 40);
        (_, double y) = TourLayout.PlaceCard(target, CardWidth, CardHeight, 1000, 700, TourPlacement.Above);

        Assert.Equal(target.Bottom + TourLayout.Gap, y);
    }

    [Fact]
    public void Right_SitsBesideTheTarget_AndFlipsLeft_WhenThereIsNoRoom()
    {
        var rail = new TourRect(8, 40, 56, 500);
        (double x, double y) = TourLayout.PlaceCard(rail, CardWidth, CardHeight, 1000, 700, TourPlacement.Right);
        Assert.Equal(rail.Right + TourLayout.Gap, x);
        Assert.Equal(rail.CenterY - CardHeight / 2, y);

        var nearRight = new TourRect(800, 200, 56, 100);
        (double left, _) = TourLayout.PlaceCard(nearRight, CardWidth, CardHeight, 1000, 700, TourPlacement.Right);
        Assert.Equal(nearRight.X - TourLayout.Gap - CardWidth, left);
    }

    [Fact]
    public void Centre_AndNoTarget_CentreInTheArea()
    {
        Assert.Equal((340d, 260d), TourLayout.PlaceCard(null, CardWidth, CardHeight, 1000, 700, TourPlacement.Below));
        Assert.Equal((340d, 260d), TourLayout.PlaceCard(new TourRect(0, 0, 10, 10), CardWidth, CardHeight, 1000, 700, TourPlacement.Centre));
    }

    [Fact]
    public void TheCard_StaysInsideTheWindow()
    {
        // The gear sits at the top right: centred under it, the card would run off the right edge.
        var gear = new TourRect(940, 0, 40, 32);
        (double x, double y) = TourLayout.PlaceCard(gear, CardWidth, CardHeight, 1000, 700, TourPlacement.Below);

        Assert.Equal(1000 - CardWidth - TourLayout.Margin, x);
        Assert.Equal(gear.Bottom + TourLayout.Gap, y);
    }

    [Theory]
    [InlineData(700, true)]
    [InlineData(651, false)]
    public void MenuAndCard_SideBySide_OnlyWhenWideEnough(double width, bool fits) =>
        Assert.Equal(fits, TourLayout.MenuFits(width, CardWidth));

    [Fact]
    public void Lerp_MovesBetweenRects_AndNeverGoesNegative()
    {
        var from = new TourRect(0, 0, 100, 40);
        var to = new TourRect(100, 100, 0, 0);

        Assert.Equal(new TourRect(50, 50, 50, 20), TourRect.Lerp(from, to, 0.5));
        TourRect overshoot = TourRect.Lerp(from, to, 1.1);
        Assert.Equal(0, overshoot.Width);
        Assert.Equal(0, overshoot.Height);
        Assert.True(new TourRect(10, 10, 20, 20).Collapse() is { X: 20, Y: 20, IsEmpty: true });
    }
}
