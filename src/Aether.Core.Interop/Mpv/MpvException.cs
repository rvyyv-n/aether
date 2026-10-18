using System;
using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Mpv;

/// <summary>A libmpv call returned a negative <c>mpv_error</c>.</summary>
public sealed class MpvException : Exception
{
    public MpvException(int errorCode, string operation)
        : base($"{operation} failed: {Describe(errorCode)} ({errorCode}).")
    {
        ErrorCode = errorCode;
    }

    public int ErrorCode { get; }

    public MpvError Error => (MpvError)ErrorCode;

    /// <summary>Throws for negative codes; returns the code otherwise. Allocates only on failure.</summary>
    public static int ThrowIfError(int errorCode, string operation) =>
        errorCode < 0 ? throw new MpvException(errorCode, operation) : errorCode;

    private static unsafe string Describe(int errorCode)
    {
        string fallback = ((MpvError)errorCode).ToString();
        try
        {
            byte* s = MpvApi.ErrorString(errorCode);
            if (s != null)
            {
                string? str = Marshal.PtrToStringUTF8((nint)s);
                return string.IsNullOrWhiteSpace(str) ? fallback : $"{str} [{fallback}]";
            }
            return fallback;
        }
        catch (DllNotFoundException)
        {
            return fallback;
        }
        catch (EntryPointNotFoundException)
        {
            return fallback;
        }
    }
}
