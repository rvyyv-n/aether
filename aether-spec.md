# Aether Media Player — Project Specification (v3)

**Aether** is an ultra-low-latency, minimalist Windows 11 media player engineered for high-framerate video playback (144Hz–360Hz), sub-millisecond competitive clip analysis, lossless stream-copy trimming, and zero-friction LAN streaming.

> **About this revision:** every feature, design element, component, file and milestone item from the original specification is kept, and section 0 maps each one to its location. Changes fall into two groups. **Fixes** correct technical errors in the original code or APIs without changing intent. **Additions** are new and are marked *(Added)*.

---

## 0. Feature Preservation Checklist

| # | Original feature / design element | Where |
|---|---|---|
| 1 | WinUI 3 presentation layer | §1, §4.5 |
| 2 | Windows 11 Mica material, Mica title bar blending, fluid Mica backdrops | §4.5 |
| 3 | Custom auto-hiding OSD with animated opacity on mouse idle | §4.5 |
| 4 | Sub-millisecond & game tick HUD (`GameTick = floor(TimeInSeconds * 20)`) | §4.2, §9.4 |
| 5 | Lossless In/Out trim bar, Mark IN (`I`) / Mark OUT (`O`) seek-bar indicators | §4.2, §4.3 |
| 6 | SwapChainPanel native interop bridge (`ISwapChainPanelNative`) | §1, §2, §9.2 |
| 7 | `IDXGISwapChain1` flip-model presentation, 144–360 Hz sync, `DXGI_SWAP_EFFECT_FLIP_DISCARD` | §2, §9.3 |
| 8 | `ID3D11Device5` & `ID3D11DeviceContext4` pipeline | §2, §9.3 |
| 9 | libmpv core engine (C-ABI), `gpu-next` pipeline, native HW acceleration | §2, §9.1 |
| 10 | Bidirectional frame-stepping buffer, demuxer max back-bytes | §4.2, §9.1 |
| 11 | `,` back / `.` forward frame stepping via `frame-back-step` / `frame-step` | §4.2, §11 |
| 12 | Low-latency params `--hwdec=d3d11va`, `--video-sync=display-resample` | §9.1 |
| 13 | Async MPV event loop on a dedicated thread pumping to view models | §2.3, §11 |
| 14 | FFmpeg `-c copy` lossless trim, sub-300 ms file generation | §4.3, §9.5 |
| 15 | Drag-to-export clip drawer into Discord / File Explorer | §4.3 |
| 16 | Embedded Kestrel server, dynamic/static local port, HTTP 206 partial content | §4.4, §9.6 |
| 17 | In-memory SVG/PNG QR code with LAN IP + stream endpoint | §4.4, §9.7 |
| 18 | Web-based minimal HTML5 player for scanned mobile devices | §4.4, §9.6 |
| 19 | Custom window chrome, edge-to-edge video | §4.5 |
| 20 | Fluent seek bar with high-precision timestamp tooltips | §4.5 |
| 21 | Drag-and-drop file open (`.mp4`, `.mkv`, `.mov`, `.webm`) | §4.1 |
| 22 | C# 13, .NET 9, Native AOT | §2.4 |
| 23 | Windows App SDK 1.6+ | §2.4, §11 |
| 24 | `[LibraryImport]` source-generated P/Invoke (no `[DllImport]`) | §6, §9.1 |
| 25 | Zero allocations on frame loops; guarded pointers; `HRESULT` validation | §5, §6 |
| 26 | Full repository structure and every original file | §3 |
| 27 | AGENTS.md: model role division (Claude Opus 5.5 / Claude Sonnet 5.5 / Gemini) and the rules of engagement | §6 |
| 28 | ROADMAP.md milestones 1–4, all items | §7 |
| 29 | CHANGELOG.md | §8 |
| 30 | Reference code: `MpvApi`, `ISwapChainPanelNative`, `TickCalculator`, `LosslessClipper`, `KestrelStreamServer` | §9 |
| 31 | Phase 1 backlog steps 1–4 | §11 |

---

## 1. System Architecture

~~~
┌────────────────────────────────────────────────────────────────────────┐
│                        WinUI 3 Presentation Layer                      │
│   • Windows 11 Mica Material               • Custom Auto-Hiding OSD    │
│   • Sub-Millisecond & Game Tick HUD        • Lossless In/Out Trim Bar  │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ XAML Visual Tree
┌───────────────────────────────────▼────────────────────────────────────┐
│                    SwapChainPanel Native Interop Bridge                │
│   • ISwapChainPanelNative (DirectX Interop COM Interface)              │
│   • IDXGISwapChain1 (Flip Model Presentation: 144Hz - 360Hz Sync)      │
│   • ID3D11Device5 & ID3D11DeviceContext4 Context Pipeline              │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Render Callbacks / Context
┌───────────────────────────────────▼────────────────────────────────────┐
│                       libmpv Core Engine (C-ABI)                       │
│   • gpu-next Rendering Pipeline           • Native HW Acceleration     │
│   • Bidirectional Frame Stepping Buffer   • Demuxer Max Back-Bytes     │
└───────────────┬────────────────────────────────────────┬───────────────┘
                │ Non-blocking Stream Pipe               │ Process Hook
