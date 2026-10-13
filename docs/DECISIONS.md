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
  - The `gpu-next` flag from the roadmap is dropped: it has no effect on the render API. Low-latency options that do apply are set by `MpvContext` (`vo=libmpv`, `hwdec=d3d11va`, `demuxer-max-back-bytes=512MiB`, `video-latency-hacks=yes`, except with software decoding, where mpv must keep a frame decoded ahead to avoid stutter).
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

## ADR-0009: Event Thread, Property Dispatch and Frame Stepping
- **Context:** Pass 3 needs mpv's events (property changes, command replies, end of file) on the UI without ever blocking the WinUI `DispatcherQueue`, and frame stepping that reacts at once to a held key without flooding mpv. The client API delivers events through `mpv_wait_event`, which blocks; its `mpv_event` memory is reused on the next call.
- **Decision:**
  - `MpvEventLoop` (Interop) owns one `TaskCreationOptions.LongRunning` thread ("Aether.MpvEvents") per core, waiting with an infinite timeout. Stop is a flag plus `mpv_wakeup`; mpv latches a wake-up that arrives while nobody waits, so set-flag-then-wake cannot be lost. `Dispose` never blocks; `Stop(timeout)` waits, except on the event thread itself.
  - The loop AddRefs the core's `MpvHandle` from `Start` until the thread exits, so `mpv_terminate_destroy` can never run under `mpv_wait_event`. `MpvContext.Dispose` marks the handle closed first and then calls `mpv_wakeup`, so a parked loop wakes, sees `IsClosed`, and drops its reference; the last reference (context, loop or render context) destroys the core.
  - Events reach one `IMpvEventSink` as `MpvEventView`, a ref struct over mpv's memory: nothing is copied, boxed or marshalled per event, and the view cannot outlive the call. Properties are observed under numeric ids (`reply_userdata`) and dispatched on the id, never by comparing names. A throwing sink is reported through `OnFault` and the loop continues. `MPV_EVENT_SHUTDOWN` ends the loop; `MPV_EVENT_QUEUE_OVERFLOW` is forwarded so the sink can re-read state.
  - Commands sent at key-repeat rate use `MpvCommand` (argv pre-encoded once on the pinned object heap) through `mpv_command_async`, so the UI thread never waits on the core and sending allocates nothing. Results come back as `MPV_EVENT_COMMAND_REPLY` with the caller's id.
  - Player logic that does not need WinUI lives in `Aether.Core.Playback` (event-to-state dispatch, `FrameStepper`, view-model state) so it is unit-testable; `Aether.App` only adapts it to `DispatcherQueue`, the render clock and key events.
  - UI dispatch is coalesced: the event thread writes property values into one pending state and requests a UI flush only on the clean-to-dirty transition; the UI thread applies the newest state at most once per display refresh. Intermediate values are dropped on purpose.
  - Frame stepping keeps at most one step in flight (cleared by its command reply); repeats that arrive while a step is in flight are dropped, not queued, so releasing the key stops stepping at once. `keep-open=yes` keeps the last frame loaded so stepping at the end of a file does not unload it.
- **Status:** Accepted. Not yet verified with libmpv loaded (no binary on the build machine): the event loop and dispatch are covered by tests against a fake event source.

