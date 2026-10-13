using System;
using Aether.Core.Interop.Angle;

namespace Aether.Core.Interop.Rendering;

/// <summary>
/// The blur behind the player card (ADR-0013), drawn into the back buffer after mpv has rendered, in
/// the same ANGLE context. In-app Acrylic can't see a swap chain, so the render thread does it:
/// the card's rectangle plus a margin is blitted down to half and then quarter size (two linear
/// blits), blurred with a separable 9-tap Gaussian, and drawn back clipped to the card's rounded
/// rectangle (a signed-distance function gives the antialiased edge), together with the card's
/// shadow outside that shape. The XAML card keeps its own tint on top.
/// <para>
/// ES 3 only (blits and <c>gl_VertexID</c>, so no vertex buffers are needed). mpv draws in this
/// context next frame, so every binding and state this pass touches is saved and restored. Render thread.
/// </para>
/// </summary>
internal sealed unsafe class BackdropBlurPass : IDisposable
{
    private const uint GL_VERSION = 0x1F02;
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
    private const uint GL_SAMPLER_BINDING = 0x8919;
    private const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    private const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    private const uint GL_TEXTURE_WRAP_S = 0x2802;
    private const uint GL_TEXTURE_WRAP_T = 0x2803;
    private const int GL_LINEAR = 0x2601;
    private const int GL_CLAMP_TO_EDGE = 0x812F;
    private const uint GL_RGBA = 0x1908;
    private const uint GL_UNSIGNED_BYTE = 0x1401;
    private const uint GL_TRIANGLE_STRIP = 0x0005;
    private const uint GL_VERTEX_SHADER = 0x8B31;
    private const uint GL_FRAGMENT_SHADER = 0x8B30;
    private const uint GL_COMPILE_STATUS = 0x8B81;
    private const uint GL_LINK_STATUS = 0x8B82;
    private const uint GL_CURRENT_PROGRAM = 0x8B8D;
    private const uint GL_VIEWPORT = 0x0BA2;
    private const uint GL_COLOR_WRITEMASK = 0x0C23;
    private const uint GL_BLEND = 0x0BE2;
    private const uint GL_SCISSOR_TEST = 0x0C11;
    private const uint GL_CULL_FACE = 0x0B44;
    private const uint GL_DEPTH_TEST = 0x0B71;
    private const uint GL_STENCIL_TEST = 0x0B90;
    private const uint GL_BLEND_SRC_RGB = 0x80C9;
    private const uint GL_BLEND_SRC_ALPHA = 0x80CB;
    private const uint GL_BLEND_DST_RGB = 0x80C8;
    private const uint GL_BLEND_DST_ALPHA = 0x80CA;
    private const uint GL_BLEND_EQUATION_RGB = 0x8009;
    private const uint GL_BLEND_EQUATION_ALPHA = 0x883D;
    private const uint GL_ONE = 1;
    private const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;
    private const uint GL_FUNC_ADD = 0x8006;

    // Two rounds of the classic 9-tap kernel (5 linear fetches) at quarter size: about 12 px of
    // standard deviation at 1x, close to what Acrylic looks like.
    private const int BlurRounds = 2;

    private const string VertexSource = """
        #version 300 es
        uniform vec4 uQuad;
        void main() {
            vec2 c = vec2(float(gl_VertexID & 1), float((gl_VertexID >> 1) & 1));
            gl_Position = vec4(mix(uQuad.xy, uQuad.zw, c), 0.0, 1.0);
        }
        """;

    private const string BlurSource = """
        #version 300 es
        precision highp float;
        uniform sampler2D uTex;
        uniform vec4 uBlur; // 1/width, 1/height, step.x, step.y in uv
        out vec4 o;
        void main() {
            vec2 uv = gl_FragCoord.xy * uBlur.xy;
            vec2 s = uBlur.zw;
            o = texture(uTex, uv) * 0.2270270270
              + (texture(uTex, uv + s * 1.3846153846) + texture(uTex, uv - s * 1.3846153846)) * 0.3162162162
              + (texture(uTex, uv + s * 3.2307692308) + texture(uTex, uv - s * 3.2307692308)) * 0.0702702703;
        }
        """;

