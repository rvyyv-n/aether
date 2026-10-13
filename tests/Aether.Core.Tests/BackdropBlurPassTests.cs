using System;
using System.Diagnostics;
using Aether.Core.Interop.Angle;
using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.Rendering;
using Aether.Core.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace Aether.Core.Tests;

/// <summary>The blur pass on a real ANGLE context (skipped where ANGLE is missing).</summary>
public sealed unsafe class BackdropBlurPassTests(ITestOutputHelper output)
{
    private const uint GL_SCISSOR_TEST = 0x0C11;
    private const uint GL_BLEND = 0x0BE2;
    private const uint GL_COLOR_BUFFER_BIT = 0x4000;
    private const uint GL_VIEWPORT = 0x0BA2;
    private const uint GL_RGBA = 0x1908;
    private const uint GL_UNSIGNED_BYTE = 0x1401;
    private const uint GL_TEXTURE_2D = 0x0DE1;
    private const uint GL_TEXTURE_BINDING_2D = 0x8069;
    private const uint GL_CURRENT_PROGRAM = 0x8B8D;
    private const uint GL_DRAW_FRAMEBUFFER_BINDING = 0x8CA6;
    private const uint GL_TEXTURE1 = 0x84C1;
    private const uint GL_ACTIVE_TEXTURE = 0x84E0;
    private const uint GL_BLEND_SRC_RGB = 0x80C9;

    private const uint Width = 640, Height = 360;

    private sealed class Rig : IDisposable
    {
        public uint W, H;
        public D3D11DeviceManager Device = null!;
        public DxgiPresenter Presenter = null!;
        public AngleBridge Bridge = null!;
        public BackdropBlurPass Pass = null!;

        public delegate* unmanaged[Stdcall]<uint, int*, void> GetIntegerv;
        public delegate* unmanaged[Stdcall]<uint, byte> IsEnabled;
        public delegate* unmanaged[Stdcall]<uint, void> Enable;
        public delegate* unmanaged[Stdcall]<uint, void> Disable;
        public delegate* unmanaged[Stdcall]<int, int, int, int, void> Scissor;
        public delegate* unmanaged[Stdcall]<int, int, int, int, void> Viewport;
        public delegate* unmanaged[Stdcall]<float, float, float, float, void> ClearColor;
        public delegate* unmanaged[Stdcall]<uint, void> Clear;
        public delegate* unmanaged[Stdcall]<int, int, int, int, uint, uint, void*, void> ReadPixels;
        public delegate* unmanaged[Stdcall]<int, uint*, void> GenTextures;
        public delegate* unmanaged[Stdcall]<uint, uint, void> BindTexture;
        public delegate* unmanaged[Stdcall]<uint, void> ActiveTexture;
        public delegate* unmanaged[Stdcall]<void> Finish;

        public static Rig Create(uint width = Width, uint height = Height)
        {
            var rig = new Rig { W = width, H = height };
            rig.Device = D3D11DeviceManager.Create();
            rig.Presenter = new DxgiPresenter(rig.Device, new DxgiPresenterOptions { Width = width, Height = height, ClearStateOnResize = false });
            rig.Bridge = AngleBridge.Create(rig.Device, rig.Presenter);
            rig.GetIntegerv = (delegate* unmanaged[Stdcall]<uint, int*, void>)rig.Bridge.GlProc("glGetIntegerv"u8);
            rig.IsEnabled = (delegate* unmanaged[Stdcall]<uint, byte>)rig.Bridge.GlProc("glIsEnabled"u8);
            rig.Enable = (delegate* unmanaged[Stdcall]<uint, void>)rig.Bridge.GlProc("glEnable"u8);
            rig.Disable = (delegate* unmanaged[Stdcall]<uint, void>)rig.Bridge.GlProc("glDisable"u8);
            rig.Scissor = (delegate* unmanaged[Stdcall]<int, int, int, int, void>)rig.Bridge.GlProc("glScissor"u8);
            rig.Viewport = (delegate* unmanaged[Stdcall]<int, int, int, int, void>)rig.Bridge.GlProc("glViewport"u8);
            rig.ClearColor = (delegate* unmanaged[Stdcall]<float, float, float, float, void>)rig.Bridge.GlProc("glClearColor"u8);
            rig.Clear = (delegate* unmanaged[Stdcall]<uint, void>)rig.Bridge.GlProc("glClear"u8);
            rig.ReadPixels = (delegate* unmanaged[Stdcall]<int, int, int, int, uint, uint, void*, void>)rig.Bridge.GlProc("glReadPixels"u8);
            rig.GenTextures = (delegate* unmanaged[Stdcall]<int, uint*, void>)rig.Bridge.GlProc("glGenTextures"u8);
            rig.BindTexture = (delegate* unmanaged[Stdcall]<uint, uint, void>)rig.Bridge.GlProc("glBindTexture"u8);
            rig.ActiveTexture = (delegate* unmanaged[Stdcall]<uint, void>)rig.Bridge.GlProc("glActiveTexture"u8);
            rig.Finish = (delegate* unmanaged[Stdcall]<void>)rig.Bridge.GlProc("glFinish"u8);
            rig.Pass = BackdropBlurPass.Create(rig.Bridge)!;
            return rig;
        }

