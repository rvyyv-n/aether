# Roadmap Scout & Implementation Analysis: Passes 8–11

## Executive Summary

This report surveys the codebase and implementation requirements for Passes 8 through 11 in `docs/ROADMAP.md`:
- **Pass 8 (Picture in Picture):** Windows App SDK `CompactOverlay` window with seamless swap-chain/presenter hand-over without stopping decoding.
- **Pass 9 (Link Playback with yt-dlp):** URL resolution, loading/failure states in the hero, stream handoff to mpv, and link recents.
- **Pass 10 (Precise Clip Export):** Hardware-accelerated re-encode pipeline (NVENC / QSV / AMF / x264), bitrate/size constraints, and progress reporting.
- **Pass 11 (LAN Streamer & Native AOT):** Embedded Kestrel server pairing flyout, zero-transcode mobile shell, release trimming/IL2104 resolution, and engine resilience.

Because an active design task is concurrently modifying `MainPage.xaml`, `MainPage.xaml.cs`, `MainWindow.xaml.cs`, and `DesignTokens.xaml`, source code must remain untouched and `dotnet build`/`test` must not run. Fortunately, **a significant portion of Passes 8–11 consists of pure Core and Engine work** (`Aether.Core.Interop`, `Aether.Core.Playback`, `Aether.PvP.Review`, `Aether.Server.LanHost`, and standalone tests) that can be developed and validated in isolation before UI integration begins.

---

## Pass 8: Picture in Picture

### 1. Existing Assets
- **Windowing & Presenters:**
  - [`MainWindow.xaml.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.App/MainWindow.xaml.cs): Manages the `AppWindow` and toggles presenters between `Overlapped` and `FullScreen`. Implements [`IShellWindow`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.App/MainWindow.xaml.cs#L19).
  - [`AspectRatioLock.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.Core.Interop/Windowing/AspectRatioLock.cs): Subclasses `GWLP_WNDPROC` to handle `WM_SIZING` and `WM_GETMINMAXINFO`.