## ADR-0010: Player Shell, Letterboxing and Startup
- **Context:** Pass 4 needs a borderless Windows 11 shell whose look follows the OpenMouse/Wintoys references (quiet, near-monochrome glass over a wallpaper-tinted backdrop), Mica behind the letterbox bars, controls that hide on idle, and a window that paints before libmpv has loaded.
- **Decision:**
  - The window extends content into the title bar and uses `MicaBackdrop Kind="BaseAlt"`; the stronger wallpaper tint matches the references. The page draws its own 32 px title bar strip (drag region via `SetTitleBar`); the system still draws the caption buttons, whose glyph colour goes transparent while the chrome is hidden.
  - Letterboxing: the `SwapChainPanel` is sized to the video's display aspect ratio (`dwidth`/`dheight`, observed as `Int64` and carried on `PlayerState`), centred in its host. The bars are therefore the host's background: Mica when windowed, black in full screen. This was chosen over a premultiplied-alpha swap chain with a transparent mpv clear colour, which would change the presenter and the ANGLE path for no gain.
  - Auto-hide: `ChromeVisibility` (Playback, UI-agnostic, tested with a manual clock) decides; holds (not playing, pointer over controls, drag or dialog) keep the chrome up. The fade animates each element's Composition visual opacity (`ChromeFader`), so it runs on the compositor. The cursor is hidden with `ShowCursor` through `CursorVisibility` (Interop), which steps the per-thread counter only across the shown/hidden boundary; WinUI has no hidden cursor shape.
  - Startup: `EngineStartup` probes for libmpv and creates the `MpvContext` on the thread pool; the UI thread then composes `PlayerSession` around it (event loop, dispatch, key input, surface). Missing binaries and initialization failures come back as a status, never as an exception, and the page shows them with Retry and Open folder. A startup abandoned by unload or retry disposes the engine it produced.
  - Commands from the shell (`loadfile`, `cycle pause`, `set volume`) go through `mpv_command_async`, so the UI never waits on the core.
- **Status:** Accepted. Not yet verified on screen: the packaged app needs Developer Mode to launch from the CLI, and an unpackaged build lacks the merged framework resources.

## ADR-0011: Settings Store
- **Context:** The Settings pages (Pass 6) need a model with defaults, per-setting reset and "applies on restart" flags, usable without WinUI.
- **Decision:**
  - `SettingsStore` and `AetherSettings` (an immutable record) live in `Aether.Core.Playback/Settings`. A table of `SettingDefinition`s (key, restart flag, allowed values) drives `Set`, `Reset`, load validation and the JSON file, so the UI binds by key and nothing is written twice.
  - The JSON is read with `JsonDocument` and written with `Utf8JsonWriter`, per key. That is reflection-free (Native AOT) and lets one invalid or unknown value fall back to its own default instead of failing the whole file, which a source-generated whole-object deserialiser cannot do.\n  - Storage is behind `ISettingsStorage`. `FileSettingsStorage` writes `settings.json.tmp` then replaces the file, at `%LOCALAPPDATA%\\Aether\\settings.json` (works packaged and unpackaged). It lives in Core.Playback so the atomic write is tested; `App.Settings` is the one shared instance, loaded on the thread pool at launch.
  - Restart-flagged settings: hardware decoding, passthrough, custom mpv options. `PendingRestart` compares them with the values at the last `Load`.
  - `MpvOptionsValidator` only checks the `key=value` shape; "unknown option" needs mpv's option list and is left to the Advanced page (6.5).
- **Status:** Accepted.

## ADR-0012: Label Casing and Shell Strings
- **Context:** The shell mixed all-caps captions (`SUPPORTED`, `DETAILS`), "press Ctrl O", coloured status text and two names for the renderer. The final design handoff (spec sheet, screen 4) fixes the wording.
- **Decision:**
  - Sentence case for every label, button, title, tooltip and status; no all-caps, no Title Case. Format names (MP4, WEBM), "URL", "ANGLE · D3D11" and file names keep their own spelling.
  - Shortcuts are written `Ctrl+O`; tooltips read "Label (Key)". The renderer is always "ANGLE · D3D11". Colour lives in status dots only; status text stays white.
  - Cascadia Mono only for timecodes. The controls' clock is `mm:ss`, with hours (`h:mm:ss`) only for clips of an hour or more.
  - Shell text comes from `Strings/en-us/Resources.resw` through `AppStrings` (`ResourceLoader`). Keys stay `Area_Name`, so `x:Uid` (which needs `Name.Text` keys) is not used; a test checks every key the app reads exists.
- **Status:** Accepted.