┌───────────────▼──────────────┐         ┌───────────────▼──────────────┐
│  Server.LanHost (Kestrel)    │         │  PvP.Review (FFmpeg Worker)  │
│  • HTTP 206 Partial Content  │         │  • Lossless -c copy Trim     │
│  • In-Memory QR Code Pairing │         │  • Sub-300ms File Generation │
└──────────────────────────────┘         └──────────────────────────────┘
~~~

Because the video is presented through a `SwapChainPanel`, it is part of the XAML visual tree. Mica, the auto-hiding OSD, the HUD and the trim bar therefore compose directly over the video, with no airspace problems and no extra windows.

---

## 2. Rendering Pipeline (Fast Path)

### 2.1 How libmpv reaches the SwapChainPanel *(Fix)*

The original Phase 1 step asks for an `mpv_render_context` targeting the swap chain back buffer. Upstream libmpv's render API exposes OpenGL and software back ends. A native D3D11/DXGI render API type is not guaranteed in every build. To deliver the original architecture at full speed, use the following, in priority order:

| Path | How | Copies per frame | Use when |
|---|---|---|---|
| **1. Native DXGI render API** | `MPV_RENDER_API_TYPE` = DXGI/D3D11, with mpv rendering straight into the swap-chain back buffer | 0 | The bundled libmpv build exposes it (check at startup) |
| **2. ANGLE zero-copy (default)** | libmpv OpenGL render API on ANGLE (EGL over our `ID3D11Device5`). The swap-chain back buffer is wrapped as an EGL surface via `EGL_ANGLE_d3d_texture_client_buffer`, so mpv's FBO *is* the back buffer | 0 | Always available |

Both paths keep the original stack unchanged: `SwapChainPanel` → `ISwapChainPanelNative::SetSwapChain` → `IDXGISwapChain1` (flip discard) → `ID3D11Device5` / `ID3D11DeviceContext4` → libmpv `gpu-next`.

### 2.2 Swap-chain configuration for 144–360 Hz
- `DXGI_SWAP_EFFECT_FLIP_DISCARD`, 2 buffers, `DXGI_FORMAT_B8G8R8A8_UNORM` (or `R10G10B10A2_UNORM` for HDR).
- `DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT` + `SetMaximumFrameLatency(1)`: the render thread waits on the latency handle before each frame, giving minimum queue depth and minimum latency.
- `DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING` available as an opt-in "competitive" mode (`Present(0, DXGI_PRESENT_ALLOW_TEARING)`); the default is vsync `Present(1, 0)`.
- Created with `CreateSwapChainForComposition` (required for `SwapChainPanel`).
- Handle `SizeChanged` and `CompositionScaleChanged` with `ResizeBuffers` plus `SetMatrixTransform` (inverse scale) for crisp DPI-correct output.

### 2.3 Threading model *(Added)*
| Thread | Responsibility |
|---|---|
| UI (`DispatcherQueue`) | XAML, input, view models. Never blocks |
| Render thread | Waits on the swap-chain latency handle, then calls `mpv_render_context_render`, `Present`, and `mpv_render_context_report_swap` (gives mpv real vsync timing so `display-resample` paces correctly) |
| MPV event thread | `mpv_wait_event` loop (`TaskCreationOptions.LongRunning`). Posts batched property changes to the UI at most once per display refresh |
| Export / server | Thread pool; fully isolated from playback |

The mpv update callback (`mpv_render_context_set_update_callback`) only signals an `AutoResetEvent`. It does no work and makes no allocations.

### 2.4 Core Technology Stack
- **Language & Runtime:** C# 13, .NET 9, configured for **Native AOT** compilation (supported for WinUI 3 from Windows App SDK 1.6).
- **UI Framework:** Windows App SDK (WinUI 3) with the native Windows 11 Mica backdrop.
- **Playback Backend:** `libmpv` (v0.38+ / latest C-ABI) via the `gpu-next` rendering pipeline.
- **Graphics Interop:** Direct3D 11 and a DXGI swap chain hosted in `SwapChainPanel` via `ISwapChainPanelNative`.
- **Network Server:** Embedded ASP.NET Core Kestrel HTTP server (byte-range streaming).
- **Export Pipeline:** Managed background process execution targeting bundled `ffmpeg.exe`.
- *(Added)* **QR generation:** QRCoder (managed, AOT-safe), with SVG/PNG produced in memory.
- *(Added)* **Testing:** xUnit; FlaUI for UI smoke tests.

