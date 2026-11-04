using System.Diagnostics;
using System.Globalization;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback.Tracks;
using Aether.Core.Tests.TestSupport;

namespace Aether.Core.Tests;

/// <summary>The subtitle commands against the real libmpv, whose flags and track list they depend on.</summary>
public sealed class SubtitlesMpvTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aether-subs-" + Guid.NewGuid().ToString("N"));

    public SubtitlesMpvTests() => Directory.CreateDirectory(_dir);

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
    public async Task Found_Files_Load_With_Only_The_Preferred_Language_On()
    {
        using MpvContext mpv = await Playing("Film.mkv", "Film.ja.srt", "Film.en.srt", "Film.de.srt");
        IReadOnlyList<SubtitleFile> found = SubtitleFiles.Match(
            Path.Combine(_dir, "Film.mkv"), Directory.GetFiles(_dir), [Path.Combine(_dir, "Film.mkv")]);

        foreach (string[] command in SubtitleFiles.Commands(found, "en", select: true))
        {
            mpv.Command(command);
        }
        TrackList tracks = await Tracks(mpv, count: 3);

        Assert.Equal(3, tracks.Subtitles.Count);
        Assert.EndsWith("Film.en.srt", tracks.SelectedSubtitle?.ExternalFile, StringComparison.Ordinal);
        Assert.Single(tracks.Subtitles, t => t.Selected);
    }

    [MpvFact]
    public async Task A_Secondary_Subtitle_Reads_Back_As_Secondary_And_Restores()
    {
        using MpvContext mpv = await Playing("Film.mkv", "Film.en.srt", "Film.ja.srt");
        mpv.Command("sub-add", Path.Combine(_dir, "Film.en.srt"), "select");
        mpv.Command("sub-add", Path.Combine(_dir, "Film.ja.srt"), "auto");
        TrackList loaded = await Tracks(mpv, count: 2);
        int japanese = loaded.Subtitles.Single(t => t.ExternalFile!.EndsWith("ja.srt", StringComparison.Ordinal)).Id;

        mpv.SetProperty("secondary-sid", japanese.ToString(CultureInfo.InvariantCulture));
        TrackList both = await Tracks(mpv, count: 2, until: list => list.SecondarySubtitle is not null);

        Assert.Equal(japanese, both.SecondarySubtitle?.Id);
        Assert.EndsWith("en.srt", both.SelectedSubtitle?.ExternalFile, StringComparison.Ordinal);

        TrackChoices picked = TrackChoices.From(new TrackSnapshot(both, 0, 0));
        mpv.SetProperty("secondary-sid", "no");
        TrackList cleared = await Tracks(mpv, count: 2, until: list => list.SecondarySubtitle is null);
        foreach (string[] command in picked.Select(new TrackSnapshot(cleared, 0, 0)))
        {
            mpv.Command(command);
        }
        Assert.Equal(japanese, (await Tracks(mpv, count: 2, until: list => list.SecondarySubtitle is not null)).SecondarySubtitle?.Id);
    }

    private async Task<MpvContext> Playing(string video, params string[] subtitles)
    {
        if (!NativeTestEnvironment.IsFfmpegAvailable(out string? ffmpeg))
        {
            throw new InvalidOperationException("ffmpeg is needed to make the test clip");
        }
        string clip = Path.Combine(_dir, video);
        var psi = new ProcessStartInfo(ffmpeg!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc=duration=20:size=160x120:rate=10", "-c:v", "mpeg4", clip })
        {
            psi.ArgumentList.Add(arg);
        }
        using (Process process = Process.Start(psi)!)
        {
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        foreach (string name in subtitles)
        {
            await File.WriteAllTextAsync(Path.Combine(_dir, name), $"1\n00:00:00,500 --> 00:00:19,000\n{name}\n");
        }

        MpvContext mpv = MpvContext.Create(new MpvContextOptions
        {
            HardwareDecoder = "no",
            AdditionalOptions = [new("vo", "null"), new("ao", "null"), new("sub-auto", "no")],
        });
        mpv.Command("loadfile", clip);
        for (int i = 0; i < 60 && mpv.GetPropertyString("duration") is null; i++)
        {
            await Task.Delay(50);
        }
        return mpv;
    }

    private static async Task<TrackList> Tracks(MpvContext mpv, int count, Func<TrackList, bool>? until = null)
    {
        TrackList list = TrackList.Empty;
        for (int i = 0; i < 60; i++)
        {
            list = TrackList.Parse(mpv.GetPropertyString("track-list"));
            if (list.Subtitles.Count >= count && (until?.Invoke(list) ?? true))
            {
                return list;
            }
            await Task.Delay(50);
        }
        return list;
    }
}
