using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Mpv;

public sealed class MpvContextOptions
{
    /// <summary>
    /// <c>hwdec</c>. d3d11va decodes on the D3D11 device that the render context exposes
    /// (ANGLE's device, which is ours; see ADR-0007), so frames stay on the GPU.
    /// </summary>
    public string HardwareDecoder { get; init; } = "d3d11va";

    /// <summary><c>demuxer-max-back-bytes</c>: demuxed history kept for backward seeks and frame stepping.</summary>
    public string BackBufferSize { get; init; } = "512MiB";

    /// <summary><c>video-latency-hacks</c>: skip the decoder's frame-queue warm-up after seeks.</summary>
    public bool VideoLatencyHacks { get; init; } = true;

    /// <summary>Applied after the defaults, in order; a later entry overrides an earlier one.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> AdditionalOptions { get; init; } = [];
}

/// <summary>
/// Lifecycle wrapper for one libmpv core (<c>mpv_create</c> → options → <c>mpv_initialize</c>).
/// Options that only take effect before initialization (<c>vo</c>, <c>hwdec</c>, demuxer caches) are
/// applied here; every mpv error code is checked. The core is destroyed with
/// <c>mpv_terminate_destroy</c> once this object and every render context created from it are disposed.
/// <para>libmpv's client API is thread-safe; this wrapper adds no locking of its own.</para>
/// </summary>
public sealed class MpvContext : IDisposable
{
    private readonly MpvHandle _handle;

    private MpvContext(MpvHandle handle, IReadOnlyList<KeyValuePair<string, string>> appliedOptions)
    {
        _handle = handle;
        AppliedOptions = appliedOptions;
    }

    /// <summary>The owned core. Use <see cref="SafeHandle.DangerousAddRef"/> to borrow the raw pointer.</summary>
    public MpvHandle Handle => _handle.IsClosed ? throw new ObjectDisposedException(nameof(MpvContext)) : _handle;

    /// <summary>Options passed to <c>mpv_set_option_string</c>, in order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> AppliedOptions { get; }

    public bool IsDisposed => _handle.IsClosed;

    /// <summary>
    /// The pre-initialization option list for <paramref name="options"/>. Pure; usable without libmpv.
    /// <c>vo=libmpv</c> is fixed: the render API only drives that output.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> BuildOptions(MpvContextOptions? options = null)
    {
        options ??= new MpvContextOptions();
        var list = new List<KeyValuePair<string, string>>
        {
            new("vo", "libmpv"),
            new("hwdec", options.HardwareDecoder),
            new("demuxer-max-back-bytes", options.BackBufferSize),
            new("video-latency-hacks", options.VideoLatencyHacks ? "yes" : "no"),
            // Embedded player: no user config, no terminal, no built-in key bindings or OSC.
            new("config", "no"),
            new("terminal", "no"),
            new("input-default-bindings", "no"),
            new("input-vo-keyboard", "no"),
            new("osc", "no"),
            new("idle", "yes"),
        };
        foreach (KeyValuePair<string, string> extra in options.AdditionalOptions)
        {
            ArgumentException.ThrowIfNullOrEmpty(extra.Key, nameof(options));
            ArgumentNullException.ThrowIfNull(extra.Value, nameof(options));
            list.Add(extra);
        }
        return list.AsReadOnly();
    }

    /// <summary>Creates, configures and initializes a core. Throws <see cref="MpvException"/> on any error.</summary>
    /// <exception cref="DllNotFoundException">libmpv is not installed (see <see cref="NativeLibraries"/>).</exception>
    public static MpvContext Create(MpvContextOptions? options = null)
    {
        IReadOnlyList<KeyValuePair<string, string>> applied = BuildOptions(options);

        nint raw = MpvApi.Create();
        if (raw == 0)
        {
            throw new OutOfMemoryException("mpv_create returned NULL.");
        }

        var handle = new MpvHandle(raw);
        try
        {
            foreach (KeyValuePair<string, string> option in applied)
            {
                MpvException.ThrowIfError(MpvApi.SetOptionString(raw, option.Key, option.Value), $"mpv_set_option_string({option.Key})");
            }
            MpvException.ThrowIfError(MpvApi.Initialize(raw), "mpv_initialize");
        }
        catch
        {
            handle.Dispose();
            throw;
        }
        return new MpvContext(handle, applied);
    }

    public void SetProperty(string name, string value)
    {
        bool added = false;
        _handle.DangerousAddRef(ref added);
        try
        {
            MpvException.ThrowIfError(MpvApi.SetPropertyString(_handle.DangerousGetHandle(), name, value), $"mpv_set_property_string({name})");
        }
        finally
        {
            if (added)
            {
                _handle.DangerousRelease();
            }
        }
    }

    /// <summary>Property value as a string, or null if mpv has none (unavailable or unknown).</summary>
    public string? GetPropertyString(string name)
    {
        bool added = false;
        _handle.DangerousAddRef(ref added);
        try
        {
            nint s = MpvApi.GetPropertyString(_handle.DangerousGetHandle(), name);
            if (s == 0)
            {
                return null;
            }
            try
            {
                return Marshal.PtrToStringUTF8(s);
            }
            finally
            {
                MpvApi.Free(s);
            }
        }
        finally
        {
            if (added)
            {
                _handle.DangerousRelease();
            }
        }
    }

    /// <summary>Synchronous <c>mpv_command</c>. Not for the frame loop (marshals strings).</summary>
    public unsafe void Command(params string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
        {
            throw new ArgumentException("A command needs a name.", nameof(args));
        }

        byte** argv = (byte**)NativeMemory.AllocZeroed((nuint)(args.Length + 1), (nuint)sizeof(byte*));
        bool added = false;
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                argv[i] = (byte*)Marshal.StringToCoTaskMemUTF8(args[i] ?? throw new ArgumentNullException(nameof(args)));
            }
            _handle.DangerousAddRef(ref added);
            MpvException.ThrowIfError(MpvApi.Command(_handle.DangerousGetHandle(), argv), $"mpv_command({args[0]})");
        }
        finally
        {
            if (added)
            {
                _handle.DangerousRelease();
            }
            for (int i = 0; i < args.Length; i++)
            {
                if (argv[i] != null)
                {
                    Marshal.FreeCoTaskMem((nint)argv[i]);
                }
            }
            NativeMemory.Free(argv);
        }
    }

    /// <summary>
    /// Releases this wrapper's reference. The core is destroyed now, or when the last render
    /// context created from it is disposed.
    /// </summary>
    public void Dispose() => _handle.Dispose();
}