        /// <summary>Left of <paramref name="splitX"/> white, right of it black.</summary>
        public void FillSplit(int splitX)
        {
            Enable(GL_SCISSOR_TEST);
            Scissor(0, 0, splitX, (int)H);
            ClearColor(1, 1, 1, 1);
            Clear(GL_COLOR_BUFFER_BIT);
            Scissor(splitX, 0, (int)W - splitX, (int)H);
            ClearColor(0, 0, 0, 1);
            Clear(GL_COLOR_BUFFER_BIT);
            Disable(GL_SCISSOR_TEST);
        }

        /// <summary>Red channel at a UI (top-left origin) pixel.</summary>
        public int Red(int x, int yFromTop)
        {
            byte* pixel = stackalloc byte[4];
            ReadPixels(x, (int)H - 1 - yFromTop, 1, 1, GL_RGBA, GL_UNSIGNED_BYTE, pixel);
            return pixel[0];
        }

        public int GetInt(uint name)
        {
            int value;
            GetIntegerv(name, &value);
            return value;
        }

        public void Dispose()
        {
            Pass?.Dispose();
            Bridge?.Dispose();
            Presenter?.Dispose();
            Device?.Dispose();
        }
    }

    private static BackdropFrame Frame(float opacity = 1f, float x = 220, float y = 250) =>
        new(new BackdropRegion(x, y, 200, 60, 24, 1f, Width, Height), opacity);

    [Fact]
    public void Draw_BlursTheVideoBehindTheCard_AndLeavesTheRestAlone()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        using Rig rig = Rig.Create();
        rig.FillSplit(320);
        Assert.Equal(255, rig.Red(319, 280));
        Assert.Equal(0, rig.Red(320, 280));

        rig.Pass.Draw(Frame(), Width, Height);

        // Inside the card the hard white/black edge is now a ramp.
        int edge = rig.Red(320, 280);
        Assert.InRange(edge, 90, 170);
        Assert.InRange(rig.Red(300, 280), edge + 10, 254);
        Assert.True(rig.Red(340, 280) < edge - 10);
        Assert.True(rig.Red(250, 280) > 240); // far from the edge it is still white

