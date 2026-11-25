using System.Globalization;
using Aether.Core.Interop.Mpv;
using Microsoft.Win32.SafeHandles;

namespace Aether.Core.Playback.Thumbnails;

/// <summary>
/// Decodes hover-preview frames for local media files on a single lazy headless libmpv core
/// (<c>vo=libmpv</c> + <c>sw</c> renderer). Never allocates or initializes native handles until the
/// first frame is requested, so app startup and idle RAM stay unaffected.
/// </summary>
public sealed unsafe class LocalThumbnailDecoder : IDisposable
{
    private readonly Action<string, int, ThumbnailFrame> _onFrameDecoded;
    private readonly ThumbnailLruCache<(string Path, int Bucket), ThumbnailFrame> _cache;
    private readonly Lock _gate = new();
    private readonly AutoResetEvent _wakeSignal = new(false);
    private readonly AutoResetEvent _renderSignal = new(false);

    private Thread? _worker;
    private bool _disposed;
    private bool _resetRequested;
    private DecodeRequest? _pending;

    private MpvContext? _mpv;
    private MpvRenderContext? _render;
    private string? _loadedPath;
    private int _sourceWidth;
    private int _sourceHeight;
    private byte[] _scratch = [];

    private sealed record DecodeRequest(string Path, int Bucket, double TargetSeconds);

    public LocalThumbnailDecoder(Action<string, int, ThumbnailFrame> onFrameDecoded, int cacheCapacity = 96)
    {
        ArgumentNullException.ThrowIfNull(onFrameDecoded);
        _onFrameDecoded = onFrameDecoded;
        _cache = new ThumbnailLruCache<(string, int), ThumbnailFrame>(cacheCapacity);
    }

    public bool TryGetCached(string path, int bucket, out ThumbnailFrame frame) =>
        _cache.TryGet((path, bucket), out frame);

