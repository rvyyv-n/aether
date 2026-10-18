namespace Aether.Core.Playback.Updates;

/// <summary>
/// Runs one update at a time: check, download and queue it for the next exit. Every failure ends as <see cref="UpdateState.Failed"/>
/// with the installed version untouched; nothing here starts an install except <see cref="RestartToUpdate"/>.
/// </summary>
public sealed class UpdateController
{
    private readonly IUpdateEngine _engine;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private UpdateStatus _status;

    public UpdateController(IUpdateEngine engine)
    {
        _engine = engine;
        _status = engine.IsInstalled ? UpdateStatus.Idle : new UpdateStatus(UpdateState.NotInstalled);
    }

    /// <summary>Raised on whichever thread changed the status.</summary>
    public event Action<UpdateStatus>? StatusChanged;

    public UpdateStatus Status => Volatile.Read(ref _status);

    public string? CurrentVersion => _engine.CurrentVersion;

    /// <summary>
    /// Looks for a newer release and, when there is one, downloads it and queues it to install on exit. Returns at once with the
    /// current status when a check is already running, nothing can be installed, or a release is already waiting.
    /// </summary>
    public async Task<UpdateStatus> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!Status.CanCheck || Status.State == UpdateState.Ready || !await _busy.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Status;
        }
        try
        {
            Set(new UpdateStatus(UpdateState.Checking));
            string? found = await _engine.CheckAsync(cancellationToken).ConfigureAwait(false);
            if (found is null || !IsNewerThanInstalled(found))
            {
                return Set(new UpdateStatus(UpdateState.UpToDate, _engine.CurrentVersion));
            }

            Set(new UpdateStatus(UpdateState.Downloading, found));
            var progress = new Progress<double>(fraction => Set(new UpdateStatus(UpdateState.Downloading, found, Math.Clamp(fraction, 0, 1))));
            await _engine.DownloadAsync(progress, cancellationToken).ConfigureAwait(false);
            _engine.ApplyOnExit();
            return Set(new UpdateStatus(UpdateState.Ready, found));
        }
        catch (OperationCanceledException)
        {
            Set(UpdateStatus.Idle);
            throw;
        }
        catch (Exception)
        {
            // Offline, a rate limit, a bad download or a failed hash check: the installed copy is still the one that runs.
            return Set(new UpdateStatus(UpdateState.Failed));
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>Installs the waiting release now and starts it. Only valid once <see cref="UpdateState.Ready"/>; a failure leaves Aether running.</summary>
    public bool RestartToUpdate()
    {
        if (Status.State != UpdateState.Ready)
        {
            return false;
        }
        try
        {
            _engine.ApplyAndRestart();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Velopack already compares versions; this only stops a feed that offers an older or equal tag from being treated as an update.
    private bool IsNewerThanInstalled(string found) =>
        !ReleaseVersion.TryParse(found, out ReleaseVersion next)
        || !ReleaseVersion.TryParse(_engine.CurrentVersion, out ReleaseVersion current)
        || next.CompareTo(current) > 0;

    private UpdateStatus Set(UpdateStatus status)
    {
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(status);
        return status;
    }
}