        // Well outside the card and its shadow nothing changes.
        Assert.Equal(255, rig.Red(100, 40));
        Assert.Equal(0, rig.Red(500, 40));
    }

    [Fact]
    public void Draw_ShadowFallsBelowTheCard_NotUnderIt()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        using Rig rig = Rig.Create();
        rig.FillSplit(600); // white across the card
        rig.Pass.Draw(Frame(), Width, Height);

        // The card is rows 250..310 from the top. Shadow is offset 8 down, so below is darker than above.
        int below = rig.Red(320, 318);
        int above = rig.Red(320, 242);
        Assert.True(below < above - 15, $"below {below}, above {above}");
        Assert.True(below < 245);
        Assert.InRange(rig.Red(320, 352), 253, 255); // out of reach

        // The card itself keeps the video's brightness (the tint is XAML's job), so no shadow shows through.
        Assert.InRange(rig.Red(320, 280), 252, 255);
    }

    [Fact]
    public void Draw_CardCornersAreRounded()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        using Rig rig = Rig.Create();
        rig.FillSplit(221); // white up to the card's left edge, black inside it
        rig.Pass.Draw(Frame(), Width, Height);

        int corner = rig.Red(222, 251); // outside the radius-24 curve: untouched black
        int middle = rig.Red(222, 280); // inside the card: blurred towards the white side
        Assert.True(middle > corner + 40, $"corner {corner}, middle {middle}");
    }

    [Fact]
    public void Draw_FadedOut_ChangesNothingAndHalfOpacityIsInBetween()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        using Rig rig = Rig.Create();
        rig.FillSplit(320);
        rig.Pass.Draw(Frame(opacity: 0f), Width, Height);
        Assert.Equal(255, rig.Red(319, 280));
        Assert.Equal(0, rig.Red(320, 280));

        rig.Pass.Draw(Frame(opacity: 0.5f), Width, Height);
        Assert.InRange(rig.Red(319, 280), 160, 254);
    }

    [Fact]
    public void Draw_RestoresEveryBindingItChanges()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        using Rig rig = Rig.Create();
        rig.FillSplit(320);

        uint texture;
        rig.GenTextures(1, &texture);
        rig.ActiveTexture(GL_TEXTURE1);
        rig.BindTexture(GL_TEXTURE_2D, texture);
        rig.Viewport(5, 6, 7, 8);
        rig.Enable(GL_SCISSOR_TEST);
        rig.Scissor(0, 0, 10, 10);
        rig.Enable(GL_BLEND);
        int blendSrc = rig.GetInt(GL_BLEND_SRC_RGB);

        rig.Pass.Draw(Frame(), Width, Height);

        int* viewport = stackalloc int[4];
        rig.GetIntegerv(GL_VIEWPORT, viewport);
        Assert.Equal([5, 6, 7, 8], new[] { viewport[0], viewport[1], viewport[2], viewport[3] });
        Assert.Equal((int)GL_TEXTURE1, rig.GetInt(GL_ACTIVE_TEXTURE));
        Assert.Equal((int)texture, rig.GetInt(GL_TEXTURE_BINDING_2D));
        Assert.True(rig.IsEnabled(GL_SCISSOR_TEST) != 0);
        Assert.True(rig.IsEnabled(GL_BLEND) != 0);
        Assert.Equal(blendSrc, rig.GetInt(GL_BLEND_SRC_RGB));
        Assert.Equal(0, rig.GetInt(GL_CURRENT_PROGRAM));
        Assert.Equal(0, rig.GetInt(GL_DRAW_FRAMEBUFFER_BINDING));
    }

    [Fact]
    public void Draw_NoAllocationPerFrame_AndTimings()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        // The real bar at 1080p: 520 x 84 DIPs, 24 above the bottom.
        const uint W = 1920, H = 1080;
        using Rig rig = Rig.Create(W, H);
        rig.FillSplit(960);
        var frame = new BackdropFrame(new BackdropRegion(700, 972, 520, 84, 24, 1f, W, H), 1f);
        for (int i = 0; i < 10; i++)
        {
            rig.Pass.Draw(frame, W, H);
            rig.Bridge.Flush();
        }

        const int Frames = 200;
        rig.Finish();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Frames; i++)
        {
            rig.Pass.Draw(frame, W, H);
            rig.Bridge.Flush();
        }
        long issued = Stopwatch.GetTimestamp();
        rig.Finish();
        long finished = Stopwatch.GetTimestamp();
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        double cpuMs = (issued - start) * 1000.0 / Stopwatch.Frequency / Frames;
        double totalMs = (finished - start) * 1000.0 / Stopwatch.Frequency / Frames;
        output.WriteLine($"{rig.Device.DriverType}: {cpuMs:F3} ms CPU, {totalMs:F3} ms CPU+GPU per frame (card 520x84 px at {W}x{H})");

        Assert.Equal(0, allocatedAfter - allocated);
        Assert.True(totalMs < 5, $"{totalMs} ms per frame");
    }
}
