# Aether Media Player — Roadmap

**Design tasks (4.1–4.6, 5.1–5.3, 6.2, 6.3) run on Opus.** Before building any of them, Opus asks the user design questions and requests reference screenshots, then confirms the direction before writing UI code.

### Pass 1: DirectX 11 Pipeline & SwapChain Interop [Completed]

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 1.1 | `D3D11DeviceManager`: create D3D11 device (BGRA/video support, level 11.1, multithread protection) | M | Opus |
| 1.2 | `DxgiPresenter`: composition swap chain (`FLIP_DISCARD`, latency 1 waitable object, tearing flag) | M | Opus |
| 1.3 | Handle DPI resize (`ResizeBuffers`) and composition inverse matrix transform (`SetMatrixTransform`) | S | Opus |
| 1.4 | `MpvVideoSurface`: custom `SwapChainPanel` control wired to `ISwapChainPanelNative::SetSwapChain` | S | Sonnet |
| 1.5 | Unit test device creation, swap chain flags, and waitable latency handle | S | Gemini |

**Pass 1 means:** WinUI 3 hosts an active Direct3D 11 flip-model swap chain inside a XAML `SwapChainPanel` with 1-frame latency sync and zero composition errors. *(Completed: 42 tests passing)*

---

### Pass 2: libmpv Engine Initialization & Dual-Path Render Context [Completed]

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 2.1 | `MpvContext`: SafeHandle lifecycle wrapper with low-latency flags (`d3d11va`, 512MiB back-buffer; `gpu-next` dropped, see ADR-0007) | M | Opus |
| 2.2 | `MpvRenderContext`: API-agnostic render context wrapper (libmpv has no D3D11 render API, see ADR-0007) | M | Opus |
| 2.3 | `AngleBridge`: primary render path, ANGLE EGL client buffer mapping swap chain texture to FBO | M | Opus |
| 2.4 | `MpvRenderLoop`: dedicated render thread loop (`Render` → `Present` → `ReportSwap`, zero GC allocations) | M | Opus |
| 2.5 | Context lifecycle and unmanaged parameter block disposal tests | S | Gemini |

**Pass 2 means:** libmpv decodes video via `d3d11va` directly into the swap chain back buffer with zero allocations on the render hot path. *(Completed: 125 tests passing)*

---

### Pass 3: Dedicated MPV Event Loop & Navigation Engine [Completed]

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 3.1 | `MpvEventLoop`: dedicated background thread pumping `mpv_wait_event` with clean `mpv_wakeup` shutdown | M | Opus |
| 3.2 | Throttled UI dispatch: batch property events to `DispatcherQueue` at most once per display refresh | S | Sonnet |
| 3.3 | `FrameStepper`: bidirectional frame stepping (`,` and `.`) with hold-to-repeat acceleration | S | Sonnet |
| 3.4 | `PlayerViewModel`: struct-backed observable state for time, duration, speed, volume, and playback status | S | Sonnet |
| 3.5 | Unit tests for frame step commands, boundaries, repeat curves, and event dispatch | S | Gemini |
| 3.C1 | Carry-over: `MpvVideoSurface.AttachPlayer`/`DetachPlayer` core lifecycle management | S | Opus |
| 3.C2 | Carry-over: CI workflow Release build validation for `Aether.Core.Playback` | S | Gemini |

**Pass 3 means:** Frame-accurate stepping forward/backward with instant visual feedback and non-blocking event dispatch to the UI. *(Completed on `claude/pass-3`: 231 tests passing, 0 warnings)*

---

