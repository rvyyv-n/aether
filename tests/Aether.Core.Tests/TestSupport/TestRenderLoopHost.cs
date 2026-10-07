using System;
using System.Collections.Concurrent;
using Aether.Core.Interop.Rendering;

namespace Aether.Core.Tests.TestSupport;

public sealed class TestRenderLoopHost : IRenderLoopHost, IDisposable
{
    public ConcurrentQueue<SwapChainHandoff> CreatedHandoffs { get; } = new();
    public ConcurrentQueue<(int Reason, int Generation)> DeviceLostEvents { get; } = new();
    public ConcurrentQueue<Exception> FaultEvents { get; } = new();

    public Action<SwapChainHandoff>? OnCreatedAction { get; set; }
    public Action<int, int>? OnDeviceLostAction { get; set; }
    public Action<Exception>? OnFaultAction { get; set; }

    public void OnSwapChainCreated(SwapChainHandoff handoff)
    {
        CreatedHandoffs.Enqueue(handoff);
        OnCreatedAction?.Invoke(handoff);
    }

    public void OnDeviceLost(int reason, int generation)
    {
        DeviceLostEvents.Enqueue((reason, generation));
        OnDeviceLostAction?.Invoke(reason, generation);
    }

    public void OnFault(Exception exception)
    {
        FaultEvents.Enqueue(exception);
        OnFaultAction?.Invoke(exception);
    }

    public void Dispose()
    {
        while (CreatedHandoffs.TryDequeue(out var handoff))
        {
            try
            {
                handoff.Dispose();
            }
            catch
            {
            }
        }
    }
}
