using System.Collections.Concurrent;
using System.Diagnostics;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback.Thumbnails;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public sealed class LocalThumbnailDecoderMpvTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aether-thumbs-" + Guid.NewGuid().ToString("N"));

    public LocalThumbnailDecoderMpvTests() => Directory.CreateDirectory(_dir);

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
    public async Task SoftwareRenderer_Decodes_Distinct_Frames_Across_Seeks_With_Zero_Leaks()
    {
        if (!NativeTestEnvironment.IsFfmpegAvailable(out string? ffmpeg))
        {
            return;
        }

        string clip = CreateSampleClip(ffmpeg!);
        long blocksBefore = MpvRenderParamBlock.LiveBlocks;

        var decoded = new ConcurrentDictionary<int, ThumbnailFrame>();
        var firstReady = new TaskCompletionSource<ThumbnailFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReady = new TaskCompletionSource<ThumbnailFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var decoder = new LocalThumbnailDecoder((path, bucket, frame) =>
        {
            decoded[bucket] = frame;
            if (bucket == 0)
            {
                firstReady.TrySetResult(frame);
            }
            else if (bucket == 3)
            {
                secondReady.TrySetResult(frame);
            }
        }))
        {
            decoder.Request(clip, bucket: 0, targetSeconds: 0.2);
            ThumbnailFrame frame1 = await firstReady.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.InRange(frame1.Width, 64, ThumbnailTiming.DefaultMaxWidth);
            Assert.InRange(frame1.Height, 36, ThumbnailTiming.DefaultMaxHeight);
            Assert.Equal(frame1.Width * frame1.Height * 4, frame1.BgraPixels.Length);
            Assert.Equal(0xFF, frame1.BgraPixels[3]);
            Assert.True(decoder.TryGetCached(clip, 0, out ThumbnailFrame cached1));
            Assert.Same(frame1, cached1);

            decoder.Request(clip, bucket: 3, targetSeconds: 3.0);
            ThumbnailFrame frame2 = await secondReady.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(frame1.Width, frame2.Width);
            Assert.Equal(frame1.Height, frame2.Height);
            Assert.Equal(0xFF, frame2.BgraPixels[3]);
            Assert.False(frame1.BgraPixels.AsSpan().SequenceEqual(frame2.BgraPixels));

            decoder.Reset();
        }

        Assert.True(MpvContext.WaitForDestroyedCores(5000));
        Assert.Equal(blocksBefore, MpvRenderParamBlock.LiveBlocks);
    }

    private string CreateSampleClip(string ffmpeg)
    {
        string clip = Path.Combine(_dir, "sample.mp4");
        var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[]
        {
            "-v", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30:duration=4",
            "-c:v", "libx264", "-g", "15", "-pix_fmt", "yuv420p",
            clip,
        })
        {
            psi.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(psi)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return clip;
    }
}
