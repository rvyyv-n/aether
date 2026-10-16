# Aether Product Spec Gap Audit

The v3 specification and project roadmap share strong alignment on core low-latency architecture, Direct3D 11/ANGLE swap chain presentation, lossless FFmpeg stream-copy trimming, and embedded LAN streaming.
Critical spec requirements were intentionally dropped or altered during implementation, notably the Minecraft/game-tick HUD overlay (dropped per user request in Pass 5.1), the `gpu-next` pipeline (replaced with ANGLE via ADR-0007), and opt-in tearing (rejected in ADR-0018).
Minor functional omissions from the spec persist in the current roadmap: keyframe indicators on In/Out markers, the `Ctrl+E` export shortcut, `Shift` (1 s) and `Ctrl` (30 s) seek modifiers, and the `THIRD_PARTY.md` licensing document.
The roadmap and changelog introduce major functional capabilities completely absent from the spec: link streaming via yt-dlp (Pass 9), precise re-encode exports (Pass 10), picture-in-picture (Pass 8), a 9-page settings suite, interactive flyout panels, and frame captures.
User decisions are needed to ratify the permanent deprecation of the HUD, approve the addition of missing shortcuts/modifiers, and confirm whether out-of-spec roadmap features should be backported into the formal specification.

---

## 1. Spec Gap Matrix (Partly Covered, Not Covered, Contradicted)