---

## 3. Repository Structure

All original files are kept. Files marked *(Added)* are new.

~~~
AetherPlayer/
├── .github/
│   └── workflows/
│       └── build-aot.yml
├── docs/
│   ├── AGENTS.md
│   ├── ROADMAP.md
│   ├── CHANGELOG.md
│   └── DECISIONS.md                 # (Added) Architecture decision records
├── src/
│   ├── Aether.App/                  # WinUI 3 Entry Point & Shell
│   │   ├── Views/
│   │   │   ├── MainWindow.xaml
│   │   │   ├── MainWindow.xaml.cs
│   │   │   └── OverlayHud.xaml
│   │   ├── ViewModels/
│   │   │   ├── PlayerViewModel.cs
│   │   │   └── ClipTrimViewModel.cs
│   │   ├── Controls/
│   │   │   ├── MpvVideoSurface.cs   # Custom SwapChainPanel Control
│   │   │   ├── PrecisionSeekBar.cs  # (Added) Fluent seek bar + In/Out markers
│   │   │   └── ClipDrawer.xaml      # (Added) Drag-to-export drawer
│   │   ├── Styles/
│   │   │   ├── DesignTokens.xaml    # Colors, Fonts, Margins
│   │   │   └── MicaTheme.xaml
│   │   ├── Native/x64/              # mpv-2.dll (+ deps), ffmpeg.exe
│   │   └── App.xaml
│   │
│   ├── Aether.Core.Interop/         # P/Invoke, C-ABI Bindings, DirectX
│   │   ├── Mpv/
│   │   │   ├── MpvApi.cs            # [LibraryImport] Signatures
│   │   │   ├── MpvTypes.cs          # Structs, Enums, Handles
│   │   │   ├── MpvContext.cs        # SafeHandle Lifecycle Wrapper
│   │   │   ├── MpvRenderContext.cs  # mpv_render_context bindings
│   │   │   └── MpvEventLoop.cs      # (Added) Dedicated event thread
│   │   ├── DirectX/
│   │   │   ├── D3D11DeviceManager.cs
│   │   │   ├── ISwapChainPanelNative.cs
│   │   │   └── DxgiPresenter.cs
│   │   └── Angle/
│   │       └── AngleBridge.cs       # (Added) EGL zero-copy back-buffer binding
│   │
│   ├── Aether.PvP.Review/           # High-FPS Frame Analysis & Clipping
│   │   ├── Analysis/
│   │   │   ├── FrameStepper.cs      # Bidirectional navigation engine
│   │   │   └── TickCalculator.cs    # Millisecond to Minecraft Tick logic
│   │   └── Clipping/
│   │       ├── ExportRegion.cs      # Mark In/Out data structures
│   │       └── LosslessClipper.cs   # FFmpeg stream-copy process wrapper
│   │
│   └── Aether.Server.LanHost/       # Embedded Streaming & Pairing
│       ├── KestrelStreamServer.cs   # Embedded HTTP 206 Host
│       ├── StreamingMiddleware.cs   # Range-request pipeline
│       └── QrPairingService.cs      # Local IP QR Generation
│
├── tests/
│   ├── Aether.Core.Tests/
│   ├── Aether.PvP.Tests/
│   └── Aether.Server.Tests/         # (Added)
├── AetherPlayer.sln
└── Directory.Build.props
~~~

---

## 4. Functional Requirements

### 4.1 Playback
- File drop listener supporting drag-and-drop of `.mp4`, `.mkv`, `.mov`, `.webm`.
- *(Added)* Open via file dialog, command-line argument, and file association.
- Hardware decoding (`d3d11va`), `gpu-next` rendering, `display-resample` sync.
- *(Added)* Play/pause, seek, speed 0.1x–4x, volume, loop, fullscreen, always-on-top, resume position per file.

### 4.2 PvP Review Toolkit
- Bidirectional frame stepping: `,` backward, `.` forward, mapped to `frame-back-step` / `frame-step`. *(Added)* Hold to auto-repeat.
- `demuxer-max-back-bytes` sized so backward stepping stays inside the cache and stays instant.
- **Sub-millisecond & Minecraft Tick HUD overlay.** Display formula: `GameTick = floor(TimeInSeconds * 20)`. Shows milliseconds, game tick and sub-tick percentage. *(Added)* Frame number, source FPS, configurable tick rate.
- Mark IN (`I`) and Mark OUT (`O`) with visual bounding indicators on the seek bar.
- *(Added)* Keyframe indicator on In/Out, since stream copy starts at a keyframe.

### 4.3 Lossless Export
- Instant lossless trimming via FFmpeg stream copy (`-c copy`), with no re-encode, preserving native 240/360 fps streams.
- Target: sub-300 ms file generation.
- Drag-to-export clip drawer: drag the finished clip directly into Discord or File Explorer (`DataPackage` with `StorageFile`).
- *(Added)* Atomic write (`.part` then rename), cancellation, `Ctrl+E` shortcut.