- **Design Tokens & Strings:**
  - [`DesignTokens.xaml`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.App/Theme/DesignTokens.xaml#L228): Already reserves `AetherSizePipWidth` (400 DIPs).
  - [`Resources.resw`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.App/Strings/en-us/Resources.resw#L522): Strings `Pip_BackToFullWindow` and `Settings_Shortcuts_PictureInPicture` are already present.
- **Keyboard Shortcuts & Menus:**
  - [`ShortcutActions.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.Core.Playback/Input/ShortcutActions.cs): `ShortcutAction.TogglePictureInPicture` is already mapped to the `P` key by default.
  - Context menu in `MainPage.xaml.cs` has the "Picture in picture" entry placed, currently disabled.
- **Rendering & Surface Engine:**
  - [`MpvVideoSurface.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.App/Controls/MpvVideoSurface.cs): Contains `AttachPlayer` and `DetachPlayer`, along with `SetBackdropActive(false)` (ADR-0013 requires disabling backdrop blur in PiP).
  - [`MpvRenderLoop.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.Core.Interop/Rendering/MpvRenderLoop.cs): Owns device generation, DXGI presenter, and thread lifecycle.

### 2. Required Contracts & Stubs
- **`IShellWindow` Extension:**
  - Add `bool IsPictureInPicture { get; }`, `event EventHandler? PictureInPictureChanged`, and `void SetPictureInPicture(bool pip)`.
- **Architecture Choice (Single vs. Multi-Window):**
  - *Option A (Single Window Presenter):* Call `AppWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay)` on `MainWindow`. This avoids recreating the swap chain or managing a secondary window, but requires dynamically swapping shell chrome and overriding `AspectRatioLock`.
  - *Option B (Dedicated PipWindow with Hand-over):* A lightweight `PipWindow` hosting a second `MpvVideoSurface`. Requires `MpvVideoSurface.DetachPlayer()` on the main window followed by `pipSurface.AttachPlayer(session.Context)`.
- **Render Loop Hand-over Synchronization:**
  - `MpvVideoSurface.RestartLoop` currently calls `StopLoop` asynchronously (`loop.Dispose()` does not wait for thread termination). A synchronous or joined stop (`MpvRenderLoop.Stop(timeout)`) is required before creating a new `MpvRenderContext`, because libmpv enforces a single active render context per core.

### 3. Risks & Unknowns
- **CompactOverlay OS Clamping:** Windows App SDK `CompactOverlayPresenter` can enforce OS-level minimum size limits that may clamp dimensions larger than 400×225. A spike must verify whether 400×225 is accepted on Windows 11 23H2/24H2.
- **AspectRatioLock Min Size Collision:** `AspectRatioLock` currently hardcodes a static `(640, 400)` minimum client size and explicitly throws `InvalidOperationException` if initialized more than once per process. It must be refactored to allow dynamic adjustment of `MinimumClientSize` down to `(400, 225)` during PiP mode.
- **Multi-Window EGL/D3D11 Handles:** If a secondary window is used, ANGLE EGL display contexts bound to thread-local storage must not conflict across swap chain panel attachments.

### 4. File Impact & Parallelization
- **Touches Design Job UI Files:**
  - `MainWindow.xaml.cs` (presenter switching, minimum size tracking).
  - `MainPage.xaml` & `MainPage.xaml.cs` (PiP chrome hiding, hover bar, "Back to full window" button).
  - `DesignTokens.xaml` (any additional PiP control tokens).
- **Pure Core / Engine Tasks (Can Start Immediately):**
  - Refactoring [`AspectRatioLock.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.Core.Interop/Windowing/AspectRatioLock.cs) to support dynamic minimum sizing and safe multi-window transitions.
  - Adding thread-synchronized hand-over / detach-attach tests in [`MpvRenderLoopTests.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/tests/Aether.Core.Tests/MpvRenderLoopTests.cs).
  - Implementing PiP lifecycle state machine in `Aether.Core.Playback`.

### 5. Micro-Batch Build Split & Model Allocation
- **Batch 8.1 (Spike & Architecture - Opus):**
  - Spike `AppWindowPresenterKind.CompactOverlay` behavior at 400×225 on Windows 11.
  - Evaluate single-window vs. dual-window hand-over architecture. Document findings in `ADR-0020`.
- **Batch 8.2 (Core Interop & Synchronization - Sonnet):**
  - Refactor `AspectRatioLock` for dynamic minimum client size.
  - Add bounded thread join on render loop shutdown to guarantee race-free `Attach`/`Detach` hand-overs.
  - Add unit tests for hand-over lifecycle and state transitions.
- **Batch 8.3 (UI & Shell Wiring - Sonnet):**
  - Implement `IShellWindow.SetPictureInPicture`, wire the `P` key shortcut and context menu.
  - Add PiP hover chrome with "Back to full window" button and suppress backdrop blur in PiP.

---

## Pass 9: Link Playback (yt-dlp)

### 1. Existing Assets
- **Flow & Validation State Machine:**
  - [`OpenUrlFlow.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.Core.Playback/OpenUrlFlow.cs): Fully implemented state machine with stages (`Closed`, `Editing`, `GettingStream`, `StillWorking`), notes (`Hint`, `Invalid`, `Unavailable`), and 10-second `StillWorkingAfter` transition timer.
  - [`WebLink.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.Core.Playback/WebLink.cs): URI validation and host parsing (`WebLink.Host()`).
- **External Tooling & Settings:**
  - [`ExternalTools.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.App/Settings/ExternalTools.cs): `FindYtDlp()`, `YtDlpVersionAsync()`, and `UpdateYtDlpAsync()` with process tree timeout management.
  - [`StreamingPage.xaml.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.App/Settings/Pages/StreamingPage.xaml.cs): Streaming settings (`StreamingQuality`, `PreferGpuDecodableFormats`, `YtDlpAutoUpdate`, `NetworkCacheMegabytes`, `HttpProxy`, `RtspTransport`).
- **Resources & Recents:**
  - [`Resources.resw`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.App/Strings/en-us/Resources.resw): All 15 link-resolution and error strings already exist (`Stream_GettingStream`, `Stream_StillWorking`, `Stream_UnavailablePrivate`, `Stream_OfflineTitle`, `Stream_SignInRequiredTitle`, `Stream_RegionBlockedTitle`, `Stream_SiteNotSupportedTitle`, etc.).
  - [`RecentsStore.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.Core.Playback/Recents/RecentsStore.cs): Built to store web links (`IsLink`, case-sensitive matching, `RecentDisplay.Source`).

### 2. Required Contracts & Stubs
- **`IYtDlpStreamResolver` Service:**
  - `Task<ResolvedStreamResult> ResolveStreamAsync(string url, StreamResolutionOptions options, CancellationToken ct)`
  - Returns direct stream URL, extracted video title, duration, format, and optional HTTP headers.
- **Error Classifier:**
  - Statically maps yt-dlp standard error output to typed errors matching `Resources.resw` (Private video, Live stream not started, Geographic restriction, Sign-in required, Unsupported URL).
- **Clip/Frame Policy Contract:**
  - Frame capture (`Ctrl+C` / `Ctrl+Shift+S`) works directly via mpv's screenshot API.
  - Lossless FFmpeg clipping cannot copy remote streams; `ClipDrawer` must disable stream clipping and display an explanatory toast ("Save the file first to export clips").

### 3. Risks & Unknowns
- **Process Cancellation & Pipe Deadlock:** yt-dlp metadata extraction for complex playlists or high-bitrate video can generate large JSON payloads. Asynchronous draining of standard output and standard error is mandatory to avoid OS pipe deadlocks. Cancellation must terminate the entire process tree (`process.Kill(entireProcessTree: true)`).
- **Stream URL Expiration & Engine Restart:** Signed CDN URLs (e.g., YouTube `googlevideo.com`) expire after several hours. During engine restarts (`P7.1`), `PlaybackCarryOver` must retain the original web URL rather than a stale resolved stream link.
- **Quality Format Strings:** Translating user preferences (`StreamingQuality` and `PreferGpuDecodableFormats`) into valid yt-dlp `-f` selectors requires careful formatting to ensure audio and video streams merge properly without transcoding.

### 4. File Impact & Parallelization
- **Touches Design Job UI Files:**
  - `MainPage.xaml.cs` (activating `streamingAvailable: true`, wiring hero loading/failure views).
  - `MainPage.xaml` (hero state visual transitions).
- **Pure Core / Engine Tasks (Can Start Immediately):**
  - Building `YtDlpStreamResolver` and `YtDlpErrorClassifier` in `Aether.Core.Playback`.
  - Adding format string builder based on `AetherSettings`.
  - Unit tests for JSON extraction, error pattern matching, and cancellation in `tests/Aether.Core.Tests`.

### 5. Micro-Batch Build Split & Model Allocation
- **Batch 9.1 (Resolver Engine & Error Parsing - Sonnet):**
  - Implement `YtDlpStreamResolver` with asynchronous pipe streaming and process tree cancellation.
  - Build `YtDlpErrorClassifier` mapping CLI stderr to `Resources.resw` error categories.
  - Add comprehensive unit tests with fixture CLI outputs.
- **Batch 9.2 (Concurrency & Carry-Over Review - Opus):**
  - Review process cancellation, timeout handling, stream carryover across engine restarts, and format selection matrix.
- **Batch 9.3 (UI Wiring & Recents Integration - Sonnet):**
  - Switch `OpenUrlFlow` to `streamingAvailable: true` in `MainPage.xaml.cs`.
  - Wire hero states (Getting stream, Still working, error cards with retry/browser open buttons).
  - Update `RecentsStore` with resolved titles and guard `ClipDrawer` against remote stream clipping.

---

## Pass 10: Precise Re-Encode Clip Export

### 1. Existing Assets
- **Clipping Service & Request Model:**
  - [`ClipExportService.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.PvP.Review/Clipping/ClipExportService.cs): Pre-warms FFmpeg, manages temporary `.part` files, provides atomic renaming, duplicate naming prevention, and process tree cancellation.
  - [`ClipRequest.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.PvP.Review/Clipping/ClipRequest.cs) & [`FfmpegArguments.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.PvP.Review/Clipping/FfmpegArguments.cs): Stream copy argument generation (`-c copy`).
- **Drawer & Transport UI:**
  - [`ClipDrawer.xaml.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.App/Controls/ClipDrawer.xaml.cs): In/out range display, export triggering, row rendering, and drag-and-drop export.
  - [`PrecisionSeekBar.xaml.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.App/Controls/PrecisionSeekBar.xaml.cs): Visual In/Out marker bands.

### 2. Required Contracts & Stubs
- **`ClipRequest` Enhancements:**
  - Add `ExportMode` (`Fast` vs. `Precise`), optional `TargetSizeBytes` (e.g., Discord 25MB, 10MB), and `VideoEncoderPreference`.
- **Hardware Encoder Prober:**
  - Contract: `IHardwareEncoderDetector` to detect GPU hardware encoder support:
    - NVIDIA NVENC (`h264_nvenc`)
    - Intel QuickSync (`h264_qsv`)
    - AMD AMF (`h264_amf`)
    - CPU Fallback (`libx264`)
- **Bitrate Calculation & Re-encode Argument Builder:**
  - Calculate target video bitrate: `TargetBitrate = ((TargetSizeBytes * 8) / DurationSeconds) - AudioBitrate`.
  - Frame-accurate arguments: `-ss [start] -to [end] -i [src] -c:v [encoder] -b:v [bitrate] -maxrate [bitrate] -bufsize [2*bitrate] -c:a aac -b:a 128k`.
- **Progress Reporting:**
  - Parse FFmpeg `-progress pipe:1` to emit percentage, speed, and time remaining via `IProgress<ClipProgress>`.
- **Fast Mode Keyframe Markers (Task 10.5):**
  - Query nearest keyframes around In/Out timestamps via FFprobe or libmpv to visualize actual cut boundaries.

### 3. Risks & Unknowns
- **Hardware Encoder Driver Instability:** GPU drivers may report encoder support but fail during initialization due to session limits or headless mode. The service must implement an automatic fallback ladder: NVENC → QSV → AMF → libx264.
- **File Size Overshoot:** Single-pass encoding with high-motion content can overshoot target file size limits. A safety margin (e.g., 90–95% of target size) or constrained VBR parameters are required.
- **Keyframe Probe Latency:** Probing keyframes across large 4K files over network shares can introduce UI latency unless bounded by a narrow probe window around the cut points.

### 4. File Impact & Parallelization
- **Touches Design Job UI Files:**
  - `ClipDrawer.xaml` & `ClipDrawer.xaml.cs` (mode selector, size limit controls).
  - `ClipRow.xaml` & `ClipRow.xaml.cs` (progress bar for re-encoding).
  - `PrecisionSeekBar.xaml` & `PrecisionSeekBar.xaml.cs` (keyframe tick markers).
- **Pure Core / Engine Tasks (Can Start Immediately):**
  - All encoder detection, command generation, bitrate calculation, and progress parsing logic in `Aether.PvP.Review.Clipping`.
  - Unit and integration tests in `tests/Aether.PvP.Tests/`.

### 5. Micro-Batch Build Split & Model Allocation
- **Batch 10.1 (Encoder Detection & Bitrate Math - Sonnet):**
  - Implement `HardwareEncoderDetector` and fallback ladder.
  - Implement bitrate calculator and `ReEncodeArgumentsBuilder` supporting target size caps.
- **Batch 10.2 (Progress Streaming & Cancellation Tests - Sonnet):**
  - Add `-progress pipe:1` asynchronous stream parser.
  - Write unit and mock integration tests covering fallback behavior, cancellation, and `.part` file cleanup.
- **Batch 10.3 (Concurrency & Pipeline Review - Opus):**
  - Review encoder fallback state machine, pipe drainage, and keyframe probe performance.
- **Batch 10.4 (UI Integration - Sonnet):**
  - Add Fast/Precise mode toggle and target size options to `ClipDrawer.xaml`.
  - Bind progress reporting to `ClipRow.xaml` and add keyframe marker rendering to `PrecisionSeekBar.xaml`.

---

## Pass 11: Embedded Kestrel LAN Streamer & Release Verification

### 1. Existing Assets
- **Server & Pairing Implementation:**
  - [`KestrelStreamServer.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.Server.LanHost/KestrelStreamServer.cs): HTTP 206 partial content streaming, token authentication, `/watch` endpoint delivering HTML5 shell, and IP address detection.
  - [`QrPairingService.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/src/Aether.Server.LanHost/QrPairingService.cs): In-memory SVG and PNG QR generation using `QRCoder`.
  - [`ServerTests.cs`](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/tests/Aether.Server.Tests/ServerTests.cs): Passing unit tests for range requests, token authentication, and QR generation.
- **Shortcut & Settings Reservations:**
  - `ShortcutAction.ToggleLanStream` is defined and mapped to key `S` by default.
  - "Stream" entry is present in context menu and reserved on the hover rail.
- **ADR Directives:**
  - ADR-0001 (Native AOT), ADR-0003 (zero hot-path allocations), ADR-0017 (unpackaged deployment and PRI unification).

### 2. Required Contracts & Stubs
- **Stream Pairing Flyout:**
  - `LanStreamFlyout.xaml`: Displays the generated QR code, pairing URL, session token, active stream details, and start/stop toggle.
- **Mobile Client Detection in `/watch`:**
  - Client-side feature detection in `/watch` HTML to display an alert if iOS Safari cannot decode HEVC/MKV without transcoding.
- **Release Build & Trimming Configuration:**
  - Update `Aether.App.csproj` and `.github/workflows/build-aot.yml` to resolve Release trimming warnings and enable `-warnaserror`.
  - Suppress or annotate `IL2104` trim warnings from `WinRT.Runtime` / `Microsoft.Windows.SDK.NET`.
- **Third-Party Attribution:**
  - Compile `THIRD_PARTY.md` covering libmpv, ANGLE, FFmpeg, yt-dlp, and QRCoder.

### 3. Risks & Unknowns
- **WinRT.Runtime IL2104 Trim Warnings:**
  - In .NET 9/10, `WinRT.Runtime` emits `IL2104` ("Assembly 'WinRT.Runtime' produced trim warnings") because WinRT projection dispatch internally relies on reflection unless CsWinRT source generation is fully configured.
  - WinUI 3 XAML markup parsing (`XamlBindingHelper`) can fail under aggressive trimming.
  - Mitigation: Target `<NoWarn>$(NoWarn);IL2104</NoWarn>` or configure assembly-level trim root descriptors (`TrimmerRootAssembly`) in Release mode while keeping `PublishTrimmed=false` or `TrimMode=partial`.
- **Multi-Adapter LAN IP Resolution:**
  - Connecting a dummy UDP socket to `10.255.255.255` may bind to a virtual network adapter (Hyper-V, WSL, VPN) rather than the local Wi-Fi interface reachable by the phone. Adapter enumeration prioritizing active Wi-Fi/Ethernet subnets is needed.
- **Engine Resilience Under Device Loss & Overflow:**
  - Verify that `MpvEventLoop` recovers cleanly from `MPV_EVENT_QUEUE_OVERFLOW` and that attached `MpvContext` instances are destroyed in the background without UI hangs.

### 4. File Impact & Parallelization
- **Touches Design Job UI Files:**
  - `MainPage.xaml.cs` (handling `ToggleLanStream`, showing pairing flyout).
- **Pure Core / Engine Tasks (Can Start Immediately):**
  - Hardening `KestrelStreamServer` network adapter discovery and `/watch` client capabilities.
  - Release build engineering in `Aether.App.csproj` and `.github/workflows/build-aot.yml` to resolve trimming and IL2104 warnings.
  - Concurrency resilience audit on `MpvEventLoop` and `MpvRenderLoop`.
  - Writing `THIRD_PARTY.md`.

### 5. Micro-Batch Build Split & Model Allocation
- **Batch 11.1 (Release Configuration & Trimming - Sonnet):**
  - Configure Release build properties, resolve IL2104 trim warnings, and migrate CI workflow from Debug to Release with `-warnaserror`.
- **Batch 11.2 (Engine Resilience & Memory Audit - Opus):**
  - Audit event queue overflow handling, device loss destruction paths, and verify zero hot-path allocations during steady-state playback.
- **Batch 11.3 (LAN Server Polish & Third-Party Docs - Sonnet):**
  - Improve local IP adapter selection in `KestrelStreamServer`, add mobile browser codec check to `/watch`, and author `THIRD_PARTY.md`.
- **Batch 11.4 (UI Flyout Integration - Sonnet):**
  - Create `LanStreamFlyout.xaml`, wire the `S` shortcut, and enable the Stream menu item.

---

## Task Dependency & Sequencing Summary

| Pass | Pure Core / Engine Tasks (Can start now) | UI Dependent Tasks (Must wait for design job) | Recommended Starting Batch |
| :--- | :--- | :--- | :--- |
| **Pass 8** | `AspectRatioLock` dynamic sizing, render loop join on hand-over | PiP chrome overlay, "Back to full window", CompactOverlay switch | **8.1 (Spike) & 8.2 (Core)** |
| **Pass 9** | `YtDlpStreamResolver`, error classifier, format builder, unit tests | Hero loading/failure views, `OpenUrlFlow` UI wiring | **9.1 (Resolver Engine)** |
| **Pass 10** | Hardware encoder prober, bitrate math, progress pipe parser | Drawer mode toggle, progress bar on `ClipRow`, seek markers | **10.1 (Encoder Engine)** |
| **Pass 11** | Trim warning resolution, CI Release migration, adapter detection, `THIRD_PARTY.md` | Pairing flyout, Stream menu item | **11.1 (Release Hardening)** |
