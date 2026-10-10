# Aether Media Player — Roadmap

**Design tasks run on Opus.** They are 4.1–4.6, 4.8, 4.9, 5.1–5.4, 6.1–6.2, 7.1–7.3 (including 7.2), 8.1 and 9.2–9.3. Non-design wiring goes to Sonnet, and tests, CI and mechanical work go to Gemini. Before building any of them, Opus asks the user design questions and requests reference screenshots, then confirms the direction before writing UI code. Screens 1–10 are drawn: mockups, redlines and `aether.tokens.json` are in the design handoff folder. Where this roadmap and a redline disagree on look or layout, the redline wins.

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

### Pass 4: Foundations & Minimalist Windows 11 Shell [Next]

Mockups: screens 2 (shell states), 3 (playing) and 4 (spec sheet).

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 4.0 | `Foundations` (mechanical): generate the XAML `ResourceDictionary` from `aether.tokens.json`, set app-level accent overrides (no accent colour), create the strings file | S | Gemini |
| 4.0a | `SettingsStore`: persisted settings object (defaults, per-setting reset, applies-on-restart flags) | M | Sonnet |
| 4.1 | Custom window chrome: borderless window, `ExtendsContentIntoTitleBar`, blended title bar, top scrim, caption glyphs hidden with the controls (spike first: transparent glyphs keep their hit areas and hover) | S | Opus |
| 4.2 | `MicaTheme`: apply Windows 11 `MicaBackdrop` (Mica Alt, per the final handoff) behind letterboxing and controls | S | Opus |
| 4.3 | `DesignTokens`: consume the token dictionary for glass, borders, typography and surfaces | S | Opus |
| 4.4 | Auto-hiding OSD: Composition GPU opacity animation after 2s mouse idle + cursor hide | M | Opus |
| 4.5 | Drag-and-drop listener on main window for `.mp4`, `.mkv`, `.mov`, `.webm` | S | Opus |
| 4.6 | `StartupState`: off-UI-thread core initialization, loading transition, and missing binary/init failure presentation | M | Opus |
| 4.7 | `InputPolish`: fast-tap frame step queue/drop arbitration, window blur repeat release | S | Sonnet |
| 4.8 | Shell states: Open URL dialog and its error states, progress and recents (screen 2) | M | Opus |
| 4.9 | Spike: Acrylic over the swap chain, and confirm `MpvVideoSurface` is a `SwapChainPanel` (no child HWND). Result recorded in `docs/DECISIONS.md` | S | Opus |
| 4.10 | Tests for the settings store, token dictionary keys and strings coverage; CI Release build | S | Gemini |

**Pass 4 means:** Edge-to-edge video player with translucent dark Mica chrome, auto-hiding OSD on mouse idle, the shell states and dialogs from screens 2–3, graceful startup and error handling, and zero airspace clipping.

---

### Pass 5: Transport, HUD & Lossless Stream Clipper

Mockups: screen 5 (transport).

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 5.1 | `OverlayHud`: sub-millisecond timestamp, game tick (`floor(T*20)`), sub-tick %, frame count, source FPS | S | Opus |
| 5.2 | `PrecisionSeekBar` and transport pill: hover and drag tooltips, In (`I`) / Out (`O`) band, speed and quality menus, loop states, compact pill below 520 px | M | Opus |
| 5.3 | `ClipDrawer`: export drawer UI with drag-to-export into Discord / File Explorer (`DataPackage`) | M | Opus |
| 5.4 | Export chip and "Clip saved" result for the in/out range | S | Opus |
| 5.5 | Pre-warm FFmpeg on idle and execute sub-300ms `-c copy` export with atomic `.part` rename | S | Sonnet |
| 5.6 | Integration tests for FFmpeg argument building, GOP alignment, and export cancellation | S | Gemini |

**Pass 5 means:** Real-time game tick HUD, the full transport with visual in/out bounds, and sub-300ms lossless clip generation draggable directly into Discord.

---

### Pass 6: Settings

Mockups: screen 7 (settings, all pages).

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 6.1 | Settings page shell: custom section list plus `Frame` (not `NavigationView`), picker below 640 px, Back button in the title bar | M | Opus |
| 6.2 | Controls without accent colour: toggle, segmented, combo, slider, keycap, expander rows | M | Opus |
| 6.3 | Pages and bindings to the settings store: General, Playback, Video, Audio, Subtitles, Streaming, Shortcuts, Advanced, About | M | Sonnet |
| 6.4 | Hardware decoding combo: Auto (`auto-safe`), Software only (`no`), Direct3D 11 (`d3d11va`), Direct3D 11 copy (`d3d11va-copy`), applied on restart | S | Sonnet |
| 6.5 | Custom mpv options field (Cascadia Mono, per-line validation) and Streaming page state when yt-dlp is missing | S | Sonnet |
| 6.6 | Settings tests: defaults, reset, restart flags, invalid option lines | S | Gemini |

