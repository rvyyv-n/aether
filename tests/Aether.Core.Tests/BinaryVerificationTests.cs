using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Aether.Core.Interop;
using Aether.Core.Interop.Mpv;
using Aether.Core.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace Aether.Core.Tests;

public sealed class BinaryVerificationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir;

    public BinaryVerificationTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), "aether-binary-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort temp cleanup
        }
    }

    [NativeBinariesFact]
    public void Verify_LibMpvAndAngle_Present_Loadable_And_ReportVersions()
    {
        // 1. Verify libmpv presence and loading
        Assert.True(NativeTestEnvironment.TryFindBinary("libmpv-2.dll", out string? mpvPath) ||
                    NativeTestEnvironment.TryFindBinary("mpv-2.dll", out mpvPath),
            "libmpv binary (libmpv-2.dll or mpv-2.dll) must be present where app/tests expect it.");
        Assert.NotNull(mpvPath);
        Assert.True(File.Exists(mpvPath), $"libmpv binary must exist at {mpvPath}");

        bool mpvLoaded = NativeLibraries.IsMpvAvailable();
        Assert.True(mpvLoaded, $"libmpv at '{mpvPath}' failed to load.");

        string? mpvVersion = NativeTestEnvironment.GetBinaryVersion(mpvPath);
        ulong clientApiVersion = MpvApi.ClientApiVersion();
        uint apiMajor = (uint)(clientApiVersion >> 16);
        uint apiMinor = (uint)(clientApiVersion & 0xFFFF);

        _output.WriteLine($"[libmpv] Found: {mpvPath}");
        _output.WriteLine($"[libmpv] File/Product Version: {mpvVersion ?? "Unknown"}");
        _output.WriteLine($"[libmpv] Client API Version: {apiMajor}.{apiMinor} (raw: 0x{clientApiVersion:X8})");

        Assert.False(string.IsNullOrWhiteSpace(mpvVersion), "libmpv should have a stamped file or product version.");
        Assert.True(clientApiVersion > 0, "mpv_client_api_version must return a positive version integer.");

        // 2. Verify ANGLE libEGL presence and loading
        Assert.True(NativeTestEnvironment.TryFindBinary("libEGL.dll", out string? eglPath),
            "libEGL.dll must be present where app/tests expect it.");
        Assert.NotNull(eglPath);
        Assert.True(File.Exists(eglPath), $"libEGL.dll must exist at {eglPath}");

        bool eglLoaded = NativeLibraries.IsAngleAvailable();
        Assert.True(eglLoaded, $"libEGL.dll at '{eglPath}' failed to load.");

        string? eglVersion = NativeTestEnvironment.GetBinaryVersion(eglPath);
        _output.WriteLine($"[ANGLE libEGL] Found: {eglPath}");
        _output.WriteLine($"[ANGLE libEGL] Version: {eglVersion ?? "Unknown"}");
        Assert.False(string.IsNullOrWhiteSpace(eglVersion), "libEGL.dll should have a stamped version.");

        // 3. Verify ANGLE libGLESv2 presence and loading
        Assert.True(NativeTestEnvironment.TryFindBinary("libGLESv2.dll", out string? glesPath),
            "libGLESv2.dll must be present where app/tests expect it.");
        Assert.NotNull(glesPath);
        Assert.True(File.Exists(glesPath), $"libGLESv2.dll must exist at {glesPath}");

        bool glesLoaded = NativeLibrary.TryLoad(glesPath, out nint glesHandle);
        try
        {
            Assert.True(glesLoaded, $"libGLESv2.dll at '{glesPath}' failed to load.");
        }
        finally
        {
            if (glesHandle != 0)
            {
                NativeLibrary.Free(glesHandle);
            }
        }

        string? glesVersion = NativeTestEnvironment.GetBinaryVersion(glesPath);
        _output.WriteLine($"[ANGLE libGLESv2] Found: {glesPath}");
        _output.WriteLine($"[ANGLE libGLESv2] Version: {glesVersion ?? "Unknown"}");
        Assert.False(string.IsNullOrWhiteSpace(glesVersion), "libGLESv2.dll should have a stamped version.");
    }

    [MpvFact]
    public async Task SmokeTest_RealMediaDecode_Headless()
    {
        // Require ffmpeg to generate test video
        if (!NativeTestEnvironment.IsFfmpegAvailable(out string? ffmpegPath))
        {
            _output.WriteLine("ffmpeg is not on PATH; cannot generate headless test clip. Skipping smoke test.");
            return;
        }

        string testClipPath = Path.Combine(_tempDir, "decode-smoke-clip.mp4");
        NativeTestEnvironment.GenerateTinyTestClip(ffmpegPath!, testClipPath, durationSeconds: 2);
        Assert.True(File.Exists(testClipPath), "Generated test clip must exist.");
        Assert.True(new FileInfo(testClipPath).Length > 0, "Generated test clip must not be empty.");

        // Headless context: software decoding without display/audio window requirements
        var options = new MpvContextOptions
        {
            HardwareDecoder = "no",
            AdditionalOptions =
            [
                new("vo", "null"),
                new("ao", "null"),
                new("pause", "no"),
            ],
        };

        using var ctx = MpvContext.Create(options);
        Assert.False(ctx.IsDisposed);

        // Open clip through real Aether wrapper
        ctx.Command("loadfile", testClipPath);

        double duration = 0;
        double timePos = 0;

        // Poll for decode progress (duration > 0 and playback position advancing)
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(50);

            string? durationStr = ctx.GetPropertyString("duration");
            if (durationStr is not null && double.TryParse(durationStr, CultureInfo.InvariantCulture, out double d) && d > 0)
            {
                duration = d;
            }

            string? timePosStr = ctx.GetPropertyString("time-pos");
            if (timePosStr is not null && double.TryParse(timePosStr, CultureInfo.InvariantCulture, out double t) && t > 0)
            {
                timePos = t;
            }

            if (duration > 0 && timePos > 0)
            {
                break;
            }
        }

        _output.WriteLine($"[Decode Smoke Test] Decoded file: {testClipPath}");
        _output.WriteLine($"[Decode Smoke Test] Measured Duration: {duration:F2}s");
        _output.WriteLine($"[Decode Smoke Test] Advanced Time-Pos: {timePos:F4}s");

        Assert.True(duration > 0, $"Duration was expected to be > 0, got {duration}");
        Assert.True(timePos > 0, $"Time-pos was expected to advance > 0, got {timePos}");

        // Stop playback and ensure clean core shutdown
        ctx.Command("stop");
        ctx.Dispose();
        Assert.True(ctx.IsDisposed);
        Assert.True(MpvContext.WaitForDestroyedCores(5000), "Background core destruction must finish cleanly.");
    }
}
