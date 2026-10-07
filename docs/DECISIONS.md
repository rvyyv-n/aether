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

## ADR-0007: ANGLE Is the Primary Render Path
- **Context:** ADR-0002 preferred a native D3D11 render API "if exposed by libmpv". The libmpv headers at release `v0.41.0` and on `master` (`include/mpv/render.h`, client API 2.5) define only `MPV_RENDER_API_TYPE_OPENGL` (`"opengl"`) and `MPV_RENDER_API_TYPE_SW` (`"sw"`); `mpv_render_context_create` returns `MPV_ERROR_NOT_IMPLEMENTED` for anything else. `vo=libmpv` ignores `gpu-api`, `gpu-context` and the `gpu-next` renderer. Embedding through `--wid` would give mpv its own D3D11 swap chain, but on a child HWND, which brings back the airspace problem ADR-0002 exists to avoid.
- **Decision:**
  - Path 2 (ANGLE) becomes the only GPU path. `AngleBridge` creates an ANGLE EGL display on *our* `ID3D11Device` (`EGL_ANGLE_device_creation` + `EGL_PLATFORM_DEVICE_EXT`) and wraps swap chain buffer 0 as an EGL pbuffer (`EGL_ANGLE_d3d_texture_client_buffer`). mpv renders into GL framebuffer 0 of that pbuffer, which is the swap chain back buffer; DXGI presents it. No copy.
  - `hwdec=d3d11va` stays: mpv's ANGLE interop (`d3d11egl`) queries the D3D11 device from the current EGL display, so decode, render and present all run on the one device created by `D3D11DeviceManager` (which already carries video support and multithread protection).
  - Task 2.2 ("Path 1") is adapted: `MpvRenderContext` is the API-agnostic render-context wrapper (`Create` takes any parameter block, `CreateOpenGL` is the path in use). If libmpv ever adds a D3D11 render API it slots in there without touching the loop.
  - The `gpu-next` flag from the roadmap is dropped: it has no effect on the render API. Low-latency options that do apply are set by `MpvContext` (`vo=libmpv`, `hwdec=d3d11va`, `demuxer-max-back-bytes=512MiB`, `video-latency-hacks=yes`).
  - ANGLE ships next to libmpv as `libEGL.dll` + `libGLESv2.dll`. Aether loads `libEGL.dll` before mpv creates its render context, so mpv's own `LoadLibrary("libEGL.dll")` binds to the same module.
  - The `sw` render API (CPU copy into the back buffer) is not implemented; it would only serve machines without ANGLE, and those don't exist once ANGLE ships with the app.
- **Status:** Accepted.

## ADR-0008: Render Thread Ownership and Device-Loss Recovery
- **Context:** ADR-0006 left recovery after device loss to Pass 2. Device loss can surface from DXGI (`Present`, `ResizeBuffers`) or from ANGLE (a context reset), and the swap chain must reach the `SwapChainPanel` on the UI thread without the UI ever waiting on the GPU.
- **Decision:**
  - `MpvRenderLoop` owns one dedicated thread ("Aether.Render") and the whole device stack: `D3D11DeviceManager` → `DxgiPresenter` → `IVideoRenderBackend` (ANGLE + mpv render context). The stack is built on that thread, because EGL contexts are bound per thread and mpv needs its GL context current to create and free a render context. It is torn down in reverse order.
  - Frame: wait on two kernel events (mpv's update callback signals one from native code; resize, redraw and stop requests signal the other) → `mpv_render_context_update` → frame-latency wait → `mpv_render_context_render` → `glFlush` → `Present` → `mpv_render_context_report_swap`. No managed allocation on that path (measured: 0 bytes over 100 frames on both backends).
  - Signal: `DxgiPresenter` latches the first loss in `DeviceLostReason` (the HRESULT, refined by `GetDeviceRemovedReason`); `IsDeviceLost` is a lock-free read, and a lost presenter returns `PresentResult.DeviceLost` without touching the swap chain. Backends report a lost GPU context by returning false from `Render`, and the loop forwards that through `ReportDeviceLost`. No logging-only paths remain.
  - Recovery: notify the host (`OnDeviceLost`), dispose backend, presenter and device, then build a new generation at once. Failed creation retries with exponential backoff (100 ms doubling to 2 s). Each new swap chain travels to the UI in a `SwapChainHandoff` that holds its own COM reference, so a second loss before the UI thread runs cannot leave a dangling pointer. The UI binds only the newest generation.
  - `Dispose` never blocks: it requests a stop and returns, and the thread releases everything on exit. The two kernel events close only once both `Dispose` and thread exit have happened.
  - With ANGLE on the shared immediate context, the presenter skips `ClearState` before `ResizeBuffers` (`ClearStateOnResize = false`). ANGLE caches D3D11 state, and destroying the pbuffer with no context current already drops its back-buffer references (verified on hardware and WARP).
- **Status:** Accepted. Not yet verified with libmpv loaded: whether mpv's d3d11va decoder re-binds to the new device after recovery.
