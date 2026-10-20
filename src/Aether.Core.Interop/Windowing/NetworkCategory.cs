using System;
using Aether.Core.Interop.DirectX.Native;
using Aether.Core.Interop.Native;

namespace Aether.Core.Interop.Windowing;

/// <summary>
/// Whether Windows treats the network behind an adapter as Public (a café or hotel network) rather than Private or a
/// work domain, read from the Network List Manager. Any thread; takes a few milliseconds, so keep it off the UI thread.
/// </summary>
public static unsafe class NetworkCategory
{
    private const int NlmNetworkCategoryPublic = 0;
    private const int S_OK = 0;
    private const int S_FALSE = 1;

    // Slots after IUnknown (3) and IDispatch (4).
    private const int GetNetworkConnectionsSlot = 9;  // INetworkListManager
    private const int EnumNextSlot = 8;               // IEnumNetworkConnections
    private const int GetNetworkSlot = 7;             // INetworkConnection
    private const int GetAdapterIdSlot = 12;          // INetworkConnection
    private const int GetCategorySlot = 18;           // INetwork

    private static readonly Guid ClsidNetworkListManager = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");
    private static readonly Guid IidNetworkListManager = new("DCB00000-570F-4A9B-8D69-199FDBA5723B");

    /// <summary>
    /// True if the connected network on <paramref name="adapterId"/> is Public, false if it is Private or a domain, null
    /// when Windows does not say (no connection on that adapter, or the service is unavailable).
    /// </summary>
    public static bool? IsPublic(Guid adapterId)
    {
        // The caller may be a pool thread with no apartment; a thread that already has one (the UI thread) keeps it.
        int init = Ole32.CoInitializeEx(0, Ole32.COINIT_MULTITHREADED);
        bool uninitialize = init is S_OK or S_FALSE;
        nint manager = 0, connections = 0;
        try
        {
            Guid clsid = ClsidNetworkListManager;
            Guid iid = IidNetworkListManager;
            if (Ole32.CoCreateInstance(&clsid, 0, Ole32.CLSCTX_ALL, &iid, &manager) < 0 || manager == 0)
            {
                return null;
            }
            if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Vtbl(manager)[GetNetworkConnectionsSlot])(manager, &connections) < 0
                || connections == 0)
            {
                return null;
            }

            while (true)
            {
                nint connection = 0;
                uint fetched = 0;
                int hr = ((delegate* unmanaged[Stdcall]<nint, uint, nint*, uint*, int>)Com.Vtbl(connections)[EnumNextSlot])(
                    connections, 1, &connection, &fetched);
                if (hr != S_OK || fetched == 0 || connection == 0)
                {
                    return null;
                }
                try
                {
                    Guid adapter;
                    if (((delegate* unmanaged[Stdcall]<nint, Guid*, int>)Com.Vtbl(connection)[GetAdapterIdSlot])(connection, &adapter) < 0
                        || adapter != adapterId)
                    {
                        continue;
                    }
                    return CategoryIsPublic(connection);
                }
                finally
                {
                    Com.Release(connection);
                }
            }
        }
        finally
        {
            Com.Release(ref connections);
            Com.Release(ref manager);
            if (uninitialize)
            {
                Ole32.CoUninitialize();
            }
        }
    }

    private static bool? CategoryIsPublic(nint connection)
    {
        nint network = 0;
        if (((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Vtbl(connection)[GetNetworkSlot])(connection, &network) < 0 || network == 0)
        {
            return null;
        }
        try
        {
            int category;
            return ((delegate* unmanaged[Stdcall]<nint, int*, int>)Com.Vtbl(network)[GetCategorySlot])(network, &category) < 0
                ? null
                : category == NlmNetworkCategoryPublic;
        }
        finally
        {
            Com.Release(network);
        }
    }
}
