using Aether.Core.Playback.Updates;

namespace Aether.Core.Tests;

public sealed class UpdateControllerTests
{
    private sealed class FakeEngine : IUpdateEngine
    {
        public bool IsInstalled { get; set; } = true;
        public string? CurrentVersion { get; set; } = "1.0.0";
        public string? Offer { get; set; }
        public Exception? CheckFails { get; set; }
        public Exception? DownloadFails { get; set; }
        public Exception? RestartFails { get; set; }
        public TaskCompletionSource? HoldCheck { get; set; }
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public int AppliedOnExit { get; private set; }
        public int Restarts { get; private set; }

        public async Task<string?> CheckAsync(CancellationToken cancellationToken)
        {
            Checks++;
            if (HoldCheck is not null)
            {
                await HoldCheck.Task;
            }
            return CheckFails is null ? Offer : throw CheckFails;
        }

        public Task DownloadAsync(IProgress<double>? progress, CancellationToken cancellationToken)
        {
            Downloads++;
            progress?.Report(0.5);
            return DownloadFails is null ? Task.CompletedTask : throw DownloadFails;
        }

        public void ApplyOnExit() => AppliedOnExit++;

        public void ApplyAndRestart()
        {
            Restarts++;
            if (RestartFails is not null)
            {
                throw RestartFails;
            }
        }
    }

    [Fact]
    public async Task ACopyWithoutAnInstaller_IsNeverChecked()
    {
        var engine = new FakeEngine { IsInstalled = false, Offer = "2.0.0" };
        var controller = new UpdateController(engine);

        UpdateStatus status = await controller.CheckAndDownloadAsync();

        Assert.Equal(UpdateState.NotInstalled, status.State);
        Assert.Equal(0, engine.Checks);
    }

    [Fact]
    public async Task WithNothingNewer_ReportsUpToDateAndDownloadsNothing()
    {
        var engine = new FakeEngine { Offer = null };
        var controller = new UpdateController(engine);

        UpdateStatus status = await controller.CheckAndDownloadAsync();

        Assert.Equal(UpdateState.UpToDate, status.State);
        Assert.Equal(0, engine.Downloads);
        Assert.Equal(0, engine.AppliedOnExit);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("0.9.0")]
    public async Task AnOfferThatIsNotNewer_IsIgnored(string offer)
    {
        var engine = new FakeEngine { Offer = offer };
        var controller = new UpdateController(engine);

        UpdateStatus status = await controller.CheckAndDownloadAsync();

        Assert.Equal(UpdateState.UpToDate, status.State);
        Assert.Equal(0, engine.Downloads);
    }

    [Fact]
    public async Task ANewerRelease_IsDownloadedAndQueuedForExitButNeverRestarted()
    {
        var engine = new FakeEngine { Offer = "1.1.0" };
        var controller = new UpdateController(engine);
        var seen = new List<UpdateState>();
        controller.StatusChanged += s => seen.Add(s.State);

        UpdateStatus status = await controller.CheckAndDownloadAsync();

        Assert.Equal(new UpdateStatus(UpdateState.Ready, "1.1.0"), status);
        Assert.Equal(1, engine.AppliedOnExit);
        Assert.Equal(0, engine.Restarts);
        Assert.Contains(UpdateState.Checking, seen);
        Assert.Contains(UpdateState.Downloading, seen);
    }

    [Fact]
    public async Task ACheckThatFails_LeavesTheInstalledCopyAlone()
    {
        var engine = new FakeEngine { CheckFails = new HttpRequestException("offline") };
        var controller = new UpdateController(engine);

        UpdateStatus status = await controller.CheckAndDownloadAsync();

        Assert.Equal(UpdateState.Failed, status.State);
        Assert.Equal(0, engine.AppliedOnExit);
        Assert.True(status.CanCheck);
    }

    [Fact]
    public async Task ADownloadThatFails_IsNeverQueuedAndCanBeRetried()
    {
        var engine = new FakeEngine { Offer = "1.1.0", DownloadFails = new InvalidDataException("hash mismatch") };
        var controller = new UpdateController(engine);

        UpdateStatus failed = await controller.CheckAndDownloadAsync();
        Assert.Equal(UpdateState.Failed, failed.State);
        Assert.Equal(0, engine.AppliedOnExit);

        engine.DownloadFails = null;
        UpdateStatus retried = await controller.CheckAndDownloadAsync();
        Assert.Equal(UpdateState.Ready, retried.State);
        Assert.Equal(1, engine.AppliedOnExit);
    }

    [Fact]
    public async Task ASecondCheckWhileOneIsRunning_DoesNotStartAnother()
    {
        var gate = new TaskCompletionSource();
        var engine = new FakeEngine { Offer = null, HoldCheck = gate };
        var controller = new UpdateController(engine);

        Task<UpdateStatus> first = controller.CheckAndDownloadAsync();
        UpdateStatus second = await controller.CheckAndDownloadAsync();
        gate.SetResult();
        await first;

        Assert.Equal(UpdateState.Checking, second.State);
        Assert.Equal(1, engine.Checks);
    }

    [Fact]
    public async Task OnceReady_AnotherCheckDoesNotDownloadAgain()
    {
        var engine = new FakeEngine { Offer = "1.1.0" };
        var controller = new UpdateController(engine);
        await controller.CheckAndDownloadAsync();

        UpdateStatus again = await controller.CheckAndDownloadAsync();

        Assert.Equal(UpdateState.Ready, again.State);
        Assert.Equal(1, engine.Checks);
        Assert.Equal(1, engine.Downloads);
    }

    [Fact]
    public async Task ACancelledCheck_ReturnsToIdleAndThrows()
    {
        var engine = new FakeEngine { CheckFails = new OperationCanceledException() };
        var controller = new UpdateController(engine);

        await Assert.ThrowsAsync<OperationCanceledException>(() => controller.CheckAndDownloadAsync());

        Assert.Equal(UpdateState.Idle, controller.Status.State);
    }

    [Fact]
    public async Task Restart_OnlyWorksOnceAReleaseIsReady()
    {
        var engine = new FakeEngine { Offer = "1.1.0" };
        var controller = new UpdateController(engine);

        Assert.False(controller.RestartToUpdate());
        await controller.CheckAndDownloadAsync();
        Assert.True(controller.RestartToUpdate());
        Assert.Equal(1, engine.Restarts);
    }

    [Fact]
    public async Task ARestartThatFails_ReportsFalseSoAetherKeepsRunning()
    {
        var engine = new FakeEngine { Offer = "1.1.0", RestartFails = new IOException("Update.exe missing") };
        var controller = new UpdateController(engine);
        await controller.CheckAndDownloadAsync();

        Assert.False(controller.RestartToUpdate());
    }
}
