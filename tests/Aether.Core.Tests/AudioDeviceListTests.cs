using System.Diagnostics;
using System.Globalization;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback.Audio;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public sealed class AudioDeviceListTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aether-audiodev-" + Guid.NewGuid().ToString("N"));

    public AudioDeviceListTests() => Directory.CreateDirectory(_dir);

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

    [Fact]
    public void Parse_ValidAndMalformedJson_HandlesDeduplicationAndFallbacks()
    {
        const string json = """
            [
              { "name": "auto", "description": "Autoselect device" },
              { "name": "wasapi/{111}", "description": "Speakers (Realtek)" },
              { "name": "wasapi/{111}", "description": "Duplicate Ignored" },
              { "name": "wasapi/{222}", "description": "" },
              { "description": "Missing Name Ignored" }
            ]
            """;

        AudioDeviceList parsed = AudioDeviceList.Parse(json);

        Assert.Equal(3, parsed.Devices.Count);
        Assert.Equal("auto", parsed.Devices[0].Name);
        Assert.Equal("wasapi/{111}", parsed.Devices[1].Name);
        Assert.Equal("Speakers (Realtek)", parsed.Devices[1].Description);
        Assert.Equal("wasapi/{222}", parsed.Devices[2].Name);
        Assert.Equal("wasapi/{222}", parsed.Devices[2].Description);

        Assert.Empty(AudioDeviceList.Parse(null).Devices);
        Assert.Empty(AudioDeviceList.Parse("not-json").Devices);
        Assert.Empty(AudioDeviceList.Parse("{}").Devices);
    }

    [Theory]
    [InlineData(null, "", "auto")]
    [InlineData("", "", "auto")]
    [InlineData("   ", "", "auto")]
    [InlineData("auto", "", "auto")]
    [InlineData("AUTO", "", "auto")]
    [InlineData("wasapi/{abc}", "wasapi/{abc}", "wasapi/{abc}")]
    public void SettingValue_And_MpvValue_RoundTripCleanly(string? input, string expectedSetting, string expectedMpv)
    {
        Assert.Equal(expectedSetting, AudioDeviceList.NormalizeSettingValue(input));
        Assert.Equal(expectedMpv, AudioDeviceList.ToMpvValue(expectedSetting));
    }

    [Fact]
    public void BuildOptions_OmitsDuplicateAuto_AndMarksUnavailableSavedDevice()
    {
        var list = new AudioDeviceList(
        [
            new("auto", "Autoselect device"),
            new("wasapi/{111}", "Speakers"),
        ]);

        IReadOnlyList<AudioDeviceOption> connected = list.BuildOptions(
            "wasapi/{111}",
            "System default",
            "{0} (unavailable)");

        Assert.Equal(2, connected.Count);
        Assert.Equal(new AudioDeviceOption("", "System default", true), connected[0]);
        Assert.Equal(new AudioDeviceOption("wasapi/{111}", "Speakers", true), connected[1]);

        IReadOnlyList<AudioDeviceOption> unplugged = list.BuildOptions(
            "wasapi/{dead}",
            "System default",
            "{0} (unavailable)");

        Assert.Equal(3, unplugged.Count);
        Assert.Equal(new AudioDeviceOption("wasapi/{dead}", "wasapi/{dead} (unavailable)", false), unplugged[2]);
    }

    [MpvFact]
    public async Task LiveMpv_Enumerates_AudioDeviceList_And_Sets_AudioDevice_Cleanly()
    {
        using var mpv = MpvContext.Create(new MpvContextOptions
        {
            HardwareDecoder = "no",
            AdditionalOptions = [new("vo", "null"), new("volume", "0")],
        });

        AudioDeviceList devices = AudioDeviceList.Read(mpv);
        Assert.Contains(devices.Devices, d => string.Equals(d.Name, "auto", StringComparison.OrdinalIgnoreCase));

        mpv.SetProperty("audio-device", AudioDeviceList.ToMpvValue(devices.Devices[0].Name));
        mpv.SetProperty("audio-device", AudioDeviceList.ToMpvValue(""));

        if (!NativeTestEnvironment.IsFfmpegAvailable(out string? ffmpeg))
        {
            return;
        }

        string clip = Path.Combine(_dir, "tone.mkv");
        var psi = new ProcessStartInfo(ffmpeg!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-v", "error", "-y", "-f", "lavfi", "-i", "sine=f=440:d=10", "-c:a", "aac", clip })
        {
            psi.ArgumentList.Add(arg);
        }
        using (Process p = Process.Start(psi)!)
        {
            await p.WaitForExitAsync();
            Assert.Equal(0, p.ExitCode);
        }

        mpv.Command("loadfile", clip);
        await WaitForProgressAsync(mpv, minDelta: 0.15);

        mpv.SetProperty("audio-device", AudioDeviceList.ToMpvValue(devices.Devices[0].Name));
        mpv.SetProperty("audio-device", AudioDeviceList.ToMpvValue(""));

        await WaitForProgressAsync(mpv, minDelta: 0.15);
    }

    private static async Task WaitForProgressAsync(MpvContext mpv, double minDelta)
    {
        double start = Position(mpv);
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(50);
            if (Position(mpv) > start + minDelta)
            {
                return;
            }
        }
        Assert.Fail($"Playback stalled at {Position(mpv)} s.");
    }

    private static double Position(MpvContext mpv) =>
        double.TryParse(mpv.GetPropertyString("time-pos"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) ? t : 0;
}
