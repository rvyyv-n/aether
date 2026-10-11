# Aether Media Player — Roadmap

## Model routing

Every task row has a **Model** column. Autonomous runs should follow it without asking:

| Model | Use for | Rows |
|:---|:---|:---|
| **Opus** | Design work (UI, chrome, Mica, OSD, HUD, seek bar, drawers, QR flyout), `Aether.Core.Interop` (P/Invoke, COM, memory, event-loop safety), architecture decisions, on-screen checks | Every row marked Opus |
| **Sonnet** | Non-design wiring in `Aether.App`, `Aether.Core.Playback`, `Aether.PvP.Review`, `Aether.Server.LanHost` | Every row marked Sonnet |
| **Gemini** | Test suites, benchmarks, CI workflows, scouting, mechanical refactors | Every row marked Gemini |

**Rules for every task:**
- **Design tasks** (4.1–4.6, 4.C3–4.C5, 4.D2–4.D6, 5.1–5.4, 6.1–6.2, 7.1–7.3, 8.1–8.2, 9.2–9.3) run on Opus. Before writing UI code, Opus asks the user design questions and requests reference screenshots, then confirms the direction.
- **Helpers** get a self-contained brief (goal, files, project rules, what to report). Floor: Sonnet at medium effort. At most three helpers per task.
- **Threads** retire at 8 turns or 150k tokens: commit, then fork (`t3_thread_fork`) or start fresh.
- **Build passes** run in a worktree, not the main folder.
- **Gates:** `dotnet build AetherPlayer.sln --no-incremental -warnaserror --no-restore -p:NuGetAudit=false` and `dotnet test AetherPlayer.sln --no-restore -p:NuGetAudit=false` must pass before a task is marked done.
- **Not autonomous:** merges, pushes, version numbers and tags stay with the user.

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
| 4.C1 | Carry-over: on-screen check of every shell state (empty, starting, failure, drag-over, playing, full screen) against the design; needs the packaged app (Developer Mode or Visual Studio) plus libmpv and a sample clip. User check: shell states and window behaviour pass; playback findings moved to 4.C2–4.C5 | S | Opus |
| 4.C2 | Bug: playback does not replay. At end of file (`eof-reached`, `keep-open`) the play button and Space do nothing; fix so Play on an ended file seeks to 0 and unpauses (`PlayerSession.TogglePause` / `MainPage.TogglePause`, `HasVideo` gate), and check the restart path with a test | S | Sonnet |
| 4.C3 | Bug: no clickable open control. Files open only via `Ctrl+O` and drag-and-drop. Add a visible Open button (controls bar and empty-state panel) that calls the same picker as the accelerator; placement and look need design approval first — **Done** (per handoff 2A and screen 3: "Open file" emphasis pill on the empty state, disabled while the engine starts and hidden on failure or drag-over; Open icon button first in the controls pill with the "Open file (Ctrl+O)" tooltip; both share the Ctrl+O picker. Open URL waits for streaming. Not yet checked on screen) | S | Opus |
| 4.C4 | Bug: label alignment and capitalisation. Known items: hint text reads "press Ctrl O" (should be `Ctrl+O`); panel captions mix `SUPPORTED` / `DETAILS` (all caps) with sentence-case labels (`Formats`, `Renderer`, `Searched`); status text mixes cases (`Engine ready`, `Starting`). Full audit of every label with the user's list before fixing; Opus confirms the casing rule first — **Done** (spec sheet wording through `Resources.resw`; failure details tile on a 72/* grid; `mm:ss` clock; ADR-0012. Not yet checked on screen) | S | Opus |
| 4.C5 | Design review: compare the current shell against the user's reference screenshots (`C:\Users\rayyan\Pictures\Screenshots`, Wintoys and OpenMouse references) and list gaps. Opus asks the user which references apply and gets approval on the list before any UI code | M | Opus |
| 4.D0 | Foundations (mechanical): generate the XAML `ResourceDictionary` from `aether.tokens.json` (the handoff `DesignTokens.xaml` is the starting point), no-accent overrides so standard controls stay white, strings file — **Done** (`d00291d`: handoff dictionary with legacy-key aliases, `Resources.resw`; gate green, 283 core tests) | S | Gemini |
| 4.D1 | `SettingsStore`: persisted settings object (defaults, per-setting reset, applies-on-restart flags) — **Done** (gate green, 299 core tests) | M | Sonnet |
| 4.D2 | Rebase the built shell (4.1–4.6) onto the handoff tokens: Mica Alt, glass surfaces, Segoe UI Variable, 32 px title bar with 138 px caption inset, caption glyphs fading with the controls (screens 1, 3, 4) — **Done** (title bar on the 16 px inset with a shared baseline, 46×32 gear (disabled until Settings), 72 px top scrim, caption glyphs eased with the chrome; shell on the new token names, badge padding 6,2; gate green, 331 core tests) | M | Opus |
| 4.D3 | Empty state (2A), status strip and startup/failure states (screens 1–2) — **Done** (96 px glass hero tile with the film glyph, title/subtitle/details/actions on the redline spacing, token colours for glyph and drop target; status strip with the engine dot on the left and ANGLE · D3D11 on the right, 2 px startup bar on its bottom edge; failure tile with Retry and Copy details (Open folder dropped per 2.2). No recents store yet, so recents are not built. Not yet checked on screen) | M | Opus |
| 4.D4 | Playing view: one-row pill (5.8), tooltips, paused, ended, controls hidden, portrait 405 (screen 3) — **Done** (48 px glass pill, min(520, window − 32), 24 above the bottom: Open, Play/Pause/Replay, mm:ss times flanking an inline seek bar (keyframe seeks while dragging, exact on release), volume button mutes on click with its slider in a hover flyout (pinned from right-click or Shift+F10) that holds the chrome, Full screen; shortcut tooltips, M mutes, ended reads the full duration; fades instant with Windows animations off; YouTube quality pill skipped until streaming. Gate green, 350 core tests. Not yet checked on screen) | M | Opus |
| 4.D5 | Open URL dialog with invalid and 405 states, progress, returning-user and missing-recent states (screen 2) — **Done** (dialog on the solid dialog fallback over a scrim, min(440, window − 32), prefilled from the clipboard only when it holds a link; Play enabled from http:// or https://, amber "That isn’t a web link" otherwise. Links can't play yet, so the hint reads "Link playback isn’t available yet" and Play keeps the dialog open with that line; Open URL pill on the empty state and Ctrl+Shift+O (the handoff's shortcut). Getting stream / still working (10 s) are built in the hero but unreachable until streaming. Recents store (`recents.json`, newest 3) fed by every file open, with the length once loaded; rows hidden until there is history, Clear list, and a missing file keeps its row with "File not found" and Remove. 7 link-failure and yt-dlp frames wait for streaming. Gate green, 397 core tests. Not yet checked on screen) | M | Opus |
| 4.D6 | Spike: Acrylic over the swap chain; confirm `MpvVideoSurface` is a `SwapChainPanel`. Result in `docs/DECISIONS.md` — **Done** (it is a `SwapChainPanel`; in-app Acrylic samples only the XAML tree behind it, so surfaces over video use the solid `AetherSurfaceDialogFallbackBrush`; OSD shadow deferred. ADR-0013. Not yet checked on screen) | S | Opus |
| 4.D7 | Tests for the settings store, token dictionary keys and strings coverage; CI build — **Done** (tokens, strings and settings suites added; gate green, 313 core tests) | S | Gemini |

**Pass 4 means:** Edge-to-edge video player with translucent dark Mica chrome, auto-hiding OSD on mouse idle, graceful startup and error handling, and zero airspace clipping. *(4.1–4.6 built on `claude/pass-4`: 270 tests passing, 0 warnings. 4.7 built: fast taps queue in one slot. 4.8 built: window fits the video and keeps its aspect ratio. Shell states and window behaviour checked on screen by the user. Open: 4.C2 (replay), 4.C5 (reference screenshot review).)*

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
| 5.7 | Open paths: command-line argument and file association registration for the four containers | S | Sonnet |
| 5.8 | Loop playback: toggle (`L`), restart at end of file, state held in `PlayerViewModel` | S | Sonnet |
| 5.9 | Always-on-top toggle (`T`) via `AppWindow.Presenter`, persisted with window state | S | Sonnet |
| 5.10 | Resume position per file: save on close or switch, restore on open, skip near start/end, cap stored entries | S | Sonnet |
| 5.11 | `PerformanceLoop`: async seek coalescing, zero-drop GPU decode verification, smooth 60fps+ UI under high-bitrate 1440p/4K load | M | Gemini |
| 5.12 | Tests for 5.7–5.10: argument and extension parsing, loop wrap, resume store round-trip and pruning, speed bounds 0.1–4.0 | S | Gemini |

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
| 6.7 | Configurable keyboard shortcuts on the Shortcuts page: defaults as today, Open URL = `Ctrl+Shift+O` with `Ctrl+L` offered as an alternative; conflicts flagged, per-shortcut reset | S | Sonnet |

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
| 9.4 | Release hardening: Native AOT build (`dotnet publish -c Release -r win-x64`); fix the Release build's IL2104 trim warnings from `WinRT.Runtime` / `Microsoft.Windows.SDK.NET`, then move CI from Debug to Release | M | Gemini |
| 9.5 | End-to-end performance audit: cold start < 300ms, zero hot-path GC allocations | M | Opus |
| 9.6 | `EngineResilience`: attach/detach context cleanup, attached core disposal device-loss safety, queue overflow resync, background core destruction | M | Opus |
| 9.7 | `BinaryVerification`: libmpv and ANGLE binary integration, real media decode smoke test | S | Gemini |

**Pass 9 means:** Instant mobile pairing via QR code for LAN video streaming, fully verified Native AOT release build with 0 dropped frames.

---

### Parked

- **Hover rail (screen 6):** deferred. Clips and Stream are reached from the right-click menu and shortcuts. The spec stays in the design handoff.
- **Re-encode export (NVENC / QSV / AMF / x264):** not planned. Export is stream copy only. Revisit for non-keyframe cuts or size-limited exports.
- **GPU timing row in Diagnostics:** libmpv doesn't expose it. Would need our own D3D11 timestamp queries.
- **Link playback (yt-dlp streaming):** not scheduled yet. When it lands, the empty-state hint must switch to "Drop a file, paste a link or press Ctrl+O", and Play in the Open URL dialog must start the stream instead of showing "Links can’t be played yet." (`OpenUrlFlow` takes `streamingAvailable`).
