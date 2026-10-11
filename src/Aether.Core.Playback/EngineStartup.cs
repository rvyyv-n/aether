namespace Aether.Core.Playback;

public enum EngineStartupStatus
{
    Ready,

    /// <summary>The native library could not be loaded.</summary>
    MissingBinary,

    /// <summary>The library loaded but creating or initializing the engine threw.</summary>
    Failed,
}

/// <summary>The outcome of <see cref="EngineStartup.RunAsync{TEngine}"/>. <see cref="Engine"/> is set only when ready.</summary>
public sealed record EngineStartupResult<TEngine>(EngineStartupStatus Status, TEngine? Engine, string? Error)
    where TEngine : class;

/// <summary>
/// Loads and creates the playback engine off the UI thread, so the window paints at once and shows
/// its loading state while the native libraries load. The caller owns the engine it gets back and
/// must dispose it if it no longer wants it (e.g. the page unloaded while starting).
/// </summary>
public static class EngineStartup
{
    /// <summary>Runs <see cref="Run{TEngine}"/> on the thread pool. Never throws for startup failures; they come back as a status.</summary>
    public static Task<EngineStartupResult<TEngine>> RunAsync<TEngine>(Func<bool> isAvailable, Func<TEngine> create)
        where TEngine : class
    {
        ArgumentNullException.ThrowIfNull(isAvailable);
        ArgumentNullException.ThrowIfNull(create);
        return Task.Run(() => Run(isAvailable, create));
    }

    /// <summary>Probes for the library, then creates the engine. Blocking; call off the UI thread.</summary>
    public static EngineStartupResult<TEngine> Run<TEngine>(Func<bool> isAvailable, Func<TEngine> create)
        where TEngine : class
    {
        ArgumentNullException.ThrowIfNull(isAvailable);
        ArgumentNullException.ThrowIfNull(create);
        try
        {
            if (!isAvailable())
            {
                return new(EngineStartupStatus.MissingBinary, null, null);
            }
            return new(EngineStartupStatus.Ready, create(), null);
        }
        catch (Exception exception)
        {
            return new(EngineStartupStatus.Failed, null, exception.Message);
        }
    }
}
