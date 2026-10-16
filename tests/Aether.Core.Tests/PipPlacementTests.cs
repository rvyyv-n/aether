using System;
using System.Collections.Generic;
using Aether.Core.Playback.Windowing;
using Xunit;

namespace Aether.Core.Tests;

public class PipPlacementTests
{
    [Fact]
    public void PictureSize_SixteenToNine_ReturnsDefaultDimensions()
    {
        (double width, double height) = PipPlacement.PictureSize(16.0 / 9.0);

        Assert.Equal(400.0, width, 0.01);
        Assert.Equal(225.0, height, 0.01);
    }

    [Fact]
    public void PictureSize_FourToThree_KeepsDefaultAreaAndAspect()
    {
        (double width, double height) = PipPlacement.PictureSize(4.0 / 3.0);

        Assert.Equal(400.0 * 225.0, width * height, 0.01);
        Assert.Equal(4.0 / 3.0, width / height, 0.001);
    }

    [Fact]
    public void PictureSize_NineToSixteen_KeepsDefaultAreaAndAspect()
    {
        // 9:16 portrait video should keep the same area (400*225 = 90000) and 9:16 aspect.
        // NOTE: If production PipPlacement enforces MinWidth (256) on portrait width, it grows to 256x455.11 (area 116508.44).
        (double width, double height) = PipPlacement.PictureSize(9.0 / 16.0);

        Assert.Equal(9.0 / 16.0, width / height, 0.001);
        Assert.Equal(400.0 * 225.0, width * height, 0.01);
    }

    [Fact]
    public void PictureSize_VeryWideAspect_GrownToAtLeastMinimumDimensionsWhileKeepingAspect()
    {
        // Ultra-wide 10:1 aspect
        (double width, double height) = PipPlacement.PictureSize(10.0);

        Assert.True(width >= PipPlacement.MinWidth);
        Assert.True(height >= PipPlacement.MinHeight);
        Assert.Equal(10.0, width / height, 0.001);
    }

