using System.Diagnostics;
using System.Globalization;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback.Audio;
using Aether.Core.Tests.TestSupport;

namespace Aether.Core.Tests;

/// <summary>The audio suite's filters against the real libmpv: they load, audio keeps flowing, and live commands land.</summary>
public sealed class AudioFiltersMpvTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aether-audio-" + Guid.NewGuid().ToString("N"));

    public AudioFiltersMpvTests() => Directory.CreateDirectory(_dir);

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
    public async Task Equalizer_Loads_And_A_Band_Moves_While_It_Plays()
    {
        using MpvContext mpv = await Playing(tracks: 1);

        mpv.Command("af", "add", Equalizer.Filter(new EqualizerCurve(-3, [6, 5, 4, 2, 0, 0, 0, 0, 0, 0])));
        await AssertStillPlaying(mpv);
        mpv.Command("af-command", Equalizer.FilterLabel, "g", "9.5", "equalizer@b5");
        mpv.Command("af-command", Equalizer.FilterLabel, "volume", "-6dB", "volume@pre");

        await AssertStillPlaying(mpv);
        Assert.Contains("@" + Equalizer.FilterLabel, mpv.GetPropertyString("af"));
    }

    [MpvFact]
    public async Task Night_Mode_Loads_And_Audio_Keeps_Flowing()
    {
        using MpvContext mpv = await Playing(tracks: 1);

        mpv.Command("af", "add", NightMode.Filter);

        await AssertStillPlaying(mpv);
        Assert.Contains("@" + NightMode.FilterLabel, mpv.GetPropertyString("af"));
    }

    private async Task<MpvContext> Playing(int tracks)
    {
        if (!NativeTestEnvironment.IsFfmpegAvailable(out string? ffmpeg))
        {
            throw new InvalidOperationException("ffmpeg is needed to make the test clip");
        }
        string clip = Path.Combine(_dir, $"tone-{tracks}.mkv");
        var psi = new ProcessStartInfo(ffmpeg!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-v", "error", "-y" })
        {
            psi.ArgumentList.Add(arg);
        }
        for (int i = 0; i < tracks; i++)
        {
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("lavfi");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add($"sine=f={220 * (i + 1)}:d=30");
        }
        for (int i = 0; i < tracks; i++)
        {
            psi.ArgumentList.Add("-map");
            psi.ArgumentList.Add(i.ToString(CultureInfo.InvariantCulture));
        }
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add(clip);
        using (Process process = Process.Start(psi)!)
        {
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }

        MpvContext mpv = MpvContext.Create(new MpvContextOptions { HardwareDecoder = "no", AdditionalOptions = [new("vo", "null"), new("ao", "null")] });
        mpv.Command("loadfile", clip);
        await AssertStillPlaying(mpv);
        return mpv;
    }

    /// <summary>The clock moves and audio reaches the output, which a filter that failed to start would stop.</summary>
    private static async Task AssertStillPlaying(MpvContext mpv)
    {
        double start = Position(mpv);
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(50);
            if (Position(mpv) > start + 0.3 && !string.IsNullOrEmpty(mpv.GetPropertyString("audio-out-params")))
            {
                return;
            }
        }
        Assert.Fail($"playback stalled at {Position(mpv)} s, audio-out-params '{mpv.GetPropertyString("audio-out-params")}'");
    }

    private static double Position(MpvContext mpv) =>
        double.TryParse(mpv.GetPropertyString("time-pos"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0;
}