### Pass 4: Minimalist Windows 11 Shell & Mica Theming [In Progress]

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 4.1 | Custom window chrome: borderless window, `ExtendsContentIntoTitleBar`, blended title bar | S | Opus |
| 4.2 | `MicaTheme`: apply Windows 11 `MicaBackdrop` (Base Alt) behind letterboxing and controls; black letterbox in full screen | S | Opus |
| 4.3 | `DesignTokens`: neutral glass panels (`#730E1014`, controls `#9E0E1014`), faint 1px borders (`#FFFFFF0F`), Segoe UI Variable with Cascadia Mono for numbers | S | Opus |
| 4.4 | Auto-hiding OSD: Composition GPU opacity animation after 2s mouse idle + cursor hide | M | Opus |
| 4.5 | Drag-and-drop listener on main window for `.mp4`, `.mkv`, `.mov`, `.webm` | S | Opus |
| 4.6 | `StartupState`: off-UI-thread core initialization, loading transition, and missing binary/init failure presentation | M | Opus |
| 4.7 | `InputPolish`: fast-tap frame step queue/drop arbitration, window blur repeat release | S | Sonnet |
| 4.C1 | Carry-over: on-screen check of every shell state (empty, starting, failure, drag-over, playing, full screen) against the design; needs the packaged app (Developer Mode or Visual Studio) plus libmpv and a sample clip | S | Opus |

**Pass 4 means:** Edge-to-edge video player with translucent dark Mica chrome, auto-hiding OSD on mouse idle, graceful startup and error handling, and zero airspace clipping. *(4.1–4.6 built on `claude/pass-4`: 270 tests passing, 0 warnings. 4.7 built: fast taps queue in one slot; open: 4.C1.)*

---

### Pass 5: Core Player Controls & High-Performance Playback

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 5.1 | `PrecisionSeekBar`: fluent scrubber with hover timestamp preview, exact seek vs coarse drag scrubbing | M | Opus |
| 5.2 | `TransportControls`: full playback transport: jump ±5s / ±1s, frame step (`<` / `>`), speed control (0.25x-4.0x), volume slider & mute | M | Opus |
| 5.3 | `NavigationRail`: minimal collapsible side icon rail (Player, Playlist/Recent, Settings) overlaid above video with auto-hide | S | Opus |
| 5.4 | `PerformanceLoop`: async seek coalescing, zero-drop GPU decode verification, smooth 60fps+ UI under high-bitrate 1440p/4K load | M | Gemini |
| 5.5 | `LosslessTrimmer`: lightweight In (`I`) / Out (`O`) range selector on seek bar with sub-300ms `-c copy` export | S | Sonnet |
| 5.6 | Integration & benchmark tests: seek latency, frame-accurate boundaries, zero memory leaks across playback cycles | S | Gemini |

**Pass 5 means:** A responsive, high-performance daily-driver media player: butter-smooth scrubber with hover preview, comprehensive playback & audio transport controls, unobtrusive navigation rail, and instant lossless clipping without gimmicks.

---

### Pass 6: Embedded Kestrel LAN Streamer & Release Verification

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 6.1 | `KestrelStreamServer`: lightweight HTTP 206 partial content server with session token auth | M | Sonnet |
| 6.2 | Minimal HTML5 mobile player shell at `/watch?t={token}` with zero transcoding | S | Opus |
| 6.3 | `QrPairingService`: in-memory SVG/PNG QR code generator with local LAN IP flyout (`S` key) | S | Opus |
| 6.4 | Release hardening: Native AOT build (`dotnet publish -c Release -r win-x64`); fix the Release build's IL2104 trim warnings from `WinRT.Runtime` / `Microsoft.Windows.SDK.NET`, then move CI from Debug to Release | M | Gemini |
| 6.5 | End-to-end performance audit: cold start < 300ms, zero hot-path GC allocations | M | Opus |
| 6.6 | `EngineResilience`: attach/detach context cleanup, attached core disposal device-loss safety, queue overflow resync, background core destruction | M | Opus |
| 6.7 | `BinaryVerification`: libmpv and ANGLE binary integration, real media decode smoke test | S | Gemini |

**Pass 6 means:** Instant mobile pairing via QR code for LAN video streaming, fully verified Native AOT release build with 0 dropped frames.
