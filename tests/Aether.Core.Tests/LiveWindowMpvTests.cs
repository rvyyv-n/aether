using System.Diagnostics;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback.Streams;
using Aether.Core.Tests.TestSupport;

namespace Aether.Core.Tests;

/// <summary><see cref="LiveWindow"/> against the real libmpv's <c>demuxer-cache-state</c>, whose shape it depends on.</summary>
public sealed class LiveWindowMpvTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aether-live-" + Guid.NewGuid().ToString("N"));

    public LiveWindowMpvTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [MpvFact]
    public async Task The_Cache_State_Reads_As_A_Window_Around_The_Playing_Point()
    {
        if (!NativeTestEnvironment.IsFfmpegAvailable(out string? ffmpeg))
        {
            throw new InvalidOperationException("ffmpeg is needed to make the test clip");
        }
        string clip = Path.Combine(_dir, "clip.ts");
        var psi = new ProcessStartInfo(ffmpeg!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=duration=20:size=160x120:rate=10", "-c:v", "mpeg2video", clip })
        {
            psi.ArgumentList.Add(arg);
        }
        using (Process process = Process.Start(psi)!)
        {
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }

        using MpvContext mpv = MpvContext.Create(new MpvContextOptions
        {
            HardwareDecoder = "no",
            AdditionalOptions = [new("vo", "null"), new("ao", "null"), new("cache", "yes")],
        });
        mpv.Command("loadfile", clip);
        LiveWindow? window = null;
        for (int i = 0; i < 80 && window is null; i++)
        {
            await Task.Delay(50);
            window = LiveWindow.Parse(mpv.GetPropertyString("demuxer-cache-state"));
        }

        Assert.NotNull(window);
        double position = double.Parse(mpv.GetPropertyString("time-pos") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(position, window.Value.Start - 1, window.Value.End + 1);
        Assert.True(window.Value.Length > 0);
    }
}
