using System;
using System.Collections.Generic;
using System.Threading;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class ChromeVisibilityTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    [Fact]
    public void StartsVisible_HidesAfterIdleDelay()
    {
        var time = new ManualTimeProvider();
        var chrome = new ChromeVisibility(time, Delay);

        Assert.True(chrome.IsVisible);
        Assert.Equal(Delay, chrome.DueIn);

        time.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.False(chrome.Tick());
        Assert.True(chrome.IsVisible);
        Assert.Equal(TimeSpan.FromMilliseconds(1), chrome.DueIn);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(chrome.Tick());
        Assert.False(chrome.IsVisible);
        Assert.Null(chrome.DueIn);
        Assert.False(chrome.Tick());
    }

    [Fact]
    public void Activity_RestartsCountdown_AndShowsHiddenChrome()
    {
        var time = new ManualTimeProvider();
        var chrome = new ChromeVisibility(time, Delay);

        time.Advance(TimeSpan.FromSeconds(1.5));
        Assert.False(chrome.NotifyActivity()); // already visible
        time.Advance(TimeSpan.FromSeconds(1.5));
        Assert.False(chrome.Tick());           // countdown restarted at 1.5 s

        time.Advance(TimeSpan.FromSeconds(0.5));
        Assert.True(chrome.Tick());
        Assert.True(chrome.NotifyActivity());
        Assert.True(chrome.IsVisible);
        Assert.Equal(Delay, chrome.DueIn);
    }

    [Fact]
    public void Hold_KeepsChromeVisible_AndShowsIt()
    {
        var time = new ManualTimeProvider();
        var chrome = new ChromeVisibility(time, Delay);
        time.Advance(Delay);
        Assert.True(chrome.Tick());

        Assert.True(chrome.SetHold(ChromeHolds.NotPlaying, true));
        Assert.True(chrome.IsVisible);
        Assert.Null(chrome.DueIn);

        time.Advance(TimeSpan.FromMinutes(5));
        Assert.False(chrome.Tick());
        Assert.True(chrome.IsVisible);
    }

    [Fact]
    public void ReleasingHold_RestartsCountdown()
    {
        var time = new ManualTimeProvider();
        var chrome = new ChromeVisibility(time, Delay);
        chrome.SetHold(ChromeHolds.PointerOverControls, true);
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.False(chrome.SetHold(ChromeHolds.PointerOverControls, false));
        Assert.Equal(Delay, chrome.DueIn);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(chrome.Tick());
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(chrome.Tick()); // the card shrinks first
        Assert.False(chrome.IsExpanded);
        Assert.True(chrome.IsVisible);
        time.Advance(Delay);
        Assert.True(chrome.Tick());
    }

    [Fact]
    public void Hold_ExpandsCard_ActivityAloneDoesNot()
    {
        var time = new ManualTimeProvider();
        var chrome = new ChromeVisibility(time, Delay);
        Assert.False(chrome.IsExpanded);

        chrome.NotifyActivity();
        Assert.False(chrome.IsExpanded);

        chrome.SetHold(ChromeHolds.KeyboardFocus, true);
        Assert.True(chrome.IsExpanded);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(chrome.Tick());
        Assert.True(chrome.IsExpanded);
    }

    [Fact]
    public void RailHold_KeepsChromeUp_ButLeavesThePillSlim()
    {
        var time = new ManualTimeProvider();
        var chrome = new ChromeVisibility(time, Delay);
        time.Advance(Delay);
        Assert.True(chrome.Tick());

        Assert.True(chrome.SetHold(ChromeHolds.Rail, true));
        Assert.False(chrome.IsExpanded);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.False(chrome.Tick());
        Assert.True(chrome.IsVisible);

        chrome.SetHold(ChromeHolds.Rail, false);
        Assert.Equal(Delay, chrome.DueIn);
    }

    [Fact]
    public void CardShrinks_OnTime_WhileThePointerMovesElsewhere()
    {
        var time = new ManualTimeProvider();
        var chrome = new ChromeVisibility(time, Delay);
        chrome.SetHold(ChromeHolds.NotPlaying, true);
        chrome.SetHold(ChromeHolds.NotPlaying, false);

        time.Advance(TimeSpan.FromSeconds(1.5));
        chrome.NotifyActivity();
        Assert.Equal(TimeSpan.FromSeconds(0.5), chrome.DueIn);
        time.Advance(TimeSpan.FromSeconds(0.5));
        Assert.False(chrome.Tick());
        Assert.False(chrome.IsExpanded);

        // The chrome then hides a full idle delay after the shrink.
        Assert.Equal(Delay, chrome.DueIn);
    }

    [Fact]
    public void Holds_AreIndependentFlags()
    {
        var chrome = new ChromeVisibility(new ManualTimeProvider(), Delay);
        chrome.SetHold(ChromeHolds.NotPlaying, true);
        chrome.SetHold(ChromeHolds.Interaction, true);
        chrome.SetHold(ChromeHolds.NotPlaying, false);

        Assert.Equal(ChromeHolds.Interaction, chrome.Holds);
        Assert.Null(chrome.DueIn);

        chrome.SetHold(ChromeHolds.Interaction, false);
        Assert.Equal(ChromeHolds.None, chrome.Holds);
        Assert.Equal(Delay, chrome.DueIn);
    }

    [Fact]
    public void EndingADrag_KeepsAnOpenFlyoutsHold()
    {
        var chrome = new ChromeVisibility(new ManualTimeProvider(), Delay);
        chrome.SetHold(ChromeHolds.Flyout, true);
        chrome.SetHold(ChromeHolds.Interaction, true);
        chrome.SetHold(ChromeHolds.Interaction, false);

        Assert.Equal(ChromeHolds.Flyout, chrome.Holds);
        Assert.Null(chrome.DueIn);
    }

    [Fact]
    public void ClearingAHoldThatIsNotSet_DoesNotRestartCountdown()
    {
        var time = new ManualTimeProvider();
        var chrome = new ChromeVisibility(time, Delay);
        time.Advance(TimeSpan.FromSeconds(1.5));

        chrome.SetHold(ChromeHolds.Interaction, false);

        Assert.Equal(TimeSpan.FromSeconds(0.5), chrome.DueIn);
    }

    [Fact]
    public void NonPositiveDelay_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChromeVisibility(new ManualTimeProvider(), TimeSpan.Zero));
    }
}

