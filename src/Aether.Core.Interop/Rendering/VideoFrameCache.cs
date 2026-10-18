using System;
using Aether.Core.Interop.Angle;

namespace Aether.Core.Interop.Rendering;

/// <summary>
/// The last video frame mpv drew, kept in a texture the size of the back buffer. A redraw that has
/// no new video frame (the card's blur fading, the card growing) copies this back instead of
/// asking mpv to render the whole picture again, which at vsync rate cost several times the
/// video's own frame rate in GPU time and made playback stutter while the card moved.
/// <para>
/// Render thread, GL context current. Binds only framebuffers and the unit 0 texture, and puts
/// both back; the caller's blits are not scissored.
/// </para>
/// </summary>
internal sealed unsafe class VideoFrameCache : IDisposable
{
    private const uint GL_READ_FRAMEBUFFER = 0x8CA8;
    private const uint GL_DRAW_FRAMEBUFFER = 0x8CA9;
    private const uint GL_DRAW_FRAMEBUFFER_BINDING = 0x8CA6;
    private const uint GL_READ_FRAMEBUFFER_BINDING = 0x8CAA;
    private const uint GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    private const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
    private const uint GL_COLOR_BUFFER_BIT = 0x4000;
    private const uint GL_TEXTURE_2D = 0x0DE1;
    private const uint GL_TEXTURE_BINDING_2D = 0x8069;
    private const uint GL_TEXTURE0 = 0x84C0;
    private const uint GL_ACTIVE_TEXTURE = 0x84E0;
    private const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    private const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    private const uint GL_SCISSOR_TEST = 0x0C11;
    private const int GL_NEAREST = 0x2600;
    private const uint GL_RGBA = 0x1908;
    private const uint GL_UNSIGNED_BYTE = 0x1401;

    private readonly delegate* unmanaged[Stdcall]<uint, int*, void> _glGetIntegerv;
    private readonly delegate* unmanaged[Stdcall]<uint, byte> _glIsEnabled;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glEnable;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glDisable;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, void> _glBindFramebuffer;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, void> _glBindTexture;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glActiveTexture;
    private readonly delegate* unmanaged[Stdcall]<int, int, int, int, int, int, int, int, uint, uint, void> _glBlitFramebuffer;
    private readonly delegate* unmanaged[Stdcall]<int, uint*, void> _glGenTextures;
    private readonly delegate* unmanaged[Stdcall]<int, uint*, void> _glDeleteTextures;
    private readonly delegate* unmanaged[Stdcall]<int, uint*, void> _glGenFramebuffers;
    private readonly delegate* unmanaged[Stdcall]<int, uint*, void> _glDeleteFramebuffers;
    private readonly delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void> _glTexImage2D;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, int, void> _glTexParameteri;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void> _glFramebufferTexture2D;
    private readonly delegate* unmanaged[Stdcall]<uint, uint> _glCheckFramebufferStatus;

    private uint _texture, _fbo;
    private int _width, _height;
    private int _capacityWidth, _capacityHeight;
    private const int CapacityStep = 128;

