using System;
using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Mpv;

public static unsafe partial class MpvApi
{
    private const string LibMpv = "mpv-2.dll";

    [LibraryImport(LibMpv, EntryPoint = "mpv_create")]
    public static partial IntPtr Create();

    [LibraryImport(LibMpv, EntryPoint = "mpv_initialize")]
    public static partial int Initialize(IntPtr handle);

    [LibraryImport(LibMpv, EntryPoint = "mpv_destroy")]
    public static partial void Destroy(IntPtr handle);

    [LibraryImport(LibMpv, EntryPoint = "mpv_terminate_destroy")]
    public static partial void TerminateDestroy(IntPtr handle);

    [LibraryImport(LibMpv, EntryPoint = "mpv_command")]
    public static partial int Command(IntPtr handle, byte** args);

    [LibraryImport(LibMpv, EntryPoint = "mpv_command_async")]
    public static partial int CommandAsync(IntPtr handle, ulong replyUserdata, byte** args);

    [LibraryImport(LibMpv, EntryPoint = "mpv_set_option_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetOptionString(IntPtr handle, string name, string data);

    [LibraryImport(LibMpv, EntryPoint = "mpv_set_property_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetPropertyString(IntPtr handle, string name, string data);

    [LibraryImport(LibMpv, EntryPoint = "mpv_get_property_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr GetPropertyString(IntPtr handle, string name);

    [LibraryImport(LibMpv, EntryPoint = "mpv_observe_property", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int ObserveProperty(IntPtr handle, ulong replyUserdata, string name, MpvFormat format);

    [LibraryImport(LibMpv, EntryPoint = "mpv_free")]
    public static partial void Free(IntPtr data);

    [LibraryImport(LibMpv, EntryPoint = "mpv_wait_event")]
    public static partial MpvEvent* WaitEvent(IntPtr handle, double timeout);

    [LibraryImport(LibMpv, EntryPoint = "mpv_wakeup")]
    public static partial void Wakeup(IntPtr handle);

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_create")]
    public static partial int RenderContextCreate(out IntPtr ctx, IntPtr mpvHandle, MpvRenderParam* parameters);

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_render")]
    public static partial int RenderContextRender(IntPtr ctx, MpvRenderParam* parameters);

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_set_update_callback")]
    public static partial void RenderContextSetUpdateCallback(
        IntPtr ctx, delegate* unmanaged[Cdecl]<IntPtr, void> callback, IntPtr ctxData);

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_update")]
    public static partial ulong RenderContextUpdate(IntPtr ctx);

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_report_swap")]
    public static partial void RenderContextReportSwap(IntPtr ctx);

    [LibraryImport(LibMpv, EntryPoint = "mpv_render_context_free")]
    public static partial void RenderContextFree(IntPtr ctx);
}