public class TimecodeTests
{
    [Theory]
    [InlineData(0, "00:00:00.000")]
    [InlineData(83.456, "00:01:23.456")]
    [InlineData(250, "00:04:10.000")]
    [InlineData(3599.9999, "00:59:59.999")]
    [InlineData(3600, "01:00:00.000")]
    [InlineData(363_600.5, "101:00:00.500")]
    [InlineData(-1, "00:00:00.000")]
    [InlineData(double.NaN, "00:00:00.000")]
    [InlineData(double.PositiveInfinity, "00:00:00.000")]
    public void Format_TruncatesToMilliseconds(double seconds, string expected)
    {
        Assert.Equal(expected, Timecode.Format(seconds));
    }

    [Fact]
    public void Format_FrameTimesDoNotLoseAMillisecond()
    {
        // Frame 5 at 60 fps starts at 0.08333… s and must show .083, not .082.
        Assert.Equal("00:00:00.083", Timecode.Format(5 / 60.0));
        Assert.Equal("00:00:01.001", Timecode.Format(1.001));
    }

    [Theory]
    [InlineData(0, 0, "00:00")]
    [InlineData(42.9, 195, "00:42")]
    [InlineData(195, 195, "03:15")]
    [InlineData(59.9999999, 60, "01:00")]
    [InlineData(3599, 3599, "59:59")]
    [InlineData(42, 3600, "0:00:42")]
    [InlineData(3723, 7200, "1:02:03")]
    [InlineData(3600, 0, "1:00:00")]
    [InlineData(363_600.5, 363_600.5, "101:00:00")]
    [InlineData(-1, 195, "00:00")]
    [InlineData(double.NaN, double.NaN, "00:00")]
    [InlineData(double.PositiveInfinity, 195, "00:00")]
    public void FormatClock_ShowsHoursOnlyFromAnHour(double seconds, double duration, string expected)
    {
        Assert.Equal(expected, Timecode.FormatClock(seconds, duration));
    }
}