| Spec Section | Requirement | Status | Suggested Pass / Resolution |
|:---|:---|:---:|:---|
| **0 (#4), 4.2, 4.5, 9.4** | **Sub-millisecond & Game Tick HUD Overlay** (`GameTick = floor(T*20)`, ms, sub-tick %, frame count, source FPS, `OverlayHud.xaml`, `TickCalculator.cs`) | **Contradicted** | Dropped in Task 5.1 ("not needed, per the user"). Clean up the stale claim in the Pass 5 roadmap summary ("Real-time game tick HUD"). |
| **4.6** | **Toggle HUD Shortcut (`H`)** | **Contradicted** | Dropped along with the HUD in Task 5.1. Unbound in `ShortcutMap` and `AetherSettings`. |
| **0 (#9), 2.1, 4.1** | **`gpu-next` Rendering Pipeline** | **Contradicted** | Dropped in Task 2.1 & ADR-0007. libmpv C-ABI render context does not expose `gpu-next` on Windows; ANGLE EGL client buffer on Direct3D 11 is used instead. |
| **4.2** | **Keyframe Indicator on In/Out Trim Markers** (Visual marker indicating GOP keyframe boundary for lossless cuts) | **Not covered** | Pass 5.2 built the In/Out selection band on `PrecisionSeekBar`, but omitted keyframe alignment indicators. Address in Pass 5 backlog or Pass 10. |
| **4.3, 4.6** | **Export Clip Shortcut (`Ctrl+E`)** | **Not covered** | Clipping currently requires clicking the export chip in the UI. `Ctrl+E` is not registered in `ShortcutMap` or `AetherSettings`. Add in Pass 7 UI finish or Pass 10. |
| **4.6** | **Seek Modifier Accelerators** (`Shift` for 1 s fine seek, `Ctrl` for 30 s coarse seek on `←` / `→`) | **Partly covered** | Arrow keys only invoke fixed 5 s skip (`SkipSeconds = 5`). Modifier key checks are not handled in `MainPage.Transport.cs` or `ShortcutMap`. Add in Pass 7.15. |
| **4.5** | **Seek Bar Tooltip Format** (`HH:MM:SS.mmm` sub-second precision) | **Contradicted** | Changed to standard clock format (`mm:ss` or `h:mm:ss`, whole seconds) via `Timecode.FormatClock` per screen 4/5 design handoff and ADR-0012. |
| **2.2, 12** | **Opt-in Tearing Mode** (`DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING` / `Present(0, ALLOW_TEARING)`) | **Contradicted** | Rejected in ADR-0018 / Task 7.4a. Composition swap chains hosted in WinUI 3 `SwapChainPanel` cannot use independent-flip tearing or VRR; VSync is required. |
| **5** | **Third-Party Attribution Document (`THIRD_PARTY.md`)** | **Not covered** | File is missing from the repository root and is not scheduled in `ROADMAP.md` (licensing rules exist only in ADR-0016). Add to Pass 11.4/11.7. |
| **2.2, 7** | **Native 10-bit HDR Swap Chain Presentation** (`R10G10B10A2_UNORM`) | **Not covered** | Swap chain is hardcoded to 8-bit `B8G8R8A8_UNORM` in `DxgiPresenter`. Pass 6 added mpv tone-mapping settings, but 10-bit swap chain output is unscheduled. Target post-v1. |
| **9.6** | **LAN Streaming Mobile Compatibility Note** (Browser alert for HEVC/MKV containers) | **Partly covered** | Task 11.2 specifies the `/watch` HTML5 shell, but does not track client container support detection or the HEVC/MKV fallback notice. Add to Pass 11.2. |
| **5** | **Explicit GC Latency Tuning** (`ServerGarbageCollector=false`, `ConcurrentGarbageCollection=false`, `GCSettings.LatencyMode = SustainedLowLatency`) | **Partly covered** | Mentioned in ADR-0003, but not configured in `Directory.Build.props` or runtime code, and not tracked in `ROADMAP.md`. Verify in Pass 11.5. |
| **4.5** | **Mica Material Backdrop Type** (`MicaBackdrop (Base)`) | **Contradicted** | Roadmap 4.2, 4.D2, and ADR-0010 deliberately adopted `MicaBackdrop Kind="BaseAlt"` to match the darker wallpaper tint of design references. |
| **2.4** | **FlaUI UI Automation Testing Framework** | **Not covered** | Automated testing is confined to xUnit unit/integration suites; UI validation relies on manual on-screen checks. FlaUI is unscheduled. |

---

## 2. Roadmap & Changelog Additions Not Supported by the Spec

The following capabilities were implemented or scheduled in `ROADMAP.md`, `CHANGELOG.md`, and `DECISIONS.md`, but have no basis in the v3 specification:

1. **Pass 9: Link Playback via yt-dlp (Tasks 9.1–9.5)**
   - Resolving and streaming web links (YouTube, etc.) directly in mpv via yt-dlp.
   - Open URL dialog with clipboard prefill, hero loading states, and error handling.
   - Background yt-dlp auto-updates and update status toasts.
2. **Pass 10: Precise Re-encode Clip Export (Tasks 10.1–10.4)**
   - Re-encoding export engine using hardware encoders (NVENC, QSV, AMF) and x264 fallback.
   - Arbitrary frame-exact cut points and target file size limits (the spec only authorized `-c copy` stream copy).
3. **Pass 8: Picture in Picture Mode (Tasks 8.1–8.3)**
   - Windows App SDK `CompactOverlay` window (400×225) with seamless decode hand-over and `P` shortcut.
4. **Pass 6 & 7: Comprehensive 9-Page Settings Suite & VLC-Parity Controls (Tasks 6.1–6.7, 7.7–7.8)**
   - Dedicated settings shell across General, Playback, Video, Audio, Subtitles, Streaming, Shortcuts, Advanced, About.
   - Advanced playback controls: deinterlacing (`yadif`, `bwdif`), custom video scaling algorithms, debanding, HDR tone-mapping, audio pitch correction on speed changes, stereo downmixing, aspect ratio overrides, damaged AVI index rebuilding, and H.264 deblocking bypass.
   - Configurable keyboard shortcuts manager (`ShortcutMap`, `AetherSettings`).
5. **Pass 7: Interactive Context Menus & Sub-Panels (Tasks 7.1, 7.16, 7.18, 7.21)**
   - Right-click `MenuFlyout` with comprehensive player command routing.
   - Interactive Tracks panel (audio and subtitle streams, track selection, SDH/Forced badges, custom subtitle loading, ±0.1 s delay steppers).
   - Video adjustment panel (zoom, rotate, aspect ratio, brightness, contrast, gamma, saturation).
   - Left-edge auto-revealing hover rail (Task 7.21).
6. **Pass 7: Toast Notification System (Tasks 7.2, 7.2a, 7.5)**
   - Priority-queued toast notifications with interactive action buttons and dismiss timers.
7. **Pass 7: Instant Frame Capture (Task 7.19)**
   - Copy current frame to clipboard (`Ctrl+C`) and save frame to disk (`Ctrl+Shift+S`) without OSD.
8. **Pass 7: Dynamic Display Refresh Rate Tracking (Task 7.4, ADR-0018)**
   - Querying display refresh via `EnumDisplaySettingsW` to dynamically set mpv's `display-fps-override`.
9. **Pre-Pass 7: Custom ANGLE GL Backdrop Blur Pass (Task P7.6, ADR-0013)**
   - Custom dual-pass 9-tap Gaussian blur pass in ANGLE OpenGL to blur video directly behind the player card.
10. **Pre-Pass 7: In-Place Engine Restart (Task P7.1, ADR-0011)**
    - Recreating mpv context and render pipeline on the existing swap chain without app relaunch.
11. **Distribution & Packaging: Unpackaged Standalone Architecture (ADR-0017)**
    - Custom lightweight per-user installer (`AetherInstaller.exe`), portable zip distribution (`Aether-portable`), and context menu shell integration.
12. **Recent Files History (Task 4.D5)**
    - Persisted history of recently opened local media and streams (`recents.json`).

---

## 3. Fully Covered Spec Items

The following features, requirements, and constraints from the spec are fully implemented or accurately tracked:

- **DirectX 11 & SwapChain Interop:** `ID3D11Device5`/`ID3D11DeviceContext4` pipeline, composition swap chain (`FLIP_DISCARD`, 2 buffers, latency 1 waitable object, `SetMaximumFrameLatency(1)`), `ISwapChainPanelNative::SetSwapChain` interop with corrected GUID (Tasks 1.1–1.5, ADR-0002, ADR-0005, ADR-0006).
- **Core Playback Engine:** libmpv C-ABI integration using .NET 9 source-generated `[LibraryImport]` P/Invoke, `vo=libmpv`, `hwdec=d3d11va` zero-copy hardware decoding, `video-sync=display-resample` (Tasks 2.1, 2.3, ADR-0001, ADR-0007).
- **Threading & Event Pump:** Dedicated long-running thread for `mpv_wait_event`, non-blocking event dispatch coalesced to display refresh, clean shutdown via `mpv_wakeup` (Tasks 3.1, 3.2, ADR-0009).
- **Zero Allocations Hot Path:** Allocation-free render loop (`Render` → `Present` → `ReportSwap`), struct-backed view models, and unmanaged parameter blocks (Tasks 2.4, ADR-0003, ADR-0008).
- **Frame Navigation:** Bidirectional frame stepping (`,` backward, `.` forward via `frame-back-step`/`frame-step`), repeat key acceleration, drop coalescing, and 512 MiB demuxer back-bytes cache (Tasks 2.1, 3.3, 4.7).
- **Lossless Stream Clipping:** Sub-300 ms FFmpeg `-c copy` export, idle process pre-warming, `.part` atomic rename, process cancellation, and drag-to-export clip drawer into Discord/Explorer via `DataPackage` (Tasks 5.3–5.6).
- **Shell Design & Theming:** Custom window chrome, borderless edge-to-edge video, auto-hiding OSD with 2 s idle timer, cursor hiding, and reduced-motion compliance (Tasks 4.1, 4.4, ADR-0010).
- **Playback Transport Controls:** Play/pause, seek coalescing, speed bounds (0.1×–4.0×), volume slider with mute toggle, loop playback (`L`), always-on-top (`T`), per-file resume position (`resume.json`), and drag-and-drop file opening (Tasks 4.5, 4.D4, 5.2, 5.8–5.11, ADR-0014).
- **LAN Streaming Architecture:** Embedded Kestrel HTTP 206 byte-range streaming server (`/stream/video`), session token security, in-memory QR code generator (`S` key flyout), and HTML5 mobile viewer (Pass 11.1–11.3).
- **Release & Target Platform:** .NET 9, C# 13, Windows App SDK 1.6+, Native AOT publishing target, and multi-agent governance rules (Pass 11.4, ADR-0001, docs/AGENTS.md).
