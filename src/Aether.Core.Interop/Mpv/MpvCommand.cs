using System;
using System.Text;

namespace Aether.Core.Interop.Mpv;

/// <summary>
/// A fixed mpv command, encoded once into a NULL-terminated UTF-8 <c>argv</c> on the pinned object
/// heap. Sending it (<see cref="MpvContext.CommandAsync"/>) allocates nothing, so it suits commands
/// issued at key-repeat rate such as frame stepping. There is nothing to dispose: the GC frees the
/// buffers once the command is unreachable, and every call keeps the command alive for its duration.
/// </summary>
public sealed unsafe class MpvCommand
{
    public static readonly MpvCommand FrameStep = new("frame-step");
    public static readonly MpvCommand FrameBackStep = new("frame-back-step");

    private readonly byte[] _strings;
    private readonly nint[] _argv;

    public MpvCommand(params string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
        {
            throw new ArgumentException("A command needs a name.", nameof(args));
        }

        int total = 0;
        foreach (string arg in args)
        {
            ArgumentNullException.ThrowIfNull(arg, nameof(args));
            if (arg.Contains('\0'))
            {
                throw new ArgumentException("Arguments cannot contain NUL.", nameof(args));
            }
            total += Encoding.UTF8.GetByteCount(arg) + 1;
        }

        _strings = GC.AllocateArray<byte>(total, pinned: true);
        _argv = GC.AllocateArray<nint>(args.Length + 1, pinned: true);
        fixed (byte* basePtr = _strings)
        {
            int offset = 0;
            for (int i = 0; i < args.Length; i++)
            {
                _argv[i] = (nint)(basePtr + offset);
                offset += Encoding.UTF8.GetBytes(args[i], _strings.AsSpan(offset)) + 1; // trailing 0 is already there
            }
        }
        _argv[args.Length] = 0;
        Arguments = Array.AsReadOnly((string[])args.Clone());
    }

    public string Name => Arguments[0];

    public System.Collections.ObjectModel.ReadOnlyCollection<string> Arguments { get; }

    /// <summary>The <c>const char**</c> argument vector. Pinned; valid while this object is reachable.</summary>
    internal byte** Argv
    {
        get
        {
            fixed (nint* p = _argv)
            {
                return (byte**)p; // pinned object heap: the address is stable after the fixed block
            }
        }
    }

    public override string ToString() => string.Join(' ', Arguments);
}