**Pass 6 means:** Every setting in the mockups is reachable, persisted and applied, and the page holds at 405 px.

---

### Pass 7: Menus, Toasts & Diagnostics

Mockups: screens 9 (menus), 10 (notifications) and 8.1–8.2 (diagnostics).

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 7.1 | Right-click `MenuFlyout` with icons, shortcuts, submenus (including Clips and Stream), max height with scroll; tracks and video flyouts | M | Opus |
| 7.2 | Toast control: look, placement, fade, reduced motion, live-region announcement | S | Opus |
| 7.2a | `ToastService`: one visible at a time, queued by priority, a toast with an action is never replaced, duplicates collapse, 4 s / 8 s with an action, pauses on hover and focus | M | Sonnet |
| 7.3 | Diagnostics panel (Ctrl+I) on a solid surface: decoder, video, frame rate, dropped, cache ("—" when unavailable). No GPU row | S | Opus |
| 7.4 | libmpv property wiring for diagnostics: `hwdec-current`, `video-format`, `video-params/w` and `h`, `container-fps`, `display-fps` (spike first: with `vo=libmpv` it is expected to stay empty, so read the monitor refresh rate via `MonitorFromWindow`/`EnumDisplaySettingsW` and set `display-fps-override`, updating on `WM_DISPLAYCHANGE`), `decoder-frame-drop-count`, `frame-drop-count`, `demuxer-cache-duration` | S | Sonnet |
| 7.5 | Wire engine failures, yt-dlp updates, frame copy and export results to the toast service | S | Sonnet |
| 7.6 | Tests for toast queue rules and diagnostics formatting | S | Gemini |

**Pass 7 means:** Menus, toasts and diagnostics behave as the redline describes, including narrow windows and missing data.

---

### Pass 8: Picture in Picture

Mockups: screens 8.3–8.4.

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 8.1 | Picture in picture: `AppWindow` CompactOverlay presenter at 400×225 (spike first: confirm the minimum size), hover controls, "Back to full window" beside the system close button, `P` shortcut (ignored when a text field has focus) | M | Opus |
| 8.2 | Engine hand-over between the main window and the overlay window without a decode restart (swap chain, device and lifetime safety) | M | Opus |
| 8.3 | Tests for the hand-over lifecycle and picture-in-picture state transitions | S | Gemini |

**Pass 8 means:** The video can float in a small always-on-top window and return to the full window without a stall.

---

### Pass 9: Embedded Kestrel LAN Streamer & Release Verification

| # | Task | Effort | Model |
|:---|:---|:---:|:---:|
| 9.1 | `KestrelStreamServer`: lightweight HTTP 206 partial content server with session token auth | M | Sonnet |
| 9.2 | Minimal HTML5 mobile player shell at `/watch?t={token}` with zero transcoding | S | Opus |
| 9.3 | `QrPairingService`: in-memory SVG/PNG QR code generator with local LAN IP flyout (`S` key) | S | Opus |
| 9.4 | Release hardening: Native AOT build (`dotnet publish -c Release -r win-x64`) | M | Gemini |
| 9.5 | End-to-end performance audit: cold start < 300ms, zero hot-path GC allocations | M | Opus |
| 9.6 | `EngineResilience`: attach/detach context cleanup, attached core disposal device-loss safety, queue overflow resync, background core destruction | M | Opus |
| 9.7 | `BinaryVerification`: libmpv and ANGLE binary integration, real media decode smoke test | S | Gemini |

**Pass 9 means:** Instant mobile pairing via QR code for LAN video streaming, fully verified Native AOT release build with 0 dropped frames.

---

### Parked

- **Hover rail (screen 6):** deferred. Clips and Stream are reached from the right-click menu and shortcuts. The spec stays in the design handoff.
- **Re-encode export (NVENC / QSV / AMF / x264):** not planned. Export is stream copy only. Revisit for non-keyframe cuts or size-limited exports.
- **GPU timing row in Diagnostics:** libmpv doesn't expose it. Would need our own D3D11 timestamp queries.
