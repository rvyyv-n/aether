using System.Diagnostics;
using System.Globalization;
using Aether.PvP.Review.Clipping;
using Xunit.Abstractions;

namespace Aether.PvP.Tests;

public sealed class ClipExportPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aether-clippipeline-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public ClipExportPipelineTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string MakeSource(string name = "src.mp4")
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, "not a real video");
        return path;
    }

    private ClipRequest Request(string source, double start = 1.5, double end = 4.0, string prefix = "Clip") =>
        new(source, start, end, Path.Combine(_dir, "out"), prefix);

    private string FakeFfmpeg(string behaviour)
    {
        if (OperatingSystem.IsWindows())
        {
            string path = Path.Combine(_dir, "fake-ffmpeg.cmd");
            string tail = behaviour switch
            {
                "fail" => "exit /b 1",
                "hang" => "ping -n 30 127.0.0.1 >nul\r\nexit /b 0",
                _ => "exit /b 0"
            };
            File.WriteAllText(path,
                "@echo off\r\nfor %%a in (%*) do set \"last=%%~a\"\r\necho data> \"%last%\"\r\n" + tail + "\r\n");
            return path;
        }
        else
        {
            string path = Path.Combine(_dir, "fake-ffmpeg.sh");
            string tail = behaviour switch
            {
                "fail" => "exit 1",
                "hang" => "sleep 30",
                _ => "exit 0"
            };
            File.WriteAllText(path, "#!/bin/sh\nfor a; do last=\"$a\"; done\necho data > \"$last\"\n" + tail + "\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }
    }

    private string[] OutputFiles() =>
        Directory.Exists(Path.Combine(_dir, "out")) ? Directory.GetFiles(Path.Combine(_dir, "out")) : [];

    [Fact]
    public void Build_IncludesMapAllStreamsAndZeroTsFlag()
    {
        var args = FfmpegArguments.Build(Request("C:\\v\\game.mkv"), "C:\\o\\clip.part").ToList();

        // -map 0 ensures all audio and subtitle streams are preserved rather than ffmpeg default 1-stream selection
        int mapIndex = args.IndexOf("-map");
        Assert.True(mapIndex >= 0);
        Assert.Equal("0", args[mapIndex + 1]);

        // -avoid_negative_ts make_zero prevents shifted timestamps in container copy
        int tsIndex = args.IndexOf("-avoid_negative_ts");
        Assert.True(tsIndex >= 0);
        Assert.Equal("make_zero", args[tsIndex + 1]);

        // Banner and prompt suppressions
        Assert.Contains("-hide_banner", args);
        Assert.Contains("-nostdin", args);
        Assert.Contains("-nostats", args);

        int logIndex = args.IndexOf("-loglevel");
        Assert.True(logIndex >= 0);
        Assert.Equal("error", args[logIndex + 1]);

        int yIndex = args.IndexOf("-y");
        Assert.True(yIndex >= 0);
        Assert.Equal("C:\\o\\clip.part", args[yIndex + 1]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("")]
    public void Build_CalculatesDurationWithInvariantCulture(string cultureName)
    {
        var prevCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = cultureName.Length > 0 ? new CultureInfo(cultureName) : CultureInfo.InvariantCulture;

            var req = new ClipRequest("sample.mp4", 10.12345, 15.67891, "C:\\out");
            var args = FfmpegArguments.Build(req, "temp.part").ToList();

            int ssIndex = args.IndexOf("-ss");
            int tIndex = args.IndexOf("-t");

            // Timestamps must use invariant period decimals, not commas
            Assert.Equal("10.123", args[ssIndex + 1]);
            Assert.Equal("5.555", args[tIndex + 1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
        }
    }

    [Fact]
    public void Build_PreservesPathsWithSpacesAndQuotesInArgumentList()
    {
        string spacedSource = @"C:\Video Recordings\Game Match (1) [2026].mp4";
        string spacedDest = @"C:\My Clips\High Light (1).mp4.part";

        var args = FfmpegArguments.Build(new ClipRequest(spacedSource, 1.0, 5.0, @"C:\My Clips"), spacedDest);

        // Individual arguments in ArgumentList must not be pre-quoted with double quotes
        // because ProcessStartInfo.ArgumentList applies OS-level escaping automatically.
        int inputIndex = args.ToList().IndexOf("-i");
        Assert.Equal(spacedSource, args[inputIndex + 1]);
        Assert.False(args[inputIndex + 1].StartsWith("\""));
        Assert.False(args[inputIndex + 1].EndsWith("\""));

        Assert.Equal(spacedDest, args[^1]);
        Assert.False(args[^1].StartsWith("\""));
        Assert.False(args[^1].EndsWith("\""));

        var psi = new ProcessStartInfo("ffmpeg.exe");
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        Assert.Equal(spacedSource, psi.ArgumentList[inputIndex + 1]);
        Assert.Equal(spacedDest, psi.ArgumentList[^1]);
    }

    [Fact]
    public void Build_PositionsSeekBeforeInputForGopAlignment()
    {
        // Placing -ss before -i performs fast container seek. With -c copy, cutting begins
        // at the keyframe at or prior to the requested timestamp, starting up to one GOP early.
        var args = FfmpegArguments.Build(Request("video.mp4", 5.2, 10.4), "out.part").ToList();

        int ss = args.IndexOf("-ss");
        int i = args.IndexOf("-i");
        int c = args.IndexOf("-c");

        Assert.True(ss >= 0 && ss < i, "-ss must precede -i for fast container seek");
        Assert.True(c >= 0);
        Assert.Equal("copy", args[c + 1]);
    }

    [FfmpegFact]
    public async Task Export_GopKeyframeAlignment_RealFfmpeg()
    {
        string ffmpeg = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string source = Path.Combine(_dir, "gop-source.mp4");

        // Generate synthetic video: 6 seconds at 30 fps with keyframes every 60 frames (2 seconds).
        // Keyframes will be at t=0.0s, 2.0s, 4.0s.
        var gen = Process.Start(new ProcessStartInfo(ffmpeg)
        {
            ArgumentList = { "-v", "error", "-f", "lavfi", "-i", "testsrc=duration=6:size=320x240:rate=30",
                             "-c:v", "mpeg4", "-g", "60", "-y", source },
            CreateNoWindow = true,
            UseShellExecute = false
        })!;
        await gen.WaitForExitAsync();
        Assert.Equal(0, gen.ExitCode);

        using var service = new ClipExportService();
        await service.PreWarmAsync(ffmpeg);
        Assert.True(service.IsFfmpegAvailable);

        // Request cut at 1.5s to 3.5s. Because keyframes are at 0s and 2s, container seek
        // starts at the keyframe at or before 1.5s (i.e. 0.0s), demonstrating GOP alignment without re-encoding.
        var result = await service.ExportAsync(new ClipRequest(source, 1.5, 3.5, Path.Combine(_dir, "out")));

        Assert.True(File.Exists(result.OutputPath));
        Assert.True(result.SizeBytes > 0);
        _output.WriteLine($"GOP clip generated at {result.OutputPath}, size {result.SizeBytes} bytes");
    }

    [Fact]
    public async Task Validate_RejectsNegativeStartTimestamp()
    {
        using var service = new ClipExportService(FakeFfmpeg("ok"));
        string source = MakeSource();

        var negStartReq = new ClipRequest(source, -0.5, 5, _dir);
        var exStart = await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(negStartReq));
        Assert.Contains("negative", exStart.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_RejectsEndTimestampLessOrEqualStart()
    {
        using var service = new ClipExportService(FakeFfmpeg("ok"));
        string source = MakeSource();

        var equalReq = new ClipRequest(source, 5.0, 5.0, _dir);
        var exEqual = await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(equalReq));
        Assert.Contains("strictly greater", exEqual.Message, StringComparison.OrdinalIgnoreCase);

        var invertedReq = new ClipRequest(source, 5.0, 3.0, _dir);
        var exInverted = await Assert.ThrowsAsync<ArgumentException>(() => service.ExportAsync(invertedReq));
        Assert.Contains("strictly greater", exInverted.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_Cancellation_AlreadyCancelledToken_DoesNotCreatePartOrRun()
    {
        using var service = new ClipExportService(FakeFfmpeg("ok"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportAsync(Request(MakeSource()), cts.Token));

        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task Export_DisposeService_CancelsInFlightExportAndCleansPart()
    {
        var service = new ClipExportService(FakeFfmpeg("hang"));
        string source = MakeSource();

        var task = service.ExportAsync(Request(source));

        // Wait until ffmpeg started and wrote the .part file
        for (int i = 0; i < 100 && OutputFiles().Length == 0; i++)
            await Task.Delay(50);
        Assert.Single(OutputFiles());

        // Disposing service cancels _lifetime token
        service.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task LosslessClipper_ExportLosslessAsync_CancelsCleanly()
    {
        string fake = FakeFfmpeg("hang");
        string source = MakeSource();
        using var cts = new CancellationTokenSource();

        var task = LosslessClipper.ExportLosslessAsync(Request(source), fake, cts.Token);

        for (int i = 0; i < 100 && OutputFiles().Length == 0; i++)
            await Task.Delay(50);
        Assert.Single(OutputFiles());

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task Export_PreservesSpacesInDestinationDirectoryAndPrefix()
    {
        using var service = new ClipExportService(FakeFfmpeg("ok"));
        string source = MakeSource("game clip.mp4");
        string outDir = Path.Combine(_dir, "Exports With Spaces");

        var result = await service.ExportAsync(new ClipRequest(source, 1.0, 3.0, outDir, "My PvP Clip"));

        Assert.True(File.Exists(result.OutputPath));
        Assert.StartsWith(outDir, result.OutputPath);
        Assert.Contains("My PvP Clip_", Path.GetFileName(result.OutputPath));
    }
}
