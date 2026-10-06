# Architecture Decision Records (ADRs)

## ADR-0001: P/Invoke Strategy & Native AOT
- **Context:** .NET 9 and Windows App SDK 1.6 support Native AOT. Legacy `[DllImport]` incurs runtime marshaling stubs and inhibits complete AOT trimming.
- **Decision:** All native C-ABI bindings (`mpv-2.dll`, DirectX COM interfaces) must use modern source-generated `[LibraryImport]` and `[GeneratedComInterface]`. Callbacks must use unmanaged function pointers (`delegate* unmanaged[Cdecl]`) with `[UnmanagedCallersOnly]`.
- **Status:** Accepted.

## ADR-0002: Direct3D 11 & SwapChainPanel Fast Path
- **Context:** Video rendering in WinUI 3 must avoid airspace limitations to allow native XAML Mica backdrops, auto-hiding OSD, and analytical HUD overlays to composite directly over video frames.
- **Decision:** Host Direct3D 11 flip-model swap chain (`DXGI_SWAP_EFFECT_FLIP_DISCARD`) via `ISwapChainPanelNative::SetSwapChain`. The primary rendering target is native DXGI render API if exposed by libmpv; otherwise ANGLE zero-copy EGL client buffer wrapping the swap chain back buffer.
- **Status:** Accepted.

## ADR-0003: Zero Allocations on Video Hot-Paths
- **Context:** High refresh rate playback (144Hz–360Hz) will suffer micro-stutter if garbage collection pauses occur during rendering or event handling.
- **Decision:** No heap allocations on frame rendering or high-frequency event pump loops. Struct-backed view models, preallocated parameter blocks, and `GCSettings.LatencyMode = SustainedLowLatency` during playback.
- **Status:** Accepted.

## ADR-0004: Multi-Model Agent Specialization in T3 Code
- **Context:** Complex systems require different reasoning depths and velocities across low-level interop vs. XAML UI vs. architectural refactoring.
- **Decision:** T3 Code orchestration will delegate low-level interop (`Aether.Core.Interop`) to Claude Opus 5.5, high-velocity UX and features (`Aether.App`, `Aether.PvP.Review`, `Aether.Server.LanHost`) to Claude Sonnet 5.5, and cross-repo context, header ingestion, scaffolding, and test generation to Gemini.
- **Status:** Accepted.

## ADR-0005: DirectX COM Through Raw Vtable Calls
- **Context:** The D3D11/DXGI surface used per frame (`Present`, `ClearRenderTargetView`, `ResizeBuffers`, the latency wait) must not allocate, and Native AOT rules out runtime-generated COM wrappers. `[GeneratedComInterface]` still routes calls through `ComWrappers` and managed object identity.
- **Decision:** `Aether.Core.Interop.DirectX` holds DirectX objects as bare `nint` COM pointers and calls them through `delegate* unmanaged[Stdcall]` vtable slots (`Native/Com.cs`). Each wrapper owns exactly the references it AddRef'd, releases them in `Dispose` (with a finalizer backstop; D3D11/DXGI objects are free-threaded), and checks every `HRESULT`. `ISwapChainPanelNative` is reached the same way (`SwapChainPanelNative.SetSwapChain`), from the panel pointer the app passes in. Only flat C exports (`D3D11CreateDevice`, `WaitForSingleObjectEx`) use `[LibraryImport]`.
- **Status:** Accepted.

## ADR-0006: Device and Presenter Policy
- **Context:** Pass 1 needs a device that serves d3d11va decode and composition, and a swap chain with one frame of latency, safe to resize from the UI thread without blocking it.
- **Decision:**
  - Device: BGRA + video support, multithread protection on, feature levels 11.1 then 11.0 (d3d11va and `gpu-next` need no more than 11.0). Hardware falls back to WARP; WARP drops the video flag because it rejects it (`DXGI_ERROR_UNSUPPORTED`). The DXGI factory comes from the device's own adapter.
  - Swap chain: composition, `FLIP_DISCARD`, 2 buffers, `B8G8R8A8_UNORM`, `DXGI_SCALING_STRETCH`, `FRAME_LATENCY_WAITABLE_OBJECT` with maximum latency 1, plus `ALLOW_TEARING` when the factory supports it (used only by `Present(vsync: false)`). Alpha mode defaults to `Ignore` so DWM can use independent flip; letterboxing over Mica (Pass 4) should size the panel to the video, or opt into `Premultiplied`.
  - Resize: the UI thread posts the panel's DIP size and `CompositionScale` with `SetTargetSize` (no GPU work, no wait). The render-owner thread applies it with `ApplyPendingResize` between frames: `ResizeBuffers` (same flags) when the pixel size changes, and `SetMatrixTransform` with the inverse scale when the DPI changes.
  - Device loss surfaces as `PresentResult.DeviceLost` / `IsDeviceLost`, not an exception. Recovery (recreate device and presenter) is left to the Pass 2 render loop.
- **Status:** Accepted.