### 4.4 Embedded LAN Streamer & Pairing
- Lightweight Kestrel server on a dynamic or static local port (default 8443).
- Serves the local media file through HTTP 206 partial-content byte ranges (`/stream/video`).
- In-memory SVG/PNG QR code encoding the local LAN IP and stream endpoint (`/watch`).
- Web-based minimal HTML5 player interface for scanned mobile devices.
- *(Added)* Per-session random token in the QR URL; serves only the active file; stops on exit.

### 4.5 Design: Minimal Windows 11
- **Custom window chrome:** zero window borders, `ExtendsContentIntoTitleBar`, edge-to-edge video, Mica title bar blending.
- **Fluid Mica backdrops:** `MicaBackdrop` (Base) on the window, visible through the title bar region and around letterboxed video. The video surface is opaque where it draws, so Mica costs nothing during playback.
- **Custom auto-hiding OSD:** control bar with animated opacity transitions on mouse idle (2 s), using Composition implicit animations (opacity only, GPU-side, no layout pass). The cursor hides with it.
- **Fluent seek bar** with high-precision timestamp tooltips (`HH:MM:SS.mmm`) and In/Out markers.
- **High-contrast analytical typography:** Segoe UI Variable for chrome; Cascadia Mono (tabular digits) for the HUD so numbers never jitter.
- **Lossless In/Out trim bar** integrated into the seek bar.
- All colours, fonts and margins come from `DesignTokens.xaml`; Mica behaviour from `MicaTheme.xaml`.
- *(Added)* HUD text updates only on value change, at most once per refresh, via `x:Bind` one-way bindings to a struct-backed view model, with no per-frame allocations.
- *(Added)* Respects system reduced-motion settings.

### 4.6 Default Keyboard Map *(Added)*
| Key | Action |
|---|---|
| Space | Play / pause |
| `,` / `.` | Frame back / forward |
| ← / → | Seek −5 s / +5 s (Shift 1 s, Ctrl 30 s) |
| `I` / `O` | Mark In / Out |
| Ctrl+E | Export clip |
| `F` / Esc | Fullscreen / exit |
| `H` | Toggle HUD |
| `S` | Toggle LAN share / QR |

---

## 5. Non-Functional Requirements *(Added)*

| Metric | Target |
|---|---|
| Cold start to first frame (AOT) | < 300 ms |
| Frame step latency | ≤ 1 display refresh |
| Lossless export, 30 s clip | < 300 ms |
| Dropped frames at 240 Hz, 10 min | 0 |
| Allocations on render/event hot path | 0 |

- GC: `ServerGarbageCollector=false`, `ConcurrentGarbageCollection=false`, `GCSettings.LatencyMode = SustainedLowLatency` during playback.
- Security: ffmpeg arguments via `ArgumentList`; LAN token; no telemetry.
- Licensing: an LGPL libmpv build, dynamically linked; FFmpeg run as a separate process; documented in `THIRD_PARTY.md`.

---

## 6. `docs/AGENTS.md`

~~~markdown
# Operational Instructions for AI Agents (Antigravity / Claude)

## Role Division & Model Assignment
- **Claude Opus 5.5 / Reasoning Specialists:**
  - Strictly assigned to `Aether.Core.Interop`.
  - Authoring low-level unsafe structs, P/Invoke signatures, DirectX COM wrappers (`ISwapChainPanelNative`), and memory lifetime safety.
  - Verification of zero unmanaged memory leaks and thread safety on the MPV event loop.
- **Claude Sonnet 5.5 / Velocity Specialists:**
  - Assigned to `Aether.App`, `Aether.PvP.Review`, and `Aether.Server.LanHost`.
  - WinUI 3 XAML styling, MVVM wiring, keyboard accelerators, and FFmpeg command builder integration.
- **Gemini / Context Refactoring:**
  - Large header ingestions (`mpv.h`, `render.h`), multi-file project restructuring, build issue resolution, and test generation.

## Technical Rules of Engagement
1. **P/Invoke Standard:** DO NOT use legacy `[DllImport]`. Use modern .NET 9 source-generated `[LibraryImport]` with explicit marshaling to ensure Native AOT compatibility.
2. **Memory Allocation:** Zero allocations on frame-rendering loops. Interop callbacks from `libmpv` must not marshal objects across every tick.
3. **No Thread Starvation:** The `mpv_wait_event` listener must operate on a dedicated long-running background thread (`TaskCreationOptions.LongRunning`). Never block the WinUI `DispatcherQueue`.
4. **Error Handling:** Unmanaged pointer operations must be guarded. Direct3D calls must validate `HRESULT` returns with `Marshal.ThrowExceptionForHR` or explicit failure logging.
5. **No Visual Bloat:** Adhere strictly to the minimal Windows 11 design philosophy: zero window borders, fluid Mica backdrops, auto-hiding scrubbers, and high-contrast analytical typography.
6. **(Added) Native callbacks:** Use `[UnmanagedCallersOnly]` + function pointers (`delegate* unmanaged[Cdecl]`) for libmpv callbacks — no managed delegates (AOT-safe, GC-safe).
7. **(Added) Process arguments:** Build external process arguments with `ProcessStartInfo.ArgumentList`.
8. **(Added) Decisions:** Record architecture choices in `docs/DECISIONS.md`.
~~~

