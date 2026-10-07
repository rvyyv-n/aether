using System;
using System.Collections.Generic;
using System.ComponentModel;
using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class PlayerViewModelTests
{
    [Fact]
    public void InitialValues_MatchExpectedDefaults()
    {
        var vm = new PlayerViewModel();

        Assert.Equal(0, vm.Position);
        Assert.Equal(0, vm.Duration);
        Assert.Equal(1, vm.Speed);
        Assert.Equal(100, vm.Volume);
        Assert.False(vm.IsPaused);
        Assert.False(vm.IsAtEnd);
        Assert.Equal(0, vm.FramesPerSecond);
        Assert.Equal(PlaybackStatus.Idle, vm.Status);
        Assert.Equal(PlayerState.Initial, vm.State);
    }

    [Fact]
    public void Apply_EqualState_RaisesNoPropertyChanged()
    {
        var vm = new PlayerViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Apply(PlayerState.Initial);

        Assert.Empty(raised);

        var customState = new PlayerState(10, 100, 1.5, 80, true, false, 60, PlaybackStatus.Paused);
        vm.Apply(customState);
        raised.Clear();

        vm.Apply(customState);

        Assert.Empty(raised);
    }

    [Fact]
    public void Apply_PositionChanged_RaisesStateAndPositionOnly()
    {
        var vm = new PlayerViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var next = PlayerState.Initial with { Position = 12.34 };
        vm.Apply(next);

        Assert.Equal(new[] { "State", "Position" }, raised);
        Assert.Equal(12.34, vm.Position);
    }

    [Fact]
    public void Apply_DurationChanged_RaisesStateAndDurationOnly()
    {
        var vm = new PlayerViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var next = PlayerState.Initial with { Duration = 120.0 };
        vm.Apply(next);

        Assert.Equal(new[] { "State", "Duration" }, raised);
        Assert.Equal(120.0, vm.Duration);
    }

    [Fact]
    public void Apply_SpeedChanged_RaisesStateAndSpeedOnly()
    {
        var vm = new PlayerViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var next = PlayerState.Initial with { Speed = 2.0 };
        vm.Apply(next);

        Assert.Equal(new[] { "State", "Speed" }, raised);
        Assert.Equal(2.0, vm.Speed);
    }

    [Fact]
    public void Apply_VolumeChanged_RaisesStateAndVolumeOnly()
    {
        var vm = new PlayerViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var next = PlayerState.Initial with { Volume = 75.0 };
        vm.Apply(next);

        Assert.Equal(new[] { "State", "Volume" }, raised);
        Assert.Equal(75.0, vm.Volume);
    }

    [Fact]
    public void Apply_IsPausedChanged_RaisesStateAndIsPausedOnly()
    {
        var vm = new PlayerViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var next = PlayerState.Initial with { IsPaused = true };
        vm.Apply(next);

        Assert.Equal(new[] { "State", "IsPaused" }, raised);
        Assert.True(vm.IsPaused);
    }

    [Fact]
    public void Apply_IsAtEndChanged_RaisesStateAndIsAtEndOnly()
    {
        var vm = new PlayerViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var next = PlayerState.Initial with { IsAtEnd = true };
        vm.Apply(next);

        Assert.Equal(new[] { "State", "IsAtEnd" }, raised);
        Assert.True(vm.IsAtEnd);
    }

    [Fact]
    public void Apply_FramesPerSecondChanged_RaisesStateAndFramesPerSecondOnly()
    {
        var vm = new PlayerViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var next = PlayerState.Initial with { FramesPerSecond = 59.94 };
        vm.Apply(next);

        Assert.Equal(new[] { "State", "FramesPerSecond" }, raised);
        Assert.Equal(59.94, vm.FramesPerSecond);
    }

    [Fact]
    public void Apply_StatusChanged_RaisesStateAndStatusOnly()
    {
        var vm = new PlayerViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var next = PlayerState.Initial with { Status = PlaybackStatus.Playing };
        vm.Apply(next);

        Assert.Equal(new[] { "State", "Status" }, raised);
        Assert.Equal(PlaybackStatus.Playing, vm.Status);
    }

    [Fact]
    public void Apply_MultipleFieldsChanged_RaisesStateOnceAndChangedProperties()
    {
        var vm = new PlayerViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var next = PlayerState.Initial with
        {
            Position = 45.0,
            Speed = 1.25,
            Status = PlaybackStatus.Playing,
        };
        vm.Apply(next);

        Assert.Equal(new[] { "State", "Position", "Speed", "Status" }, raised);
    }

    [Fact]
    public void Apply_EventArgsInstancesAreReusedAcrossCalls()
    {
        var vm = new PlayerViewModel();
        var argsMapFirst = new Dictionary<string, PropertyChangedEventArgs>();
        var argsMapSecond = new Dictionary<string, PropertyChangedEventArgs>();

        void Handler1(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null)
            {
                argsMapFirst[e.PropertyName] = e;
            }
        }

        void Handler2(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null)
            {
                argsMapSecond[e.PropertyName] = e;
            }
        }

        var fullState = new PlayerState(10, 100, 1.5, 80, true, true, 60, PlaybackStatus.Paused);

        vm.PropertyChanged += Handler1;
        vm.Apply(fullState);
        vm.PropertyChanged -= Handler1;

        vm.PropertyChanged += Handler2;
        vm.Apply(PlayerState.Initial);
        vm.PropertyChanged -= Handler2;

        string[] expectedNames =
        [
            "State", "Position", "Duration", "Speed", "Volume",
            "IsPaused", "IsAtEnd", "FramesPerSecond", "Status"
        ];

        foreach (string name in expectedNames)
        {
            Assert.True(argsMapFirst.TryGetValue(name, out PropertyChangedEventArgs? firstArgs), $"Missing first args for {name}");
            Assert.True(argsMapSecond.TryGetValue(name, out PropertyChangedEventArgs? secondArgs), $"Missing second args for {name}");
            Assert.Same(firstArgs, secondArgs);
        }
    }

    [Fact]
    public void ZeroAllocation_Apply_WithNoSubscribers()
    {
        var vm = new PlayerViewModel();
        var stateA = new PlayerState(1, 10, 1.0, 100, false, false, 30, PlaybackStatus.Playing);
        var stateB = new PlayerState(2, 20, 1.5, 80, true, true, 60, PlaybackStatus.Paused);

        // Warm up JIT and method dispatch
        for (int i = 0; i < 50; i++)
        {
            vm.Apply(stateA);
            vm.Apply(stateB);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            vm.Apply((i & 1) == 0 ? stateA : stateB);
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }
}