public class MediaFormatsTests
{
    [Theory]
    [InlineData(@"C:\clips\round.mp4", true)]
    [InlineData(@"C:\clips\round.MKV", true)]
    [InlineData("round.mov", true)]
    [InlineData("round.WebM", true)]
    [InlineData("round.avi", false)]
    [InlineData("round.mp4.part", false)]
    [InlineData("mp4", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_MatchesExtensionCaseInsensitively(string? path, bool expected)
    {
        Assert.Equal(expected, MediaFormats.IsSupported(path));
    }

    [Theory]
    [InlineData("round.mkv", "MKV")]
    [InlineData(".webm", "WEBM")]
    [InlineData("noextension", "")]
    [InlineData(null, "")]
    public void Label_IsUpperCaseExtensionWithoutDot(string? path, string expected)
    {
        Assert.Equal(expected, MediaFormats.Label(path));
    }

    [Fact]
    public void Extensions_AreTheFourContainers()
    {
        Assert.Equal([".mp4", ".mkv", ".mov", ".webm"], MediaFormats.Extensions);
    }
}

public class EngineStartupTests
{
    private sealed class Engine;

    [Fact]
    public void Run_Ready_ReturnsTheEngine()
    {
        var engine = new Engine();
        EngineStartupResult<Engine> result = EngineStartup.Run(() => true, () => engine);

        Assert.Equal(EngineStartupStatus.Ready, result.Status);
        Assert.Same(engine, result.Engine);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Run_MissingBinary_DoesNotCreate()
    {
        bool created = false;
        EngineStartupResult<Engine> result = EngineStartup.Run(() => false, () =>
        {
            created = true;
            return new Engine();
        });

        Assert.Equal(EngineStartupStatus.MissingBinary, result.Status);
        Assert.Null(result.Engine);
        Assert.False(created);
    }

    [Fact]
    public void Run_CreateThrows_ReportsFailedWithMessage()
    {
        EngineStartupResult<Engine> result =
            EngineStartup.Run<Engine>(() => true, () => throw new InvalidOperationException("mpv_initialize failed"));

        Assert.Equal(EngineStartupStatus.Failed, result.Status);
        Assert.Null(result.Engine);
        Assert.Equal("mpv_initialize failed", result.Error);
    }

    [Fact]
    public void Run_ProbeThrows_ReportsFailed()
    {
        EngineStartupResult<Engine> result =
            EngineStartup.Run<Engine>(() => throw new BadImageFormatException("wrong architecture"), () => new Engine());

        Assert.Equal(EngineStartupStatus.Failed, result.Status);
        Assert.Equal("wrong architecture", result.Error);
    }

    [Fact]
    public async System.Threading.Tasks.Task RunAsync_ReturnsBeforeTheWorkRuns()
    {
        // The probe blocks until the caller holds the task, so a synchronous RunAsync would time out
        // in the probe and report MissingBinary. (Comparing thread ids is flaky: the awaiting test
        // thread is itself a pool thread and may pick up the queued work.)
        using var callerHasTask = new ManualResetEventSlim();
        bool onPoolThread = false;
        var engine = new Engine();

        System.Threading.Tasks.Task<EngineStartupResult<Engine>> task = EngineStartup.RunAsync(() =>
        {
            onPoolThread = Thread.CurrentThread.IsThreadPoolThread;
            return callerHasTask.Wait(TimeSpan.FromSeconds(10));
        }, () => engine);
        callerHasTask.Set();
        EngineStartupResult<Engine> result = await task;

        Assert.Equal(EngineStartupStatus.Ready, result.Status);
        Assert.Same(engine, result.Engine);
        Assert.True(onPoolThread);
    }
}

public unsafe class VideoDimensionDispatchTests
{
    [Fact]
    public void DisplaySize_ReachesViewModelAfterFlush()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        using var events = new NativeMpvEvents();

        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyInt64((ulong)PlayerProperty.DisplayWidth, 2560)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyInt64((ulong)PlayerProperty.DisplayHeight, 1080)));
        Assert.Equal(1, scheduler.RequestFlushCount); // coalesced
        Assert.Equal(0, vm.VideoWidth);

        dispatcher.Flush();

        Assert.Equal(2560, vm.VideoWidth);
        Assert.Equal(1080, vm.VideoHeight);
        Assert.Contains(nameof(PlayerViewModel.VideoWidth), raised);
        Assert.Contains(nameof(PlayerViewModel.VideoHeight), raised);
    }

    [Fact]
    public void DisplaySize_UnavailableOrInvalid_FallsBackToZero()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyInt64((ulong)PlayerProperty.DisplayWidth, 1920)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyInt64((ulong)PlayerProperty.DisplayHeight, -5)));
        dispatcher.Flush();
        Assert.Equal(1920, vm.VideoWidth);
        Assert.Equal(0, vm.VideoHeight);

        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyUnavailable((ulong)PlayerProperty.DisplayWidth)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyDouble((ulong)PlayerProperty.DisplayHeight, 1080.0)));
        dispatcher.Flush();
        Assert.Equal(0, vm.VideoWidth);
        Assert.Equal(0, vm.VideoHeight); // wrong format is ignored, not converted
    }

    [Fact]
    public void InitialState_HasNoVideoSize()
    {
        Assert.Equal(0, PlayerState.Initial.VideoWidth);
        Assert.Equal(0, PlayerState.Initial.VideoHeight);
    }
}

public class FailureDetailsTests
{
    [Fact]
    public void Format_ListsTitleSubtitleAndRows_OnePerLine()
    {
        string text = FailureDetails.Format(
            "Engine failed to start",
            "libmpv-2.dll wasn’t found",
            [("Searched", @"C:\Program Files\Aether\libmpv-2.dll"), ("Error", "Reinstall Aether to restore it.")]);

        Assert.Equal(
            "Engine failed to start\r\nlibmpv-2.dll wasn’t found\r\n"
            + @"Searched: C:\Program Files\Aether\libmpv-2.dll" + "\r\nError: Reinstall Aether to restore it.",
            text);
    }

    [Fact]
    public void Format_SkipsEmptySubtitleAndRows()
    {
        string text = FailureDetails.Format("Engine failed to start", " ", [("Searched", null), ("Error", "Access denied")]);

        Assert.Equal("Engine failed to start\r\nError: Access denied", text);
    }
}