---

## 7. `docs/ROADMAP.md`

~~~markdown
# Project Roadmap: Aether Media Player

## (Added) Milestone 0: Verification Spike
- [ ] Detect whether the bundled libmpv exposes a native DXGI render API; otherwise validate the ANGLE zero-copy path.
- [ ] Verify Native AOT publish of the WinUI 3 app with libmpv loaded.

## Milestone 1: Core Playback Engine & Native Rendering (Sprint 1)
- [ ] Implement C# .NET 9 `[LibraryImport]` source-generated bindings for `libmpv`.
- [ ] Implement `ISwapChainPanelNative` interop with Direct3D11 flip-model swapchain.
- [ ] Connect `libmpv` `gpu-next` render context to the WinUI 3 `SwapChainPanel`.
- [ ] Configure low-latency parameters (`--hwdec=d3d11va`, `--video-sync=display-resample`).
- [ ] Implement asynchronous MPV event loop pumping events to UI ViewModels.

## Milestone 2: Minimalist Mica Interface & UX (Sprint 2)
- [ ] Custom window chrome with edge-to-edge video and Mica titlebar blending.
- [ ] Auto-hiding control bar with animated opacity transitions on mouse idle.
- [ ] Fluent seek bar with high-precision timestamp tooltips.
- [ ] File drop listener (`.mp4`, `.mkv`, `.mov`, `.webm`) supporting drag-and-drop.

## Milestone 3: PvP Review Toolkit & Lossless Export (Sprint 3)
- [ ] Bidirectional frame-stepping binding (`,` for backward, `.` for forward).
- [ ] Sub-millisecond & Minecraft Tick HUD overlay:
      Display formula: GameTick = floor(TimeInSeconds * 20)
- [ ] Mark IN (`I`) and Mark OUT (`O`) visual bounding indicators on seek bar.
- [ ] Lossless instant trimming engine via FFmpeg stream-copy (`-c copy`).
- [ ] Drag-to-export clip drawer directly into Discord/file explorer.

## Milestone 4: Embedded LAN Streamer & Pairing (Sprint 4)
- [ ] Embed lightweight Kestrel server listening on dynamic/static local port.
- [ ] Serve local media file through HTTP 206 Partial Content byte ranges.
- [ ] Generate in-memory SVG/PNG QR code encoding local LAN IP and stream endpoint.
- [ ] Web-based minimal HTML5 player interface for scanned mobile devices.

## (Added) Milestone 5: Polish & Release
- [ ] Performance pass against §5 targets, opt-in tearing mode, HDR output.
- [ ] Settings, file associations, MSIX + portable packaging.
~~~

---

## 8. `docs/CHANGELOG.md`

~~~markdown
# Changelog

All notable changes to this project will be documented in this file.
The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]
### Added
- Repository initialization specification.
- Architecture blueprints for WinUI 3 SwapChainPanel and libmpv D3D11 interop.
- PvP analysis toolset design specifications (Frame stepping, tick HUD, lossless trimming).
- Embedded Kestrel streaming host architecture with local QR code generation.
~~~

---

## 9. Technical Implementation Specifications

> These are reference sketches; verify them against the actual `mpv/render.h` of the bundled build.

### 9.1 `src/Aether.Core.Interop/Mpv/MpvApi.cs`

*(Fixes: render-param enum values now match upstream `render.h`; render calls take a parameter **array**; callbacks use unmanaged function pointers for AOT; `mpv_command` marshals a null-terminated UTF-8 array.)*

~~~csharp
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

    // args: NULL-terminated array of UTF-8 C strings (built by MpvContext without per-call GC allocs)
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

    // libmpv render API
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

[StructLayout(LayoutKind.Sequential)]
public struct MpvRenderParam
{
    public MpvRenderParamType Type;
    public IntPtr Data;
}

// Values per upstream mpv/render.h
public enum MpvRenderParamType
{
    Invalid = 0,
    ApiType = 1,
    OpenGLInitParams = 2,
    OpenGLFbo = 3,
    FlipY = 4,
    Depth = 5,
    IccProfile = 6,
    AmbientLight = 7,
    X11Display = 8,
    WlDisplay = 9,
    AdvancedControl = 10,
    NextFrameInfo = 11,
    BlockForTargetTime = 12,
    SkipRendering = 13,
    DrmDisplay = 14,
    DrmDrawSurfaceSize = 15,
    DrmDisplayV2 = 16,
    SwSize = 17,
    SwFormat = 18,
    SwStride = 19,
    SwPointer = 20
}

