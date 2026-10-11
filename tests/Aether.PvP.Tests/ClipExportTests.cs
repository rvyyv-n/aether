using Aether.PvP.Review.Clipping;
using Xunit.Abstractions;

namespace Aether.PvP.Tests;

public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (!ClipExportTests.FfmpegOnPath())
            Skip = "ffmpeg.exe is not on PATH; the real-export timing test needs it.";
    }
}

public sealed class ClipExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aether-clip-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public ClipExportTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    internal static bool FfmpegOnPath()
    {
        string name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(p => File.Exists(Path.Combine(p, name)));
    }

    private string MakeSource(string name = "src.mp4")
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, "not a real video");
        return path;
    }

    private ClipRequest Request(string source) =>
        new(source, 1.5, 4.0, Path.Combine(_dir, "out"), "Clip");

    // Fake ffmpeg: a script that writes to its last argument, then behaves as asked.
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
    public void Build_PlacesSeekBeforeInputAndCopiesStreams()
    {
        var args = FfmpegArguments.Build(Request("C:\\v\\game.mkv"), "C:\\o\\clip.mkv.part");

        int ss = args.ToList().IndexOf("-ss");
        int i = args.ToList().IndexOf("-i");
        Assert.True(ss >= 0 && ss < i);
        Assert.Equal("1.500", args[ss + 1]);
        Assert.Equal("C:\\v\\game.mkv", args[i + 1]);
        Assert.Equal("2.500", args[args.ToList().IndexOf("-t") + 1]);
        Assert.Equal("copy", args[args.ToList().IndexOf("-c") + 1]);
        Assert.Equal("matroska", args[args.ToList().IndexOf("-f") + 1]);
        Assert.Equal("C:\\o\\clip.mkv.part", args[^1]);
    }

    [Theory]
    [InlineData("a.mp4", "mp4")]
    [InlineData("a.MKV", "matroska")]
    [InlineData("a.webm", "webm")]
    [InlineData("a.mov", "mov")]
    [InlineData("a.avi", "mp4")]
    public void Build_PicksMuxerFromSourceExtension(string file, string muxer)
    {
        var args = FfmpegArguments.Build(Request(file), "x.part").ToList();
        Assert.Equal(muxer, args[args.IndexOf("-f") + 1]);
    }

    [Fact]
    public async Task Export_RenamesPartToFinalOnSuccess()
    {
        using var service = new ClipExportService(FakeFfmpeg("ok"));

        var result = await service.ExportAsync(Request(MakeSource()));

        Assert.True(File.Exists(result.OutputPath));
        Assert.EndsWith(".mp4", result.OutputPath);
        Assert.True(result.SizeBytes > 0);
        Assert.Single(OutputFiles());
    }

    [Fact]
    public async Task Export_SameSecondClipsKeepSeparateFiles()
    {
        using var service = new ClipExportService(FakeFfmpeg("ok"));
        string source = MakeSource();

        var a = await service.ExportAsync(Request(source));
        var b = await service.ExportAsync(Request(source));

        Assert.NotEqual(a.OutputPath, b.OutputPath);
        Assert.Equal(2, OutputFiles().Length);
    }

    [Fact]
    public async Task Export_FailureDeletesPart()
    {
        using var service = new ClipExportService(FakeFfmpeg("fail"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(Request(MakeSource())));

        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task Export_CancelKillsProcessAndDeletesPart()
    {
        using var service = new ClipExportService(FakeFfmpeg("hang"));
        using var cts = new CancellationTokenSource();

        var task = service.ExportAsync(Request(MakeSource()), cts.Token);
        for (int i = 0; i < 100 && OutputFiles().Length == 0; i++)
            await Task.Delay(50);
        Assert.Single(OutputFiles());
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task Export_MissingBinaryThrowsAndLeavesNothing()
    {
        using var service = new ClipExportService(Path.Combine(_dir, "no-such-ffmpeg.exe"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ExportAsync(Request(MakeSource())));

        Assert.Empty(OutputFiles());
    }

    [Fact]
    public async Task Export_RejectsBadRange()
    {
        using var service = new ClipExportService(FakeFfmpeg("ok"));
        string source = MakeSource();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExportAsync(new ClipRequest(source, 3, 3, Path.Combine(_dir, "out"))));
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.ExportAsync(Request(Path.Combine(_dir, "missing.mp4"))));
    }

    [Fact]
    public async Task PreWarm_MissingBinaryDoesNotThrowAndMarksUnavailable()
    {
        using var service = new ClipExportService();

        await service.PreWarmAsync(Path.Combine(_dir, "no-such-ffmpeg.exe"));

        Assert.False(service.IsFfmpegAvailable);
    }

    [Fact]
    public async Task PreWarm_WorkingBinaryMarksAvailable()
    {
        using var service = new ClipExportService();

        await service.PreWarmAsync(FakeFfmpeg("ok"));
        await service.PreWarmAsync(FakeFfmpeg("ok"));

        Assert.True(service.IsFfmpegAvailable);
    }

    [FfmpegFact]
    public async Task Export_RealFfmpegIsFast()
    {
        string ffmpeg = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string source = Path.Combine(_dir, "real.mp4");
        var gen = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ffmpeg)
        {
            ArgumentList = { "-v", "error", "-f", "lavfi", "-i", "testsrc=duration=6:size=320x240:rate=30",
                             "-c:v", "mpeg4", "-g", "30", "-y", source },
            CreateNoWindow = true,
            UseShellExecute = false
        })!;
        await gen.WaitForExitAsync();
        Assert.Equal(0, gen.ExitCode);

        using var service = new ClipExportService();
        await service.PreWarmAsync(ffmpeg);
        Assert.True(service.IsFfmpegAvailable);

        var result = await service.ExportAsync(new ClipRequest(source, 1, 3, Path.Combine(_dir, "out")));

        _output.WriteLine($"Export took {result.Elapsed.TotalMilliseconds:F0} ms, {result.SizeBytes} bytes");
        Assert.True(result.SizeBytes > 0);
        Assert.Single(OutputFiles());
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(1), $"Export took {result.Elapsed.TotalMilliseconds:F0} ms");
    }
}