    private const string CompositeSource = """
        #version 300 es
        precision highp float;
        uniform sampler2D uTex;
        uniform vec4 uCard;   // x0, y0, x1, y1 in back-buffer pixels
        uniform vec4 uCopy;   // x, y, width, height of the blurred source in back-buffer pixels
        uniform vec4 uShape;  // corner radius, shadow sigma, shadow offset, opacity (pixels)
        uniform float uShadow; // shadow strength
        out vec4 o;
        float box(vec2 p, vec2 ext, float r) {
            vec2 q = abs(p) - ext + r;
            return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
        }
        void main() {
            vec2 centre = (uCard.xy + uCard.zw) * 0.5;
            vec2 ext = (uCard.zw - uCard.xy) * 0.5;
            vec2 p = gl_FragCoord.xy - centre;
            float cover = clamp(0.5 - box(p, ext, uShape.x), 0.0, 1.0);
            // A Gaussian-blurred edge is close to a logistic curve over the distance to the shape.
            float d = box(p - vec2(0.0, -uShape.z), ext, uShape.x) / uShape.y;
            float shadow = uShadow / (1.0 + exp(min(1.702 * d, 30.0))) * (1.0 - cover);
            vec3 blur = texture(uTex, (gl_FragCoord.xy - uCopy.xy) / uCopy.zw).rgb;
            float a = uShape.w;
            o = vec4(blur * (cover * a), a * (cover + shadow));
        }
        """;

    private readonly delegate* unmanaged[Stdcall]<uint, int*, void> _glGetIntegerv;
    private readonly delegate* unmanaged[Stdcall]<uint, byte*, void> _glGetBooleanv;
    private readonly delegate* unmanaged[Stdcall]<uint, byte> _glIsEnabled;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glEnable;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glDisable;
    private readonly delegate* unmanaged[Stdcall]<byte, byte, byte, byte, void> _glColorMask;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, void> _glBindFramebuffer;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, void> _glBindTexture;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glActiveTexture;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, void> _glBindSampler;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glUseProgram;
    private readonly delegate* unmanaged[Stdcall]<int, int, int, int, void> _glViewport;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, uint, uint, void> _glBlendFuncSeparate;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, void> _glBlendEquationSeparate;
    private readonly delegate* unmanaged[Stdcall]<int, int, int, int, int, int, int, int, uint, uint, void> _glBlitFramebuffer;
    private readonly delegate* unmanaged[Stdcall]<uint, int, int, void> _glDrawArrays;
    private readonly delegate* unmanaged[Stdcall]<int, uint*, void> _glGenTextures;
    private readonly delegate* unmanaged[Stdcall]<int, uint*, void> _glDeleteTextures;
    private readonly delegate* unmanaged[Stdcall]<int, uint*, void> _glGenFramebuffers;
    private readonly delegate* unmanaged[Stdcall]<int, uint*, void> _glDeleteFramebuffers;
    private readonly delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void> _glTexImage2D;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, int, void> _glTexParameteri;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void> _glFramebufferTexture2D;
    private readonly delegate* unmanaged[Stdcall]<uint, uint> _glCheckFramebufferStatus;
    private readonly delegate* unmanaged[Stdcall]<uint, uint> _glCreateShader;
    private readonly delegate* unmanaged[Stdcall]<uint, int, byte**, int*, void> _glShaderSource;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glCompileShader;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, int*, void> _glGetShaderiv;
    private readonly delegate* unmanaged[Stdcall]<uint> _glCreateProgram;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, void> _glAttachShader;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glLinkProgram;
    private readonly delegate* unmanaged[Stdcall]<uint, uint, int*, void> _glGetProgramiv;
    private readonly delegate* unmanaged[Stdcall]<uint, byte*, int> _glGetUniformLocation;
    private readonly delegate* unmanaged[Stdcall]<int, float, void> _glUniform1f;
    private readonly delegate* unmanaged[Stdcall]<int, float, float, float, float, void> _glUniform4f;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glDeleteShader;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glDeleteProgram;

    private uint _blurProgram, _compositeProgram;
    private int _blurQuad, _blurParams;
    private int _compositeQuad, _compositeCard, _compositeCopy, _compositeShape, _compositeShadow;