public enum MpvFormat { None = 0, String = 1, OsdString = 2, Flag = 3, Int64 = 4, Double = 5, Node = 6 }

[StructLayout(LayoutKind.Sequential)]
public struct MpvEvent
{
    public int EventId;
    public int Error;
    public ulong ReplyUserdata;
    public IntPtr Data;
}
~~~

Low-latency initialization profile:

~~~csharp
mpv.SetOption("vo", "libmpv");
mpv.SetOption("gpu-next", "yes");                    // or vo=gpu-next per build
mpv.SetOption("hwdec", "d3d11va");                   // zero-copy decode on the shared D3D11 device
mpv.SetOption("video-sync", "display-resample");
mpv.SetOption("video-timing-offset", "0");
mpv.SetOption("demuxer-max-back-bytes", "512MiB");   // instant backward frame stepping
mpv.SetOption("demuxer-max-bytes", "512MiB");
mpv.SetOption("hr-seek", "yes");                     // frame-accurate seeks
mpv.SetOption("hr-seek-framedrop", "no");
mpv.SetOption("keep-open", "yes");
mpv.SetOption("osc", "no");                          // Aether draws its own OSD
mpv.SetOption("input-default-bindings", "no");
~~~

Render-thread loop (no allocations):

~~~csharp
while (_running)
{
    WaitForSingleObjectEx(_frameLatencyWaitable, 1000, true);   // DXGI waitable swap chain
    _renderSignal.WaitOne();                                    // set by mpv update callback
    if ((MpvApi.RenderContextUpdate(_rctx) & 1) == 0) continue; // MPV_RENDER_UPDATE_FRAME
    RenderIntoBackBuffer();                                     // path 1 or 2 (§2.1)
    _swapChain.Present(_tearing ? 0u : 1u, _tearing ? DXGI_PRESENT_ALLOW_TEARING : 0u);
    MpvApi.RenderContextReportSwap(_rctx);
}
~~~

### 9.2 `src/Aether.Core.Interop/DirectX/ISwapChainPanelNative.cs`

*(Fix: the original GUID matched neither interface. WinUI 3 `Microsoft.UI.Xaml` uses `63AAD0B8-7C24-40FF-85A8-640D944CC325`; the UWP `Windows.UI.Xaml` version is `F92F19D2-3ADE-45A6-A20C-F6F1EA90554B`. Verify against `microsoft.ui.xaml.media.dxinterop.h`.)*

~~~csharp
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Aether.Core.Interop.DirectX;

[GeneratedComInterface]
[Guid("63AAD0B8-7C24-40FF-85A8-640D944CC325")]
public partial interface ISwapChainPanelNative
{
    [PreserveSig]
    int SetSwapChain(IntPtr swapChain);
}
~~~

### 9.3 `src/Aether.Core.Interop/DirectX/D3D11DeviceManager.cs` / `DxgiPresenter.cs` *(outline)*

~~~csharp
// D3D11DeviceManager
//   D3D11CreateDevice(adapter: null, D3D_DRIVER_TYPE_HARDWARE,
//       flags: D3D11_CREATE_DEVICE_BGRA_SUPPORT | VIDEO_SUPPORT, levels: 11_1, 11_0)
//   -> QueryInterface ID3D11Device5 / ID3D11DeviceContext4
//   -> ID3D10Multithread::SetMultithreadProtected(true)   // shared with d3d11va decode
//
// DxgiPresenter
//   IDXGIFactory2::CreateSwapChainForComposition(device, desc {
//       Width/Height = panel size * CompositionScale, Format = B8G8R8A8_UNORM,
//       BufferCount = 2, SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD,
//       AlphaMode = DXGI_ALPHA_MODE_IGNORE,
//       Flags = FRAME_LATENCY_WAITABLE_OBJECT | ALLOW_TEARING })
//   -> IDXGISwapChain2::SetMaximumFrameLatency(1); GetFrameLatencyWaitableObject()
//   -> ISwapChainPanelNative::SetSwapChain(swapChain)  (UI thread)
//   -> on resize: ResizeBuffers + IDXGISwapChain2::SetMatrixTransform(1/scale)
~~~

### 9.4 `src/Aether.PvP.Review/Analysis/TickCalculator.cs`

~~~csharp
using System;

namespace Aether.PvP.Review.Analysis;

public readonly record struct PlaybackMetric(
    double TimestampSeconds,
    long Milliseconds,
    long GameTick,
    long SubTickPercentage,
    long FrameNumber          // (Added)
);