    private VideoFrameCache(AngleBridge bridge)
    {
        _glGetIntegerv = (delegate* unmanaged[Stdcall]<uint, int*, void>)Need(bridge, "glGetIntegerv"u8);
        _glIsEnabled = (delegate* unmanaged[Stdcall]<uint, byte>)Need(bridge, "glIsEnabled"u8);
        _glEnable = (delegate* unmanaged[Stdcall]<uint, void>)Need(bridge, "glEnable"u8);
        _glDisable = (delegate* unmanaged[Stdcall]<uint, void>)Need(bridge, "glDisable"u8);
        _glBindFramebuffer = (delegate* unmanaged[Stdcall]<uint, uint, void>)Need(bridge, "glBindFramebuffer"u8);
        _glBindTexture = (delegate* unmanaged[Stdcall]<uint, uint, void>)Need(bridge, "glBindTexture"u8);
        _glActiveTexture = (delegate* unmanaged[Stdcall]<uint, void>)Need(bridge, "glActiveTexture"u8);
        _glBlitFramebuffer = (delegate* unmanaged[Stdcall]<int, int, int, int, int, int, int, int, uint, uint, void>)Need(bridge, "glBlitFramebuffer"u8);
        _glGenTextures = (delegate* unmanaged[Stdcall]<int, uint*, void>)Need(bridge, "glGenTextures"u8);
        _glDeleteTextures = (delegate* unmanaged[Stdcall]<int, uint*, void>)Need(bridge, "glDeleteTextures"u8);
        _glGenFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)Need(bridge, "glGenFramebuffers"u8);
        _glDeleteFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)Need(bridge, "glDeleteFramebuffers"u8);
        _glTexImage2D = (delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void>)Need(bridge, "glTexImage2D"u8);
        _glTexParameteri = (delegate* unmanaged[Stdcall]<uint, uint, int, void>)Need(bridge, "glTexParameteri"u8);
        _glFramebufferTexture2D = (delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void>)Need(bridge, "glFramebufferTexture2D"u8);
        _glCheckFramebufferStatus = (delegate* unmanaged[Stdcall]<uint, uint>)Need(bridge, "glCheckFramebufferStatus"u8);
    }

    /// <summary>Null if ANGLE lacks a needed entry point (ES 2 has no blits); the caller then renders straight to the back buffer.</summary>
    public static VideoFrameCache? Create(AngleBridge bridge)
    {
        try
        {
            VideoFrameCache cache = new(bridge);
            uint* names = stackalloc uint[1];
            cache._glGenTextures(1, names);
            cache._texture = names[0];
            cache._glGenFramebuffers(1, names);
            cache._fbo = names[0];
            return cache;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The framebuffer mpv renders into. Valid after <see cref="EnsureSize"/> returned true.</summary>
    public uint Framebuffer => _fbo;

    /// <summary>
    /// Sizes the texture to the back buffer. <paramref name="reallocated"/> is true when the old picture
    /// is gone and mpv must draw again. False if the framebuffer would not be complete.
    /// </summary>
    public bool EnsureSize(uint width, uint height, out bool reallocated)
    {
        reallocated = false;
        if ((int)width == _width && (int)height == _height)
        {
            return true;
        }
        reallocated = true; // the picture is gone either way: mpv draws at the new size

        // Resizing in steps would reallocate the texture every time; keep it while it is big enough and not wasteful.
        if ((int)width <= _capacityWidth && (int)height <= _capacityHeight
            && _capacityWidth - (int)width < CapacityStep * 2 && _capacityHeight - (int)height < CapacityStep * 2)
        {
            (_width, _height) = ((int)width, (int)height);
            return true;
        }

        int capacityWidth = RoundUp((int)width), capacityHeight = RoundUp((int)height);
        int activeTexture, texture, draw;
        _glGetIntegerv(GL_ACTIVE_TEXTURE, &activeTexture);
        _glActiveTexture(GL_TEXTURE0);
        _glGetIntegerv(GL_TEXTURE_BINDING_2D, &texture);
        _glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &draw);

        _glBindTexture(GL_TEXTURE_2D, _texture);
        _glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA, capacityWidth, capacityHeight, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
        _glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        _glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        _glBindFramebuffer(GL_DRAW_FRAMEBUFFER, _fbo);
        _glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, _texture, 0);
        bool complete = _glCheckFramebufferStatus(GL_DRAW_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE;

        _glBindFramebuffer(GL_DRAW_FRAMEBUFFER, (uint)draw);
        _glBindTexture(GL_TEXTURE_2D, (uint)texture);
        _glActiveTexture((uint)activeTexture);

        (_width, _height, _capacityWidth, _capacityHeight) = complete
            ? ((int)width, (int)height, capacityWidth, capacityHeight)
            : (0, 0, 0, 0);
        return complete;
    }

    private static int RoundUp(int value) => (value + CapacityStep - 1) / CapacityStep * CapacityStep;

    /// <summary>Copies the cached picture 1:1 to the back buffer (framebuffer 0).</summary>
    public void CopyToBackBuffer()
    {
        int read, draw;
        _glGetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &read);
        _glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &draw);
        bool scissor = _glIsEnabled(GL_SCISSOR_TEST) != 0;
        if (scissor)
        {
            _glDisable(GL_SCISSOR_TEST);
        }

        _glBindFramebuffer(GL_READ_FRAMEBUFFER, _fbo);
        _glBindFramebuffer(GL_DRAW_FRAMEBUFFER, 0);
        _glBlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, GL_COLOR_BUFFER_BIT, (uint)GL_NEAREST);

        _glBindFramebuffer(GL_READ_FRAMEBUFFER, (uint)read);
        _glBindFramebuffer(GL_DRAW_FRAMEBUFFER, (uint)draw);
        if (scissor)
        {
            _glEnable(GL_SCISSOR_TEST);
        }
    }

    private static void* Need(AngleBridge bridge, ReadOnlySpan<byte> name)
    {
        void* proc = bridge.GlProc(name);
        return proc != null ? proc : throw new NotSupportedException("ANGLE lacks a GL entry point the video frame cache needs.");
    }

    /// <summary>Render thread, context current.</summary>
    public void Dispose()
    {
        uint* names = stackalloc uint[1];
        names[0] = _texture;
        _glDeleteTextures(1, names);
        names[0] = _fbo;
        _glDeleteFramebuffers(1, names);
        (_texture, _fbo, _width, _height, _capacityWidth, _capacityHeight) = (0, 0, 0, 0, 0, 0);
    }
}