    [Fact]
    public void PictureSize_VeryTallAspect_GrownToAtLeastMinimumDimensionsWhileKeepingAspect()
    {
        // Very tall 1:10 aspect
        (double width, double height) = PipPlacement.PictureSize(0.1);

        Assert.True(width >= PipPlacement.MinWidth);
        Assert.True(height >= PipPlacement.MinHeight);
        Assert.Equal(0.1, width / height, 0.001);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void PictureSize_NonPositiveOrNonFiniteAspect_FallsBackToSixteenNine(double aspect)
    {
        (double width, double height) = PipPlacement.PictureSize(aspect);

        Assert.Equal(400.0, width, 0.01);
        Assert.Equal(225.0, height, 0.01);
    }

    [Fact]
    public void Initial_ScaleOne_PositionsVisiblePictureInsideWorkAreaEdges()
    {
        var workArea = new WindowRect(0, 0, 1600, 900);
        int frameWidth = 16;
        int frameHeight = 8;

        WindowRect rect = PipPlacement.Initial(workArea, 16.0 / 9.0, scale: 1.0, frameWidth, frameHeight);

        Assert.Equal(400 + frameWidth, rect.Width);
        Assert.Equal(225 + frameHeight, rect.Height);

        int visibleRight = rect.X + rect.Width - frameWidth / 2;
        int visibleBottom = rect.Y + rect.Height - frameHeight;

        Assert.Equal(workArea.Right - (int)PipPlacement.EdgeMargin, visibleRight);
        Assert.Equal(workArea.Bottom - (int)PipPlacement.EdgeMargin, visibleBottom);
    }

    [Fact]
    public void Initial_ScaleOnePointFive_ScalesSizesAndMargin()
    {
        var workArea = new WindowRect(0, 0, 1600, 900);
        int frameWidth = 16;
        int frameHeight = 8;
        double scale = 1.5;

        WindowRect rect = PipPlacement.Initial(workArea, 16.0 / 9.0, scale, frameWidth, frameHeight);

        int expectedPictureWidth = (int)Math.Round(400 * scale);
        int expectedPictureHeight = (int)Math.Round(225 * scale);
        Assert.Equal(expectedPictureWidth + frameWidth, rect.Width);
        Assert.Equal(expectedPictureHeight + frameHeight, rect.Height);

        int visibleRight = rect.X + rect.Width - frameWidth / 2;
        int visibleBottom = rect.Y + rect.Height - frameHeight;
        int expectedMargin = (int)Math.Round(PipPlacement.EdgeMargin * scale);

        Assert.Equal(workArea.Right - expectedMargin, visibleRight);
        Assert.Equal(workArea.Bottom - expectedMargin, visibleBottom);
    }

    [Fact]
    public void Initial_NonZeroOrigin_RespectsWorkAreaOrigin()
    {
        var workArea = new WindowRect(1600, 0, 1600, 900);
        int frameWidth = 16;
        int frameHeight = 8;

        WindowRect rect = PipPlacement.Initial(workArea, 16.0 / 9.0, scale: 1.0, frameWidth, frameHeight);

        Assert.True(rect.X >= workArea.X);
        Assert.Equal(400 + frameWidth, rect.Width);
        Assert.Equal(225 + frameHeight, rect.Height);

        int visibleRight = rect.X + rect.Width - frameWidth / 2;
        int visibleBottom = rect.Y + rect.Height - frameHeight;

        Assert.Equal(workArea.Right - (int)PipPlacement.EdgeMargin, visibleRight);
        Assert.Equal(workArea.Bottom - (int)PipPlacement.EdgeMargin, visibleBottom);
    }

    [Fact]
    public void Remembered_UnchangedAspect_PreservesCentreAndPictureArea()
    {
        var primary = new WindowRect(0, 0, 1920, 1080);
        int frameWidth = 16;
        int frameHeight = 8;
        var saved = new WindowRect(500, 300, 400 + frameWidth, 225 + frameHeight);

        WindowRect result = PipPlacement.Remembered(saved, 16.0 / 9.0, 1.0, frameWidth, frameHeight, [primary], primary);

        int savedCentreX = saved.X + saved.Width / 2;
        int savedCentreY = saved.Y + saved.Height / 2;
        int resultCentreX = result.X + result.Width / 2;
        int resultCentreY = result.Y + result.Height / 2;

        Assert.Equal(savedCentreX, resultCentreX);
        Assert.Equal(savedCentreY, resultCentreY);

        int savedPictureArea = (saved.Width - frameWidth) * (saved.Height - frameHeight);
        int resultPictureArea = (result.Width - frameWidth) * (result.Height - frameHeight);
        Assert.Equal(savedPictureArea, resultPictureArea);
    }

    [Fact]
    public void Remembered_NewAspect_KeepsPictureAreaAndCentre()
    {
        var primary = new WindowRect(0, 0, 1920, 1080);
        int frameWidth = 16;
        int frameHeight = 8;
        var saved = new WindowRect(500, 300, 400 + frameWidth, 225 + frameHeight);

        WindowRect result = PipPlacement.Remembered(saved, 4.0 / 3.0, 1.0, frameWidth, frameHeight, [primary], primary);

        int savedCentreX = saved.X + saved.Width / 2;
        int savedCentreY = saved.Y + saved.Height / 2;
        int resultCentreX = result.X + result.Width / 2;
        int resultCentreY = result.Y + result.Height / 2;

        // Centered within integer division tolerance
        Assert.InRange(Math.Abs(resultCentreX - savedCentreX), 0, 1);
        Assert.InRange(Math.Abs(resultCentreY - savedCentreY), 0, 1);

        int savedPictureArea = (saved.Width - frameWidth) * (saved.Height - frameHeight);
        int resultPictureArea = (result.Width - frameWidth) * (result.Height - frameHeight);
        // Area is preserved within pixel rounding (e.g. 346 * 260 = 89960 vs 90000)
        Assert.InRange(Math.Abs(resultPictureArea - savedPictureArea), 0, 100);
    }

    [Fact]
    public void Remembered_MonitorNoLongerExists_CentredOnPrimaryWorkArea()
    {
        var primary = new WindowRect(0, 0, 1920, 1080);
        int frameWidth = 16;
        int frameHeight = 8;
        // Saved on a disconnected monitor at X=2500
        var savedOnDisconnectedMonitor = new WindowRect(2500, 300, 400 + frameWidth, 225 + frameHeight);

        WindowRect result = PipPlacement.Remembered(savedOnDisconnectedMonitor, 16.0 / 9.0, 1.0, frameWidth, frameHeight, [primary], primary);

        int expectedX = primary.X + (primary.Width - (400 + frameWidth)) / 2;
        int expectedY = primary.Y + (primary.Height - (225 + frameHeight)) / 2;

        Assert.Equal(new WindowRect(expectedX, expectedY, 400 + frameWidth, 225 + frameHeight), result);
    }

    [Fact]
    public void Remembered_HalfOffEdge_IsPulledFullyInside()
    {
        var primary = new WindowRect(0, 0, 1920, 1080);
        int frameWidth = 16;
        int frameHeight = 8;
        // Hanging off the right/bottom edge
        var savedHanging = new WindowRect(1800, 1000, 400 + frameWidth, 225 + frameHeight);

        WindowRect result = PipPlacement.Remembered(savedHanging, 16.0 / 9.0, 1.0, frameWidth, frameHeight, [primary], primary);

        Assert.True(result.X >= primary.X);
        Assert.True(result.Right <= primary.Right);
        Assert.True(result.Y >= primary.Y);
        Assert.True(result.Bottom <= primary.Bottom);
        Assert.Equal(400 + frameWidth, result.Width);
        Assert.Equal(225 + frameHeight, result.Height);
    }
}