public static class TickCalculator
{
    private const double SecondsPerTick = 0.050; // 50ms per Minecraft tick (20 ticks/sec)

    public static PlaybackMetric Compute(double timePosSeconds, double sourceFps = 0, double secondsPerTick = SecondsPerTick)
    {
        if (timePosSeconds < 0) timePosSeconds = 0;

        long ms = (long)(timePosSeconds * 1000.0);
        double tickPos = timePosSeconds / secondsPerTick;
        long tick = (long)Math.Floor(tickPos);              // GameTick = floor(TimeInSeconds * 20)
        long subTickPct = (long)((tickPos - tick) * 100.0);
        long frame = sourceFps > 0 ? (long)Math.Round(timePosSeconds * sourceFps) : 0;

        return new PlaybackMetric(timePosSeconds, ms, tick, subTickPct, frame);
    }
}
~~~

### 9.5 `src/Aether.PvP.Review/Clipping/LosslessClipper.cs`

*(Fixes: the original returned `string` from a non-generic `Task` method, which does not compile. Arguments now go through `ArgumentList`, the output is written atomically, and stderr is drained concurrently so a full pipe cannot deadlock the process.)*

~~~csharp
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Aether.PvP.Review.Clipping;

public record ClipRequest(
    string SourceFilePath,
    double StartTimeSeconds,
    double EndTimeSeconds,
    string DestinationDirectory,
    string ClipNamePrefix = "Aether_PvP_Clip"
);

public static class LosslessClipper
{
    public static async Task<string> ExportLosslessAsync(
        ClipRequest request, string ffmpegPath = "ffmpeg.exe", CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.SourceFilePath))
            throw new FileNotFoundException("Source video file not found", request.SourceFilePath);

        if (request.EndTimeSeconds <= request.StartTimeSeconds)
            throw new ArgumentException("End timestamp must be strictly greater than start timestamp.");

        Directory.CreateDirectory(request.DestinationDirectory);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string extension = Path.GetExtension(request.SourceFilePath);
        string outputPath = Path.Combine(request.DestinationDirectory, $"{request.ClipNamePrefix}_{timestamp}{extension}");
        string tempPath = outputPath + ".part";

        string startStr = request.StartTimeSeconds.ToString("F3", CultureInfo.InvariantCulture);
        string durationStr = (request.EndTimeSeconds - request.StartTimeSeconds).ToString("F3", CultureInfo.InvariantCulture);

        // FFmpeg stream-copy: zero re-encoding, preserving native 240/360fps streams
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true
        };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-ss", startStr, "-i", request.SourceFilePath,
                                  "-t", durationStr, "-map", "0", "-c", "copy",
                                  "-avoid_negative_ts", "make_zero", "-f", MuxerFor(extension), "-y", tempPath })
            startInfo.ArgumentList.Add(a);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            TryDelete(tempPath);
            throw;
        }

        if (process.ExitCode != 0)
        {
            TryDelete(tempPath);
            throw new InvalidOperationException($"FFmpeg stream copy failed with exit code {process.ExitCode}: {await stderr}");
        }

        File.Move(tempPath, outputPath, overwrite: true);
        return outputPath;
    }

    private static string MuxerFor(string ext) => ext.ToLowerInvariant() switch
    {
        ".mkv" => "matroska",
        ".webm" => "webm",
        ".mov" => "mov",
        _ => "mp4"
    };

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
~~~

> **Sub-300 ms:** `-ss` before `-i` performs an index seek, and stream copy does no decoding, so export time is bound by disk I/O. FFmpeg is pre-warmed on app idle (one `-version` call) so the first export does not pay a cold-start cost.

### 9.6 `src/Aether.Server.LanHost/KestrelStreamServer.cs`

*(Fixes: the HTML that copy-paste had stripped is restored; range processing is explicit so responses are true HTTP 206; the slim builder is used for AOT; the IP probe never sends traffic.)*

~~~csharp
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Aether.Server.LanHost;

