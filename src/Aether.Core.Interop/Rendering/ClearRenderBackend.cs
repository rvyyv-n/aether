using Aether.Core.Interop.DirectX;

namespace Aether.Core.Interop.Rendering;

/// <summary>Fills the back buffer with one colour through D3D11. The loop's default when no video backend is given.</summary>
public sealed class ClearRenderBackendFactory(float r = 0f, float g = 0f, float b = 0f) : IVideoRenderBackendFactory
{
    public bool CachesDeviceState => false;

    public IVideoRenderBackend Create(D3D11DeviceManager device, DxgiPresenter presenter, nint frameReadyEvent) =>
        new ClearRenderBackend(presenter, r, g, b);

    private sealed class ClearRenderBackend(DxgiPresenter presenter, float r, float g, float b) : IVideoRenderBackend
    {
        public bool PollFrame() => false;

        public void ReleaseBackBuffer()
        {
        }

        public void AcquireBackBuffer()
        {
        }

        public bool Render(uint width, uint height)
        {
            presenter.Clear(r, g, b, 1f);
            return true;
        }

        public void ReportSwap()
        {
        }

        public void Dispose()
        {
        }
    }
}