## ADR-0013: Surfaces over Video (Acrylic Spike)
- **Context:** The handoff puts menus, flyouts and the time bubble on SurfaceDialog (in-app Acrylic, blur behind) with a solid fallback, and gives the controls pill an OSD shadow (0 8 24, black 32%). The video is drawn by `MpvVideoSurface`, which derives from `SwapChainPanel`; the question was whether in-app Acrylic blurs it.
- **Decision:**
  - In-app `AcrylicBrush` blurs what the XAML compositor draws behind the element. A `SwapChainPanel`'s content is presented through its own DirectComposition surface and is not reliably part of that backdrop (the swap chain can present between XAML frames, and Microsoft's guidance is that Acrylic over a swap chain or `MediaPlayerElement` may sample nothing). Over video the brush would show its tint over an unblurred or empty backdrop, and its look would change with the render path.
  - Every surface over video therefore uses the solid `AetherSurfaceDialogFallbackBrush` (#F20E1014), which is what the handoff names as the fallback. The volume flyout already does; menus and the time bubble (Pass 5) follow the same rule. Mica behind the letterbox bars is unaffected: it samples the desktop, not the video.
  - The OSD shadow is not built yet. The pill is 62% ink, so a Composition `DropShadow` cast from it would show through and darken the pill unless masked out beneath it (a `CompositionMaskBrush` per resize), and `ThemeShadow` needs XAML receivers, so it would not fall on the swap chain and does not have the redline's offset or blur. Neither is cheap enough for Pass 4. The flyout keeps the system popup shadow it gets by default.
- **Status:** Accepted. Needs an on-screen check in the packaged app: confirm whether an `AcrylicBrush` over playing video blurs it (if it does, SurfaceDialog can switch back to Acrylic), and judge whether the pill needs its shadow over bright video.

## ADR-0014: Open Paths, Loop, Always on Top and Resume (Pass 5)
- **Context:** Pass 5 adds opening from the command line or Explorer, looping, a topmost window and resuming where a file was left (roadmap 5.7-5.10).
- **Decision:**
  - Resume positions are kept in `ResumeStore` (`%LOCALAPPDATA%\\Aether\\resume.json`), keyed by case-insensitive path, at most 200 entries (oldest pruned), and not kept within the first 5 s or last 10 s. The position is applied through mpv's global `start` option, set before every `loadfile` (to `none` when there is nothing to resume).
  - The app handles file activation and command-line arguments itself (`App.OnLaunched`, `LaunchArguments`) rather than redirecting to a single running instance, so the `OpenInRunningWindow` setting is not honoured yet.
  - Loop restarts once when `eof-reached` turns true, through the existing replay commands. `IsLooping` lives on `PlayerViewModel`, outside `PlayerState`.
- **Status:** Accepted. Needs an on-screen check in the packaged app: double-click and command-line launch, resume seeking (including `start=none`), loop restart and always-on-top after full screen.

## ADR-0015: Fast Developer & Agent App Execution (`run.ps1` & `dev-host`)
- **Context:** On-screen verification against design specs previously required either full MSIX packaging (`dist/Aether_Installer/Install.ps1`), launching through Visual Studio, or unpackaged builds (`WindowsPackageType=None`) which fail to render WinUI 3 XAML resources (black window). Furthermore, invoking `dotnet run` triggers NuGet package vulnerability audits and floating version resolution against `api.nuget.org`, failing in offline or sandboxed environments (`NU1301`/`NU1900`).
- **Decision:**
  - Provide `run.ps1` (and `tools/run.ps1`) for fast incremental builds (`-p:Platform=x64 --no-restore -warnaserror -p:NuGetAudit=false`) and in-place Developer Mode loose package registration (`Add-AppxPackage -Register AppxManifest.xml`). Once registered, subsequent incremental builds update in ~2-3 seconds and run directly from disk.
  - Launch using the application AUMID (`55AF92B1-5987-459F-B55A-89F4EE8B1B81_1z32rh13vfry6!App`).
  - Provide `tools/dev-host` (`.\run.ps1 -Host "path\to\video"`) to run `KestrelStreamServer` standalone on `localhost:8443` for web browser preview testing without opening the WinUI app.
  - Subagents and human developers can run `.\run.ps1` or `.\run.ps1 -NoBuild` during iterative passes to verify visual elements, layouts, and shortcuts.
- **Status:** Accepted.
