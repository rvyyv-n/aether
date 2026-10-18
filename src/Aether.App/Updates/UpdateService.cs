using Aether.Core.Playback;
using Aether.Core.Playback.Updates;

namespace Aether_App.Updates;

/// <summary>
/// Keeps Aether current: a quiet daily check (when switched on) a little after launch, and Check now from About. Everything runs off
/// the UI thread; a found update downloads quietly and installs when Aether closes, or on Restart from the toast or About page.
/// </summary>
internal static class UpdateService
{
    private static readonly Lazy<UpdateController> Lazy = new(() => new UpdateController(new VelopackUpdateEngine()));
    private static int s_schedulerStarted;

    public static UpdateController Controller => Lazy.Value;

    private static string LastCheckPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "updates", "last-check.txt");

    /// <summary>Starts the scheduled checks, once per launch. A copy that was not installed by Velopack never checks.</summary>
    public static void StartScheduledChecks()
    {
        if (Interlocked.Exchange(ref s_schedulerStarted, 1) == 0)
        {
            _ = Task.Run(RunSchedulerAsync);
        }
    }

    /// <summary>Check now: unlike the scheduled check this ignores the switch and the daily limit, and its result shows on the About page.</summary>
    public static async Task CheckNowAsync()
    {
        UpdateStatus before = Controller.Status;
        UpdateStatus after = await Task.Run(() => Controller.CheckAndDownloadAsync()).ConfigureAwait(false);
        if (after.State is not (UpdateState.Failed or UpdateState.Checking))
        {
            await RecordCheckAsync().ConfigureAwait(false);
        }
        if (after.State == UpdateState.Ready && before.State != UpdateState.Ready)
        {
            AnnounceReady(after.Version);
        }
    }

    public static void RestartToUpdate()
    {
        if (!Controller.RestartToUpdate())
        {
            App.RunOnUi(() => AppToasts.Show("app-update", AppStrings.Get("Toast_UpdateRestartFailed"), ToastTone.Failure));
        }
    }

    private static async Task RunSchedulerAsync()
    {
        await App.SettingsLoaded.ConfigureAwait(false);
        if (Controller.Status.State == UpdateState.NotInstalled)
        {
            return;
        }
        await Task.Delay(UpdateSchedule.StartupDelay).ConfigureAwait(false);
        while (true)
        {
            bool enabled = App.Settings.Current.UpdatesAutoCheck;
            DateTimeOffset? last = await ReadLastCheckAsync().ConfigureAwait(false);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            TimeSpan wait = UpdateSchedule.RetryDelay;
            if (UpdateSchedule.IsDue(enabled, last, now))
            {
                UpdateStatus after;
                try
                {
                    after = await Controller.CheckAndDownloadAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    after = UpdateStatus.Idle;
                }
                if (after.State is UpdateState.UpToDate or UpdateState.Ready)
                {
                    await RecordCheckAsync().ConfigureAwait(false);
                    wait = UpdateSchedule.Interval;
                    if (after.State == UpdateState.Ready)
                    {
                        AnnounceReady(after.Version);
                    }
                }
            }
            else if (enabled)
            {
                wait = UpdateSchedule.DueIn(last, now) + TimeSpan.FromSeconds(1);
            }
            await Task.Delay(wait).ConfigureAwait(false);
        }
    }

    // The toast offers Restart but never acts on its own, so a video that is playing is never interrupted.
    private static void AnnounceReady(string? version) =>
        App.RunOnUi(() => AppToasts.Show("app-update", AppStrings.Format("Toast_UpdateReady", version ?? ""), ToastTone.Success,
            AppStrings.Get("Toast_UpdateRestart"), RestartToUpdate));

    private static async Task RecordCheckAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LastCheckPath)!);
            await File.WriteAllTextAsync(LastCheckPath, DateTimeOffset.UtcNow.ToString("O")).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The next launch checks again, which costs one small request.
        }
    }

    private static async Task<DateTimeOffset?> ReadLastCheckAsync()
    {
        try
        {
            return DateTimeOffset.TryParse(await File.ReadAllTextAsync(LastCheckPath).ConfigureAwait(false), out DateTimeOffset when) ? when : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