public sealed class KestrelStreamServer : IAsyncDisposable
{
    private WebApplication? _app;
    private string? _currentlyHostedFilePath;
    public int ActivePort { get; private set; } = 8443;                 // static default; 0 = dynamic
    public string SessionToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    public void SetActiveFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            _currentlyHostedFilePath = filePath;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Any, ActivePort));
        _app = builder.Build();

        // HTTP 206 Partial Content Stream
        _app.MapGet("/stream/video", (string t) =>
        {
            if (t != SessionToken) return Results.Unauthorized();
            if (string.IsNullOrEmpty(_currentlyHostedFilePath) || !File.Exists(_currentlyHostedFilePath))
                return Results.NotFound();
            return Results.File(_currentlyHostedFilePath, ContentTypeFor(_currentlyHostedFilePath),
                                enableRangeProcessing: true);
        });

        // Mobile Video Viewer Shell
        _app.MapGet("/watch", (string t) =>
        {
            if (t != SessionToken) return Results.Unauthorized();
            string html = $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>Aether LAN Player</title>
                    <style>
                        html, body { margin: 0; height: 100%; background: #000; }
                        video { width: 100%; height: 100%; object-fit: contain; }
                    </style>
                </head>
                <body>
                    <video src="/stream/video?t={{t}}" controls autoplay playsinline></video>
                </body>
                </html>
                """;
            return Results.Content(html, "text/html");
        });

        await _app.StartAsync(cancellationToken);
        if (ActivePort == 0) ActivePort = new Uri(_app.Urls.First()).Port;  // dynamic port resolution
    }

    public string GetLocalIpAddress()
    {
        // UDP connect selects the outbound interface without sending packets
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
        socket.Connect("10.255.255.255", 1);
        var endPoint = socket.LocalEndPoint as IPEndPoint;
        return endPoint?.Address.ToString() ?? "127.0.0.1";
    }

    public string GetMobileWatchUrl() => $"http://{GetLocalIpAddress()}:{ActivePort}/watch?t={SessionToken}";

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        _ => "application/octet-stream"
    };

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
~~~

> `StreamingMiddleware.cs` (original file) holds shared range, header and caching logic (`Accept-Ranges: bytes`, `Cache-Control: no-store`) applied ahead of the endpoints.
> Mobile browsers natively play H.264/AAC MP4 and WebM. For HEVC or MKV sources the UI shows a compatibility note.

### 9.7 `src/Aether.Server.LanHost/QrPairingService.cs` *(Added sketch)*

~~~csharp
using QRCoder;

namespace Aether.Server.LanHost;

public static class QrPairingService
{
    public static string CreateSvg(string url)
    {
        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(8, "#FFFFFF", "#00000000");   // white on transparent (Mica-friendly)
    }

    public static byte[] CreatePng(string url)
    {
        using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(8);
    }
}
~~~

---

## 10. Testing Strategy *(Added)*
- **Unit:** `TickCalculator` (tick boundaries at 0.050 s multiples, negatives, custom tick rates); `ClipRequest` validation; URL/token building.
- **Integration:** `LosslessClipper` against a 240 fps fixture: check the duration is within one GOP, streams are preserved, no `.part` file is left behind, and export takes under 300 ms. Kestrel `Range: bytes=0-99` must return 206 with a correct `Content-Range`.
- **Interop:** 1000 create/destroy cycles with no handle or VRAM leaks; clean event-loop shutdown via `mpv_wakeup`.
- **Performance:** frame-time capture at 144/240/360 Hz (PresentMon), dropped-frame counter, and a zero-allocation assertion on the render loop (`GC.GetAllocatedBytesForCurrentThread`).

---

## 11. Phase 1 Task Backlog & Execution Steps

Execute the following steps in sequence:

1. **Step 1 (Scaffold Workspace):**
   - Generate `AetherPlayer.sln` targeting `.NET 9` and Windows App SDK 1.6+.
   - Enable `<PublishAot>true</PublishAot>` and `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in `Directory.Build.props`. *(The original copy lost these tag names; they are reconstructed from context.)*
   - *(Added)* `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<LangVersion>13</LangVersion>`.

2. **Step 2 (Import Unmanaged Binaries):**
   - Place `mpv-2.dll` and dependencies in `src/Aether.App/Native/x64/`.
   - Configure project files to output native binaries directly to the working directory.
   - *(Added)* Bundle `ffmpeg.exe` alongside; pin SHA-256 hashes.

3. **Step 3 (Implement SwapChain Rendering Pipeline):**
   - Instantiate `ID3D11Device5` and a DXGI swap chain configured with `DXGI_SWAP_EFFECT_FLIP_DISCARD` (plus waitable and tearing flags, §2.2).
   - Bind the swap chain to `ISwapChainPanelNative::SetSwapChain`.
   - Initialize `mpv_render_context` targeting the swap-chain back buffer. Use `MPV_RENDER_PARAM_API_TYPE` = DXGI if the build exposes it; otherwise use the ANGLE zero-copy path (§2.1).

4. **Step 4 (Wire Event Loop & Controls):**
   - Spawn the `mpv_wait_event` pump on a dedicated thread.
   - Connect `,` (frame back) and `.` (frame forward) keystrokes directly to `mpv_command("frame-back-step")` and `mpv_command("frame-step")`.
   - Render the Sub-Millisecond & Game Tick HUD using WinUI 3 Canvas or TextBlock elements with low-latency XAML composition.

---

## 12. Open Questions *(Added)*
- Which libmpv build (shinchiro / zhongfly / custom) will be bundled, and does it expose a DXGI render API?
- Should opt-in tearing mode ship in v1?
- Should keyframe-precise export (re-encode only the head GOP) be added later as a non-default option?