    // Half-size and quarter-size targets for the card's rectangle; _a and _b ping-pong while blurring.
    private uint _half, _a, _b, _halfFbo, _aFbo, _bFbo;
    private int _halfWidth, _halfHeight, _quarterWidth, _quarterHeight;
    private bool _failed;

    private BackdropBlurPass(AngleBridge bridge)
    {
        _glGetIntegerv = (delegate* unmanaged[Stdcall]<uint, int*, void>)Need(bridge, "glGetIntegerv"u8);
        _glGetBooleanv = (delegate* unmanaged[Stdcall]<uint, byte*, void>)Need(bridge, "glGetBooleanv"u8);
        _glIsEnabled = (delegate* unmanaged[Stdcall]<uint, byte>)Need(bridge, "glIsEnabled"u8);
        _glEnable = (delegate* unmanaged[Stdcall]<uint, void>)Need(bridge, "glEnable"u8);
        _glDisable = (delegate* unmanaged[Stdcall]<uint, void>)Need(bridge, "glDisable"u8);
        _glColorMask = (delegate* unmanaged[Stdcall]<byte, byte, byte, byte, void>)Need(bridge, "glColorMask"u8);
        _glBindFramebuffer = (delegate* unmanaged[Stdcall]<uint, uint, void>)Need(bridge, "glBindFramebuffer"u8);
        _glBindTexture = (delegate* unmanaged[Stdcall]<uint, uint, void>)Need(bridge, "glBindTexture"u8);
        _glActiveTexture = (delegate* unmanaged[Stdcall]<uint, void>)Need(bridge, "glActiveTexture"u8);
        _glBindSampler = (delegate* unmanaged[Stdcall]<uint, uint, void>)Need(bridge, "glBindSampler"u8);
        _glUseProgram = (delegate* unmanaged[Stdcall]<uint, void>)Need(bridge, "glUseProgram"u8);
        _glViewport = (delegate* unmanaged[Stdcall]<int, int, int, int, void>)Need(bridge, "glViewport"u8);
        _glBlendFuncSeparate = (delegate* unmanaged[Stdcall]<uint, uint, uint, uint, void>)Need(bridge, "glBlendFuncSeparate"u8);
        _glBlendEquationSeparate = (delegate* unmanaged[Stdcall]<uint, uint, void>)Need(bridge, "glBlendEquationSeparate"u8);
        _glBlitFramebuffer = (delegate* unmanaged[Stdcall]<int, int, int, int, int, int, int, int, uint, uint, void>)Need(bridge, "glBlitFramebuffer"u8);
        _glDrawArrays = (delegate* unmanaged[Stdcall]<uint, int, int, void>)Need(bridge, "glDrawArrays"u8);
        _glGenTextures = (delegate* unmanaged[Stdcall]<int, uint*, void>)Need(bridge, "glGenTextures"u8);
        _glDeleteTextures = (delegate* unmanaged[Stdcall]<int, uint*, void>)Need(bridge, "glDeleteTextures"u8);
        _glGenFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)Need(bridge, "glGenFramebuffers"u8);
        _glDeleteFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)Need(bridge, "glDeleteFramebuffers"u8);
        _glTexImage2D = (delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void>)Need(bridge, "glTexImage2D"u8);
        _glTexParameteri = (delegate* unmanaged[Stdcall]<uint, uint, int, void>)Need(bridge, "glTexParameteri"u8);
        _glFramebufferTexture2D = (delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void>)Need(bridge, "glFramebufferTexture2D"u8);
        _glCheckFramebufferStatus = (delegate* unmanaged[Stdcall]<uint, uint>)Need(bridge, "glCheckFramebufferStatus"u8);
        _glCreateShader = (delegate* unmanaged[Stdcall]<uint, uint>)Need(bridge, "glCreateShader"u8);
        _glShaderSource = (delegate* unmanaged[Stdcall]<uint, int, byte**, int*, void>)Need(bridge, "glShaderSource"u8);
        _glCompileShader = (delegate* unmanaged[Stdcall]<uint, void>)Need(bridge, "glCompileShader"u8);
        _glGetShaderiv = (delegate* unmanaged[Stdcall]<uint, uint, int*, void>)Need(bridge, "glGetShaderiv"u8);
        _glCreateProgram = (delegate* unmanaged[Stdcall]<uint>)Need(bridge, "glCreateProgram"u8);
        _glAttachShader = (delegate* unmanaged[Stdcall]<uint, uint, void>)Need(bridge, "glAttachShader"u8);
        _glLinkProgram = (delegate* unmanaged[Stdcall]<uint, void>)Need(bridge, "glLinkProgram"u8);
        _glGetProgramiv = (delegate* unmanaged[Stdcall]<uint, uint, int*, void>)Need(bridge, "glGetProgramiv"u8);
        _glGetUniformLocation = (delegate* unmanaged[Stdcall]<uint, byte*, int>)Need(bridge, "glGetUniformLocation"u8);
        _glUniform1f = (delegate* unmanaged[Stdcall]<int, float, void>)Need(bridge, "glUniform1f"u8);
        _glUniform4f = (delegate* unmanaged[Stdcall]<int, float, float, float, float, void>)Need(bridge, "glUniform4f"u8);
        _glDeleteShader = (delegate* unmanaged[Stdcall]<uint, void>)Need(bridge, "glDeleteShader"u8);
        _glDeleteProgram = (delegate* unmanaged[Stdcall]<uint, void>)Need(bridge, "glDeleteProgram"u8);
    }

    /// <summary>
    /// Builds the pass in the bridge's current context. Null if the context is not ES 3 or the shaders
    /// won't build, in which case the card keeps its plain tint.
    /// </summary>
    public static BackdropBlurPass? Create(AngleBridge bridge)
    {
        var getString = (delegate* unmanaged[Stdcall]<uint, byte*>)bridge.GlProc("glGetString"u8);
        if (getString == null)
        {
            return null;
        }
        byte* version = getString(GL_VERSION);
        // "OpenGL ES 3.0 (ANGLE ...)": ES 2 has neither blits nor gl_VertexID.
        if (version == null || version[10] < (byte)'3')
        {
            return null;
        }

        BackdropBlurPass? pass = null;
        try
        {
            pass = new BackdropBlurPass(bridge);
            if (pass.Build())
            {
                return pass;
            }
            pass.Dispose();
            return null;
        }
        catch (NotSupportedException)
        {
            pass?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Blurs the card's surroundings and draws the blurred card and its shadow over the back buffer.
    /// No allocation; restores every GL binding and state it changes.
    /// </summary>
    public void Draw(in BackdropFrame frame, uint bufferWidth, uint bufferHeight)
    {
        if (_failed || !BackdropGeometry.TryLayout(frame.Region, bufferWidth, bufferHeight, out BackdropLayout layout))
        {
            return;
        }

        SavedState saved = Save();
        try
        {
            if (!EnsureTargets((layout.CopyWidth + 1) / 2, (layout.CopyHeight + 1) / 2,
                    (layout.CopyWidth + 3) / 4, (layout.CopyHeight + 3) / 4))
            {
                _failed = true;
                return;
            }

            _glDisable(GL_SCISSOR_TEST);
            _glDisable(GL_BLEND);
            _glDisable(GL_CULL_FACE);
            _glDisable(GL_DEPTH_TEST);
            _glDisable(GL_STENCIL_TEST);
            _glColorMask(1, 1, 1, 1);
            _glActiveTexture(GL_TEXTURE0);
            _glBindSampler(0, 0);

            // Two linear blits make a 4×4 box average, enough to keep the quarter-size copy from shimmering.
            _glBindFramebuffer(GL_READ_FRAMEBUFFER, 0);
            _glBindFramebuffer(GL_DRAW_FRAMEBUFFER, _halfFbo);
            _glBlitFramebuffer(layout.CopyX, layout.CopyY, layout.CopyX + layout.CopyWidth, layout.CopyY + layout.CopyHeight,
                0, 0, _halfWidth, _halfHeight, GL_COLOR_BUFFER_BIT, (uint)GL_LINEAR);
            _glBindFramebuffer(GL_READ_FRAMEBUFFER, _halfFbo);
            _glBindFramebuffer(GL_DRAW_FRAMEBUFFER, _aFbo);
            _glBlitFramebuffer(0, 0, _halfWidth, _halfHeight, 0, 0, _quarterWidth, _quarterHeight, GL_COLOR_BUFFER_BIT, (uint)GL_LINEAR);

            _glViewport(0, 0, _quarterWidth, _quarterHeight);
            _glUseProgram(_blurProgram);
            _glUniform4f(_blurQuad, -1f, -1f, 1f, 1f);
            float invW = 1f / _quarterWidth, invH = 1f / _quarterHeight;
            for (int i = 0; i < BlurRounds; i++)
            {
                BlurPass(_bFbo, _a, invW, invH, invW, 0f);
                BlurPass(_aFbo, _b, invW, invH, 0f, invH);
            }

            _glBindFramebuffer(GL_DRAW_FRAMEBUFFER, 0);
            _glViewport(0, 0, (int)bufferWidth, (int)bufferHeight);
            _glEnable(GL_BLEND);
            _glBlendFuncSeparate(GL_ONE, GL_ONE_MINUS_SRC_ALPHA, GL_ONE, GL_ONE_MINUS_SRC_ALPHA);
            _glBlendEquationSeparate(GL_FUNC_ADD, GL_FUNC_ADD);
            _glUseProgram(_compositeProgram);
            _glBindTexture(GL_TEXTURE_2D, _a);

            float sigma = BackdropGeometry.ShadowBlurDips * 0.5f * layout.Scale;
            float offset = BackdropGeometry.ShadowOffsetDips * layout.Scale;
            float reach = (3f * sigma) + offset;
            float w = bufferWidth, h = bufferHeight;
            _glUniform4f(_compositeQuad,
                (((layout.CardX0 - reach) / w) * 2f) - 1f, (((layout.CardY0 - reach) / h) * 2f) - 1f,
                (((layout.CardX1 + reach) / w) * 2f) - 1f, (((layout.CardY1 + reach) / h) * 2f) - 1f);
            _glUniform4f(_compositeCard, layout.CardX0, layout.CardY0, layout.CardX1, layout.CardY1);
            _glUniform4f(_compositeCopy, layout.CopyX, layout.CopyY, layout.CopyWidth, layout.CopyHeight);
            _glUniform4f(_compositeShape, layout.Radius, sigma, offset, frame.Opacity);
            _glUniform1f(_compositeShadow, BackdropGeometry.ShadowOpacity);
            _glDrawArrays(GL_TRIANGLE_STRIP, 0, 4);
        }
        finally
        {
            Restore(saved);
        }
    }

    private void BlurPass(uint targetFbo, uint sourceTexture, float invW, float invH, float stepX, float stepY)
    {
        _glBindFramebuffer(GL_DRAW_FRAMEBUFFER, targetFbo);
        _glBindTexture(GL_TEXTURE_2D, sourceTexture);
        _glUniform4f(_blurParams, invW, invH, stepX, stepY);
        _glDrawArrays(GL_TRIANGLE_STRIP, 0, 4);
    }

    private bool Build()
    {
        uint vertex = Compile(GL_VERTEX_SHADER, VertexSource);
        uint blur = Compile(GL_FRAGMENT_SHADER, BlurSource);
        uint composite = Compile(GL_FRAGMENT_SHADER, CompositeSource);
        try
        {
            if (vertex == 0 || blur == 0 || composite == 0)
            {
                return false;
            }
            _blurProgram = Link(vertex, blur);
            _compositeProgram = Link(vertex, composite);
            if (_blurProgram == 0 || _compositeProgram == 0)
            {
                return false;
            }
        }
        finally
        {
            _glDeleteShader(vertex);
            _glDeleteShader(blur);
            _glDeleteShader(composite);
        }

        _blurQuad = Uniform(_blurProgram, "uQuad"u8);
        _blurParams = Uniform(_blurProgram, "uBlur"u8);
        _compositeQuad = Uniform(_compositeProgram, "uQuad"u8);
        _compositeCard = Uniform(_compositeProgram, "uCard"u8);
        _compositeCopy = Uniform(_compositeProgram, "uCopy"u8);
        _compositeShape = Uniform(_compositeProgram, "uShape"u8);
        _compositeShadow = Uniform(_compositeProgram, "uShadow"u8);

        uint* names = stackalloc uint[3];
        _glGenTextures(3, names);
        (_half, _a, _b) = (names[0], names[1], names[2]);
        _glGenFramebuffers(3, names);
        (_halfFbo, _aFbo, _bFbo) = (names[0], names[1], names[2]);
        return true;
    }

    private uint Compile(uint type, string source)
    {
        uint shader = _glCreateShader(type);
        // The sources are plain ASCII; narrow them into a stack buffer for the driver.
        int length = source.Length;
        byte* text = stackalloc byte[length + 1];
        for (int i = 0; i < length; i++)
        {
            text[i] = (byte)source[i];
        }
        text[length] = 0;
        _glShaderSource(shader, 1, &text, null);
        _glCompileShader(shader);
        int ok = 0;
        _glGetShaderiv(shader, GL_COMPILE_STATUS, &ok);
        if (ok == 0)
        {
            _glDeleteShader(shader);
            return 0;
        }
        return shader;
    }

    private uint Link(uint vertex, uint fragment)
    {
        uint program = _glCreateProgram();
        _glAttachShader(program, vertex);
        _glAttachShader(program, fragment);
        _glLinkProgram(program);
        int ok = 0;
        _glGetProgramiv(program, GL_LINK_STATUS, &ok);
        if (ok == 0)
        {
            _glDeleteProgram(program);
            return 0;
        }
        return program;
    }

    private int Uniform(uint program, ReadOnlySpan<byte> name)
    {
        fixed (byte* p = name)
        {
            return _glGetUniformLocation(program, p);
        }
    }

    /// <summary>(Re)allocates the targets when the card's rectangle changed size. Binds textures; the caller restores them.</summary>
    private bool EnsureTargets(int halfWidth, int halfHeight, int quarterWidth, int quarterHeight)
    {
        if (halfWidth == _halfWidth && halfHeight == _halfHeight && quarterWidth == _quarterWidth && quarterHeight == _quarterHeight)
        {
            return true;
        }

        Allocate(_half, _halfFbo, halfWidth, halfHeight);
        Allocate(_a, _aFbo, quarterWidth, quarterHeight);
        Allocate(_b, _bFbo, quarterWidth, quarterHeight);
        (_halfWidth, _halfHeight, _quarterWidth, _quarterHeight) = (halfWidth, halfHeight, quarterWidth, quarterHeight);

        foreach (uint fbo in (ReadOnlySpan<uint>)[_halfFbo, _aFbo, _bFbo])
        {
            _glBindFramebuffer(GL_DRAW_FRAMEBUFFER, fbo);
            if (_glCheckFramebufferStatus(GL_DRAW_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE)
            {
                return false;
            }
        }
        return true;
    }

    private void Allocate(uint texture, uint fbo, int width, int height)
    {
        _glActiveTexture(GL_TEXTURE0);
        _glBindTexture(GL_TEXTURE_2D, texture);
        _glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
        _glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        _glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        _glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        _glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        _glBindFramebuffer(GL_DRAW_FRAMEBUFFER, fbo);
        _glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, texture, 0);
    }

    private struct SavedState
    {
        public int DrawFbo, ReadFbo, Program, ActiveTexture, Texture, Sampler;
        public int ViewportX, ViewportY, ViewportWidth, ViewportHeight;
        public int BlendSrcRgb, BlendSrcAlpha, BlendDstRgb, BlendDstAlpha, BlendEquationRgb, BlendEquationAlpha;
        public bool Scissor, Blend, Cull, Depth, Stencil;
        public byte MaskR, MaskG, MaskB, MaskA;
    }

    private SavedState Save()
    {
        SavedState s = default;
        int value;
        _glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &value);
        s.DrawFbo = value;
        _glGetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &value);
        s.ReadFbo = value;
        _glGetIntegerv(GL_CURRENT_PROGRAM, &value);
        s.Program = value;
        _glGetIntegerv(GL_ACTIVE_TEXTURE, &value);
        s.ActiveTexture = value;
        // Only unit 0 is used, so that is the binding to keep.
        _glActiveTexture(GL_TEXTURE0);
        _glGetIntegerv(GL_TEXTURE_BINDING_2D, &value);
        s.Texture = value;
        _glGetIntegerv(GL_SAMPLER_BINDING, &value);
        s.Sampler = value;
        int* viewport = stackalloc int[4];
        _glGetIntegerv(GL_VIEWPORT, viewport);
        (s.ViewportX, s.ViewportY, s.ViewportWidth, s.ViewportHeight) = (viewport[0], viewport[1], viewport[2], viewport[3]);
        _glGetIntegerv(GL_BLEND_SRC_RGB, &value);
        s.BlendSrcRgb = value;
        _glGetIntegerv(GL_BLEND_SRC_ALPHA, &value);
        s.BlendSrcAlpha = value;
        _glGetIntegerv(GL_BLEND_DST_RGB, &value);
        s.BlendDstRgb = value;
        _glGetIntegerv(GL_BLEND_DST_ALPHA, &value);
        s.BlendDstAlpha = value;
        _glGetIntegerv(GL_BLEND_EQUATION_RGB, &value);
        s.BlendEquationRgb = value;
        _glGetIntegerv(GL_BLEND_EQUATION_ALPHA, &value);
        s.BlendEquationAlpha = value;
        s.Scissor = _glIsEnabled(GL_SCISSOR_TEST) != 0;
        s.Blend = _glIsEnabled(GL_BLEND) != 0;
        s.Cull = _glIsEnabled(GL_CULL_FACE) != 0;
        s.Depth = _glIsEnabled(GL_DEPTH_TEST) != 0;
        s.Stencil = _glIsEnabled(GL_STENCIL_TEST) != 0;
        byte* mask = stackalloc byte[4];
        _glGetBooleanv(GL_COLOR_WRITEMASK, mask);
        (s.MaskR, s.MaskG, s.MaskB, s.MaskA) = (mask[0], mask[1], mask[2], mask[3]);
        return s;
    }

    private void Restore(in SavedState s)
    {
        _glBindFramebuffer(GL_READ_FRAMEBUFFER, (uint)s.ReadFbo);
        _glBindFramebuffer(GL_DRAW_FRAMEBUFFER, (uint)s.DrawFbo);
        _glUseProgram((uint)s.Program);
        _glActiveTexture(GL_TEXTURE0);
        _glBindTexture(GL_TEXTURE_2D, (uint)s.Texture);
        _glBindSampler(0, (uint)s.Sampler);
        _glActiveTexture((uint)s.ActiveTexture);
        _glViewport(s.ViewportX, s.ViewportY, s.ViewportWidth, s.ViewportHeight);
        _glBlendFuncSeparate((uint)s.BlendSrcRgb, (uint)s.BlendDstRgb, (uint)s.BlendSrcAlpha, (uint)s.BlendDstAlpha);
        _glBlendEquationSeparate((uint)s.BlendEquationRgb, (uint)s.BlendEquationAlpha);
        SetEnabled(GL_SCISSOR_TEST, s.Scissor);
        SetEnabled(GL_BLEND, s.Blend);
        SetEnabled(GL_CULL_FACE, s.Cull);
        SetEnabled(GL_DEPTH_TEST, s.Depth);
        SetEnabled(GL_STENCIL_TEST, s.Stencil);
        _glColorMask(s.MaskR, s.MaskG, s.MaskB, s.MaskA);
    }

    private void SetEnabled(uint capability, bool enabled)
    {
        if (enabled)
        {
            _glEnable(capability);
        }
        else
        {
            _glDisable(capability);
        }
    }

    private static void* Need(AngleBridge bridge, ReadOnlySpan<byte> name)
    {
        void* proc = bridge.GlProc(name);
        return proc != null ? proc : throw new NotSupportedException("ANGLE lacks a GL entry point the backdrop blur needs.");
    }

    /// <summary>Render thread, context current.</summary>
    public void Dispose()
    {
        uint* names = stackalloc uint[3] { _half, _a, _b };
        _glDeleteTextures(3, names);
        names[0] = _halfFbo;
        names[1] = _aFbo;
        names[2] = _bFbo;
        _glDeleteFramebuffers(3, names);
        _glDeleteProgram(_blurProgram);
        _glDeleteProgram(_compositeProgram);
        (_half, _a, _b, _halfFbo, _aFbo, _bFbo) = (0, 0, 0, 0, 0, 0);
        _blurProgram = _compositeProgram = 0;
    }
}