    /// <summary>Queues a latest-wins background decode for <paramref name="path"/> at <paramref name="targetSeconds"/>.</summary>
    public void Request(string path, int bucket, double targetSeconds)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (_cache.TryGet((path, bucket), out ThumbnailFrame? cached))
        {
            _onFrameDecoded(path, bucket, cached);
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _resetRequested = false;
            _pending = new DecodeRequest(path, bucket, Math.Max(0, targetSeconds));
            if (_worker is null)
            {
                _worker = new Thread(WorkerLoop)
                {
                    Name = "Aether.ThumbnailDecoder",
                    IsBackground = true,
                };
                _worker.Start();
            }
            _wakeSignal.Set();
        }
    }

    /// <summary>Cancels any queued work and closes the open file on the decoder core while keeping the core warm.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _pending = null;
            _resetRequested = true;
            _wakeSignal.Set();
        }
    }

    public void Dispose()
    {
        Thread? worker;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _pending = null;
            worker = _worker;
            _wakeSignal.Set();
        }

        worker?.Join(TimeSpan.FromSeconds(2));
        _wakeSignal.Dispose();
        _renderSignal.Dispose();
    }

    private void WorkerLoop()
    {
        try
        {
            while (true)
            {
                DecodeRequest? request;
                bool reset;
                lock (_gate)
                {
                    if (_disposed)
                    {
                        break;
                    }
                    reset = _resetRequested;
                    _resetRequested = false;
                    request = _pending;
                    _pending = null;
                }

                if (reset && _mpv is not null)
                {
                    ResetLoadedFile();
                }

                if (request is null)
                {
                    DrainEvents(0);
                    _wakeSignal.WaitOne(200);
                    continue;
                }

                if (!EnsureEngine())
                {
                    break;
                }

                ProcessRequest(request);
            }
        }
        finally
        {
            // Required release order: free mpv_render_context strictly before MpvContext.
            _render?.Dispose();
            _render = null;
            _mpv?.Dispose();
            _mpv = null;
            _loadedPath = null;
        }
    }

    private bool EnsureEngine()
    {
        if (_mpv is not null && _render is not null)
        {
            return true;
        }

        try
        {
            var options = new MpvContextOptions
            {
                HardwareDecoder = "no",
                BackBufferSize = "1MiB",
                VideoLatencyHacks = false,
                AdditionalOptions =
                [
                    new("ao", "null"),
                    new("aid", "no"),
                    new("sid", "no"),
                    new("pause", "yes"),
                    new("vd-lavc-fast", "yes"),
                    new("vd-lavc-skiploopfilter", "all"),
                    new("hr-seek", "no"),
                    new("cache", "no"),
                    new("demuxer-max-bytes", "4MiB"),
                    new("ytdl", "no"),
                    new("sub-auto", "no"),
                    new("audio-display", "no"),
                    new("keepaspect", "yes"),
                    new("sws-scaler", "fast-bilinear"),
                ],
            };

            _mpv = MpvContext.Create(options);
            SafeWaitHandle renderHandle = _renderSignal.SafeWaitHandle;
            _render = MpvRenderContext.CreateSoftware(_mpv, renderHandle.DangerousGetHandle());
            return true;
        }
        catch
        {
            _render?.Dispose();
            _render = null;
            _mpv?.Dispose();
            _mpv = null;
            return false;
        }
    }

    private void ResetLoadedFile()
    {
        if (_mpv is null || _loadedPath is null)
        {
            _loadedPath = null;
            return;
        }

        try
        {
            _mpv.Command("stop");
            DrainEvents(0.01);
        }
        catch (Exception ex) when (ex is MpvException or ObjectDisposedException)
        {
        }
        _loadedPath = null;
        _sourceWidth = 0;
        _sourceHeight = 0;
    }

    private void ProcessRequest(DecodeRequest request)
    {
        if (_mpv is not { } mpv || _render is not { } render)
        {
            return;
        }

        if (_cache.TryGet((request.Path, request.Bucket), out ThumbnailFrame? cached))
        {
            _onFrameDecoded(request.Path, request.Bucket, cached);
            return;
        }

        try
        {
            if (!string.Equals(_loadedPath, request.Path, StringComparison.OrdinalIgnoreCase))
            {
                if (!LoadFile(mpv, request.Path))
                {
                    return;
                }
            }

            if (ShouldAbort(request.Path, request.Bucket))
            {
                return;
            }

            DrainEvents(0);
            _ = render.Update();
            _renderSignal.Reset();
            mpv.Command("seek", request.TargetSeconds.ToString("0.###", CultureInfo.InvariantCulture), "absolute+keyframes");

            if (!WaitForSeekFrame(mpv, render, request.Path, request.Bucket))
            {
                return;
            }

            (int width, int height) = ThumbnailTiming.FitThumbnailSize(_sourceWidth, _sourceHeight);
            int stride = width * 4;
            int totalBytes = stride * height;
            if (_scratch.Length < totalBytes)
            {
                _scratch = new byte[totalBytes];
            }

            int err;
            fixed (byte* ptr = _scratch)
            {
                err = render.RenderSoftware(width, height, (nuint)stride, ptr);
            }
            if (err < 0)
            {
                return;
            }

            byte[] frameBytes = new byte[totalBytes];
            Buffer.BlockCopy(_scratch, 0, frameBytes, 0, totalBytes);
            if (frameBytes.Length >= 4 && frameBytes[3] == 0)
            {
                for (int i = 3; i < frameBytes.Length; i += 4)
                {
                    frameBytes[i] = 0xFF;
                }
            }

            var frame = new ThumbnailFrame(width, height, frameBytes);
            _cache.Set((request.Path, request.Bucket), frame);

            if (!ShouldAbort(request.Path, request.Bucket))
            {
                _onFrameDecoded(request.Path, request.Bucket, frame);
            }
        }
        catch (Exception ex) when (ex is MpvException or ObjectDisposedException)
        {
        }
    }

    private bool LoadFile(MpvContext mpv, string path)
    {
        _loadedPath = null;
        _sourceWidth = 0;
        _sourceHeight = 0;
        mpv.Command("loadfile", path, "replace");

        long deadline = Environment.TickCount64 + 3000;
        bool loaded = false;
        while (Environment.TickCount64 < deadline && !ShouldAbort(path))
        {
            MpvEventId ev = PollNextEvent(mpv, 0.02);
            if (ev == MpvEventId.FileLoaded)
            {
                loaded = true;
                break;
            }
            if (ev is MpvEventId.EndFile or MpvEventId.Shutdown)
            {
                return false;
            }
        }

        if (!loaded || ShouldAbort(path))
        {
            return false;
        }

        _loadedPath = path;
        ReadSourceDimensions(mpv);
        return true;
    }

    private bool WaitForSeekFrame(MpvContext mpv, MpvRenderContext render, string path, int bucket)
    {
        long restartDeadline = Environment.TickCount64 + 800;
        bool sawSeek = false;
        bool restarted = false;

        while (Environment.TickCount64 < restartDeadline && !ShouldAbort(path, bucket))
        {
            MpvEventId ev = PollNextEvent(mpv, 0.015);
            if (ev == MpvEventId.Seek)
            {
                sawSeek = true;
                continue;
            }
            if (sawSeek && ev == MpvEventId.PlaybackRestart)
            {
                restarted = true;
                break;
            }
            if (ev is MpvEventId.EndFile or MpvEventId.Shutdown)
            {
                return false;
            }
        }

        if (!restarted || ShouldAbort(path, bucket))
        {
            return false;
        }

        if (_sourceWidth <= 0 || _sourceHeight <= 0)
        {
            ReadSourceDimensions(mpv);
        }

        long frameDeadline = Environment.TickCount64 + 250;
        while (Environment.TickCount64 < frameDeadline && !ShouldAbort(path, bucket))
        {
            if ((render.Update() & MpvRenderUpdateFlags.Frame) != 0)
            {
                return true;
            }
            DrainEvents(0);
            _renderSignal.WaitOne(15);
        }

        return !ShouldAbort(path, bucket);
    }

    private void ReadSourceDimensions(MpvContext mpv)
    {
        _sourceWidth = ParseDimension(mpv.GetPropertyString("dwidth")) ?? ParseDimension(mpv.GetPropertyString("width")) ?? 0;
        _sourceHeight = ParseDimension(mpv.GetPropertyString("dheight")) ?? ParseDimension(mpv.GetPropertyString("height")) ?? 0;
    }

    private static int? ParseDimension(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : null;

    private bool ShouldAbort(string currentPath, int? currentBucket = null)
    {
        lock (_gate)
        {
            if (_disposed || _resetRequested)
            {
                return true;
            }
            if (_pending is null)
            {
                return false;
            }
            return !string.Equals(_pending.Path, currentPath, StringComparison.OrdinalIgnoreCase)
                || (currentBucket is { } b && _pending.Bucket != b);
        }
    }

    private bool IsDisposedOrReset()
    {
        lock (_gate)
        {
            return _disposed || _resetRequested;
        }
    }

    private void DrainEvents(double timeoutSeconds)
    {
        if (_mpv is not { } mpv)
        {
            return;
        }

        int budget = 32;
        while (budget-- > 0)
        {
            MpvEventId id = PollNextEvent(mpv, timeoutSeconds);
            timeoutSeconds = 0;
            if (id == MpvEventId.None || id == MpvEventId.Shutdown)
            {
                break;
            }
        }
    }

    private static MpvEventId PollNextEvent(MpvContext mpv, double timeoutSeconds)
    {
        bool added = false;
        MpvHandle handle = mpv.Handle;
        try
        {
            handle.DangerousAddRef(ref added);
            MpvEvent* ev = MpvApi.WaitEvent(handle.DangerousGetHandle(), timeoutSeconds);
            return ev == null ? MpvEventId.None : (MpvEventId)ev->EventId;
        }
        catch (ObjectDisposedException)
        {
            return MpvEventId.Shutdown;
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }
}
