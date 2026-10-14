using Aether.Core.Interop.Native;
using Aether.Core.Interop.Windowing;
using Aether.Core.Playback;

namespace Aether.Core.Tests;

public class DisplayRefreshTrackerTests
{
    private const nint MonitorA = 0x101;
    private const nint MonitorB = 0x202;

    [Theory]
    [InlineData(120, "120")]
    [InlineData(144, "144")]
    [InlineData(240, "240")]
    [InlineData(60, "60")]
    [InlineData(59, "59")]
    public void A_Real_Rate_Is_Sent_As_Whole_Hertz(int hertz, string expected)
    {
        Assert.Equal(expected, DisplayRefreshTracker.Format(hertz));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)] // "hardware default"
    [InlineData(9)]
    [InlineData(1001)]
    [InlineData(int.MaxValue)]
    public void A_Rate_That_Is_Not_Real_Leaves_The_Choice_To_Mpv(int hertz)
    {
        Assert.Equal(DisplayRefreshTracker.Unknown, DisplayRefreshTracker.Format(hertz));
    }

    [Fact]
    public void Nothing_Is_Overridden_Before_The_First_Reading()
    {
        Assert.Equal("0", new DisplayRefreshTracker().Current);
    }

    [Fact]
    public void The_First_Reading_Is_Read_And_Reported()
    {
        var tracker = new DisplayRefreshTracker();

        Assert.True(tracker.NeedsReading(MonitorA, displayChanged: false));
        Assert.Equal("120", tracker.Accept(MonitorA, 120));
        Assert.Equal("120", tracker.Current);
    }

    [Fact]
    public void The_Same_Rate_Is_Not_Reported_Twice()
    {
        var tracker = new DisplayRefreshTracker();
        tracker.Accept(MonitorA, 120);

        Assert.Null(tracker.Accept(MonitorA, 120));
        Assert.Equal("120", tracker.Current);
    }

    [Fact]
    public void A_Moved_Window_On_The_Same_Monitor_Does_Not_Read_The_Display_Again()
    {
        var tracker = new DisplayRefreshTracker();
        tracker.Accept(MonitorA, 120);

        Assert.False(tracker.NeedsReading(MonitorA, displayChanged: false));
    }

    [Fact]
    public void Moving_To_Another_Monitor_Reads_It_And_Reports_Its_Rate()
    {
        var tracker = new DisplayRefreshTracker();
        tracker.Accept(MonitorA, 120);

        Assert.True(tracker.NeedsReading(MonitorB, displayChanged: false));
        Assert.Equal("60", tracker.Accept(MonitorB, 60));
        Assert.False(tracker.NeedsReading(MonitorB, displayChanged: false));
    }

    [Fact]
    public void Moving_To_A_Monitor_With_The_Same_Rate_Reads_It_But_Sends_Nothing()
    {
        var tracker = new DisplayRefreshTracker();
        tracker.Accept(MonitorA, 144);

        Assert.True(tracker.NeedsReading(MonitorB, displayChanged: false));
        Assert.Null(tracker.Accept(MonitorB, 144));
    }

    [Fact]
    public void A_Display_Change_Reads_Again_Even_On_The_Same_Monitor()
    {
        var tracker = new DisplayRefreshTracker();
        tracker.Accept(MonitorA, 60);

        Assert.True(tracker.NeedsReading(MonitorA, displayChanged: true));
        Assert.Equal("120", tracker.Accept(MonitorA, 120));
    }

    [Fact]
    public void A_Monitor_That_Cannot_Be_Read_Clears_The_Override()
    {
        var tracker = new DisplayRefreshTracker();
        tracker.Accept(MonitorA, 120);

        Assert.Equal("0", tracker.Accept(MonitorB, 0));
        Assert.Equal("0", tracker.Current);
    }

    [Fact]
    public void An_Unreadable_First_Monitor_Reports_Nothing_To_Send()
    {
        var tracker = new DisplayRefreshTracker();

        Assert.Null(tracker.Accept(MonitorA, 0));
        Assert.False(tracker.NeedsReading(MonitorA, displayChanged: false));
    }

    [Fact]
    public void No_Window_Has_No_Monitor_And_No_Rate()
    {
        Assert.Equal(0, DisplayRefresh.MonitorFor(0));
        Assert.Equal(0, DisplayRefresh.QueryHertz(0));
    }

    [Fact]
    public void The_Primary_Monitor_Reports_A_Plausible_Rate_Or_None()
    {
        // MONITOR_DEFAULTTOPRIMARY with no window: exercises the monitor and mode structs against the real API.
        nint monitor = User32.MonitorFromWindow(0, 1);

        int hertz = DisplayRefresh.QueryHertz(monitor);

        Assert.InRange(hertz, 0, 1000);
    }
}
