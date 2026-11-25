using System;
using System.IO;
using Aether.Core.Playback;
using Aether.Core.Playback.Diagnostics;
using Aether.Core.Playback.Settings;
using Xunit;

namespace Aether.Core.Tests;

public class VisualAuditTests
{
    [Fact]
    public void TryParse_WithoutAuditFlag_ReturnsNull()
    {
        Assert.Null(VisualAuditOptions.TryParse([]));
        Assert.Null(VisualAuditOptions.TryParse(["movie.mp4", "--video", "clip.mp4"]));
        Assert.Null(VisualAuditOptions.TryParse(["--visual-audit"]));
        Assert.Null(VisualAuditOptions.TryParse(["--visual-audit", "--video", "clip.mp4"]));
    }

    [Fact]
    public void TryParse_SpaceSeparatedFlags_ParsesOutputVideoAndShots()
    {
        VisualAuditOptions? parsed = VisualAuditOptions.TryParse([
            "--visual-audit", @"C:\temp\audit-out",
            "--video", @"C:\temp\sample.mp4",
            "--shots", "01-home-dark, 14-player-tracks-flyout ,16-player-clips-drawer",
        ]);

        Assert.NotNull(parsed);
        Assert.Equal(Path.GetFullPath(@"C:\temp\audit-out"), parsed.OutputDirectory);
        Assert.Equal(Path.GetFullPath(@"C:\temp\sample.mp4"), parsed.VideoPath);
        Assert.True(parsed.MatchesShot("01-home-dark"));
        Assert.True(parsed.MatchesShot("14-PLAYER-TRACKS-FLYOUt"));
        Assert.True(parsed.MatchesShot("16-player-clips-drawer"));
        Assert.False(parsed.MatchesShot("02-home-oled"));
    }

    [Fact]
    public void TryParse_EqualsSyntaxAndNoShotsFilter_MatchesAllShots()
    {
        VisualAuditOptions? parsed = VisualAuditOptions.TryParse([
            @"--visual-audit=""C:\temp\audit-eq""",
            @"--video=""C:\temp\clip.mkv""",
        ]);

        Assert.NotNull(parsed);
        Assert.Equal(Path.GetFullPath(@"C:\temp\audit-eq"), parsed.OutputDirectory);
        Assert.Equal(Path.GetFullPath(@"C:\temp\clip.mkv"), parsed.VideoPath);
        Assert.Null(parsed.ShotFilter);
        Assert.True(parsed.MatchesShot("anything"));
    }

    [Fact]
    public void InMemorySettingsStorage_IsolatesSettingsStoreFromDisk()
    {
        var storage = new InMemorySettingsStorage();
        var store = new SettingsStore(storage);
        store.Load();
        store.Set(SettingKeys.Theme, ThemeModes.Oled);
        store.Save();

        Assert.NotNull(storage.Read());

        var reloaded = new SettingsStore(storage);
        reloaded.Load();
        Assert.Equal(ThemeModes.Oled, reloaded.Current.Theme);
    }

    [Fact]
    public void BgraCompositor_BlitOpaque_CopiesAndScalesWithClipping()
    {
        byte[] dst = BgraCompositor.CreateCanvas(4, 4, 10, 20, 30);
        // 2x2 red source with alpha 0 (simulating Ignore-alpha swap chain)
        byte[] src = BgraCompositor.CreateCanvas(2, 2, 200, 40, 20, a: 0);

        BgraCompositor.BlitOpaque(dst, 4, 4, src, 2, 2, offsetX: 1, offsetY: 1, targetWidth: 2, targetHeight: 2);

        // Corner (0,0) untouched
        Assert.Equal(30, dst[0]);
        Assert.Equal(20, dst[1]);
        Assert.Equal(10, dst[2]);
        Assert.Equal(255, dst[3]);

        // Inside (1,1) overwritten with opaque alpha
        int p = (1 * 4 + 1) * 4;
        Assert.Equal(20, dst[p]);
        Assert.Equal(40, dst[p + 1]);
        Assert.Equal(200, dst[p + 2]);
        Assert.Equal(255, dst[p + 3]);

        // Resampled blit partially out of bounds clips safely
        byte[] blue = BgraCompositor.CreateCanvas(1, 1, 0, 0, 250, a: 0);
        BgraCompositor.BlitOpaque(dst, 4, 4, blue, 1, 1, offsetX: 3, offsetY: 3, targetWidth: 3, targetHeight: 3);
        int corner = (3 * 4 + 3) * 4;
        Assert.Equal(250, dst[corner]);
        Assert.Equal(0, dst[corner + 1]);
        Assert.Equal(0, dst[corner + 2]);
        Assert.Equal(255, dst[corner + 3]);
    }

    [Fact]
    public void BgraCompositor_BlendPremultipliedOver_ComputesSourceOverCorrectly()
    {
        // Opaque white destination
        byte[] dst = BgraCompositor.CreateCanvas(2, 2, 200, 100, 50);

        // 1x1 premultiplied 50% overlay: straight (0, 200, 100) at alpha 128 => premultiplied ~(0, 100, 50)
        byte[] overlay = [50, 100, 0, 128];
        BgraCompositor.BlendPremultipliedOver(dst, 2, 2, overlay, 1, 1, offsetX: 0, offsetY: 0);

        // out = src + dst * (255 - 128) / 255
        int expectedB = 50 + ((50 * 127 + 127) / 255);
        int expectedG = 100 + ((100 * 127 + 127) / 255);
        int expectedR = 0 + ((200 * 127 + 127) / 255);
        Assert.Equal(expectedB, dst[0]);
        Assert.Equal(expectedG, dst[1]);
        Assert.Equal(expectedR, dst[2]);
        Assert.Equal(255, dst[3]);

        // Pixel (1,0) remains untouched
        Assert.Equal(50, dst[4]);
        Assert.Equal(100, dst[5]);
        Assert.Equal(200, dst[6]);
    }

    [Fact]
    public void BgraCompositor_AnalyzePixels_DistinguishesBlankFromRealContent()
    {
        byte[] solidBlack = BgraCompositor.CreateCanvas(32, 32, 0, 0, 0);
        VisualImageStats blackStats = BgraCompositor.AnalyzePixels(solidBlack, 32, 32);
        Assert.True(blackStats.IsLikelyBlank);
        Assert.Equal(0, blackStats.NonBlackRatio);

        byte[] solidGray = BgraCompositor.CreateCanvas(32, 32, 32, 32, 32);
        VisualImageStats grayStats = BgraCompositor.AnalyzePixels(solidGray, 32, 32);
        Assert.True(grayStats.IsLikelyBlank);
        Assert.Equal(1, grayStats.DistinctSampledColors);

        // Multi-colored synthetic frame
        byte[] scene = BgraCompositor.CreateCanvas(32, 32, 16, 20, 24);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                int i = (y * 32 + x) * 4;
                scene[i] = (byte)(x * 7);
                scene[i + 1] = (byte)(y * 7);
                scene[i + 2] = (byte)((x + y) * 3);
                scene[i + 3] = 255;
            }
        }
        VisualImageStats sceneStats = BgraCompositor.AnalyzePixels(scene, 32, 32);
        Assert.False(sceneStats.IsLikelyBlank);
        Assert.True(sceneStats.DistinctSampledColors > 10);
        Assert.True(sceneStats.MeanLuma > 20);
    }
}
