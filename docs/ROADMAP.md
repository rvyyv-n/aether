# Aether Media Player — Roadmap

## Gates

`dotnet build AetherPlayer.sln --no-incremental -warnaserror --no-restore -p:NuGetAudit=false` and `dotnet test AetherPlayer.sln --no-restore -p:NuGetAudit=false` must pass before a task is marked done.

### Pass 1: DirectX 11 Pipeline & SwapChain Interop [Completed]

| # | Task | Effort |
|:---|:---|:---:|
| 1.1 | `D3D11DeviceManager`: create D3D11 device (BGRA/video support, level 11.1, multithread protection) | M |
| 1.2 | `DxgiPresenter`: composition swap chain (`FLIP_DISCARD`, latency 1 waitable object, tearing flag) | M |
| 1.3 | Handle DPI resize (`ResizeBuffers`) and composition inverse matrix transform (`SetMatrixTransform`) | S |
| 1.4 | `MpvVideoSurface`: custom `SwapChainPanel` control wired to `ISwapChainPanelNative::SetSwapChain` | S |
| 1.5 | Unit test device creation, swap chain flags, and waitable latency handle | S |

**Pass 1 means:** WinUI 3 hosts an active Direct3D 11 flip-model swap chain inside a XAML `SwapChainPanel` with 1-frame latency sync and zero composition errors. *(Completed: 42 tests passing)*

---

### Pass 2: libmpv Engine Initialization & Dual-Path Render Context [Completed]

| # | Task | Effort |
|:---|:---|:---:|
| 2.1 | `MpvContext`: SafeHandle lifecycle wrapper with low-latency flags (`d3d11va`, 512MiB back-buffer; `gpu-next` dropped, see ADR-0007) | M |
| 2.2 | `MpvRenderContext`: API-agnostic render context wrapper (libmpv has no D3D11 render API, see ADR-0007) | M |
| 2.3 | `AngleBridge`: primary render path, ANGLE EGL client buffer mapping swap chain texture to FBO | M |
| 2.4 | `MpvRenderLoop`: dedicated render thread loop (`Render` → `Present` → `ReportSwap`, zero GC allocations) | M |
| 2.5 | Context lifecycle and unmanaged parameter block disposal tests | S |

**Pass 2 means:** libmpv decodes video via `d3d11va` directly into the swap chain back buffer with zero allocations on the render hot path. *(Completed: 125 tests passing)*

---

### Pass 3: Dedicated MPV Event Loop & Navigation Engine [Completed]

| # | Task | Effort |
|:---|:---|:---:|
| 3.1 | `MpvEventLoop`: dedicated background thread pumping `mpv_wait_event` with clean `mpv_wakeup` shutdown | M |
| 3.2 | Throttled UI dispatch: batch property events to `DispatcherQueue` at most once per display refresh | S |
| 3.3 | `FrameStepper`: bidirectional frame stepping (`,` and `.`) with hold-to-repeat acceleration | S |
| 3.4 | `PlayerViewModel`: struct-backed observable state for time, duration, speed, volume, and playback status | S |
| 3.5 | Unit tests for frame step commands, boundaries, repeat curves, and event dispatch | S |
| 3.C1 | Carry-over: `MpvVideoSurface.AttachPlayer`/`DetachPlayer` core lifecycle management | S |
| 3.C2 | Carry-over: CI workflow Release build validation for `Aether.Core.Playback` | S |

**Pass 3 means:** Frame-accurate stepping forward/backward with instant visual feedback and non-blocking event dispatch to the UI. *(231 tests passing, 0 warnings)*

---

### Pass 4: Minimalist Windows 11 Shell & Mica Theming [In Progress]

Mockups: screens 2 (shell states), 3 (playing) and 4 (spec sheet). Screens 1–10 are drawn; mockups, redlines and `aether.tokens.json` are in the design handoff folder. Where this roadmap and a redline disagree on look or layout, the redline wins.

| # | Task | Effort |
|:---|:---|:---:|
| 4.1 | Custom window chrome: borderless window, `ExtendsContentIntoTitleBar`, blended title bar | S |
| 4.2 | `MicaTheme`: apply Windows 11 `MicaBackdrop` (Base Alt) behind letterboxing and controls; black letterbox in full screen | S |
| 4.3 | `DesignTokens`: neutral glass panels (`#730E1014`, controls `#9E0E1014`), faint 1px borders (`#FFFFFF0F`), Segoe UI Variable with Cascadia Mono for numbers | S |
| 4.4 | Auto-hiding OSD: Composition GPU opacity animation after 2s mouse idle + cursor hide | M |
| 4.5 | Drag-and-drop listener on main window for `.mp4`, `.mkv`, `.mov`, `.webm` | S |
| 4.6 | `StartupState`: off-UI-thread core initialization, loading transition, and missing binary/init failure presentation | M |
| 4.7 | `InputPolish`: fast-tap frame step queue/drop arbitration, window blur repeat release | S |
| 4.C1 | Carry-over: on-screen check of every shell state (empty, starting, failure, drag-over, playing, full screen) against the design; needs the packaged app (run `.\run.ps1` in Developer Mode or launch from Visual Studio) plus libmpv and a sample clip. User check: shell states and window behaviour pass; playback findings moved to 4.C2–4.C5 | S |
| 4.C2 | Bug: playback does not replay. At end of file (`eof-reached`, `keep-open`) the play button and Space do nothing; fix so Play on an ended file seeks to 0 and unpauses (`PlayerSession.TogglePause` / `MainPage.TogglePause`, `HasVideo` gate), and check the restart path with a test | S |
| 4.C3 | Bug: no clickable open control. Files open only via `Ctrl+O` and drag-and-drop. Add a visible Open button (controls bar and empty-state panel) that calls the same picker as the accelerator; placement and look need design approval first — **Done** (per handoff 2A and screen 3: "Open file" emphasis pill on the empty state, disabled while the engine starts and hidden on failure or drag-over; Open icon button first in the controls pill with the "Open file (Ctrl+O)" tooltip; both share the Ctrl+O picker. Open URL waits for streaming. Not yet checked on screen) | S |
| 4.C4 | Bug: label alignment and capitalisation. Known items: hint text reads "press Ctrl O" (should be `Ctrl+O`); panel captions mix `SUPPORTED` / `DETAILS` (all caps) with sentence-case labels (`Formats`, `Renderer`, `Searched`); status text mixes cases (`Engine ready`, `Starting`). Full audit of every label with the user's list before fixing; The casing rule is confirmed first — **Done** (spec sheet wording through `Resources.resw`; failure details tile on a 72/* grid; `mm:ss` clock; ADR-0012. Not yet checked on screen) | S |
| 4.C5 | Design review: compare the current shell against the reference screenshots (Wintoys and OpenMouse) and list gaps. The list is approved before any UI code | M |
| 4.D0 | Foundations (mechanical): generate the XAML `ResourceDictionary` from `aether.tokens.json` (the handoff `DesignTokens.xaml` is the starting point), no-accent overrides so standard controls stay white, strings file — **Done** (`d00291d`: handoff dictionary with legacy-key aliases, `Resources.resw`; gate green, 283 core tests) | S |
| 4.D1 | `SettingsStore`: persisted settings object (defaults, per-setting reset, applies-on-restart flags) — **Done** (gate green, 299 core tests) | M |
| 4.D2 | Rebase the built shell (4.1–4.6) onto the handoff tokens: Mica Alt, glass surfaces, Segoe UI Variable, 32 px title bar with 138 px caption inset, caption glyphs fading with the controls (screens 1, 3, 4) — **Done** (title bar on the 16 px inset with a shared baseline, 46×32 gear (disabled until Settings), 72 px top scrim, caption glyphs eased with the chrome; shell on the new token names, badge padding 6,2; gate green, 331 core tests) | M |
| 4.D3 | Empty state (2A), status strip and startup/failure states (screens 1–2) — **Done** (96 px glass hero tile with the film glyph, title/subtitle/details/actions on the redline spacing, token colours for glyph and drop target; status strip with the engine dot on the left and ANGLE · D3D11 on the right, 2 px startup bar on its bottom edge; failure tile with Retry and Copy details (Open folder dropped per 2.2). No recents store yet, so recents are not built. Not yet checked on screen) | M |
| 4.D4 | Playing view: one-row pill (5.8), tooltips, paused, ended, controls hidden, portrait 405 (screen 3) — **Done** (48 px glass pill, min(520, window − 32), 24 above the bottom: Open, Play/Pause/Replay, mm:ss times flanking an inline seek bar (keyframe seeks while dragging, exact on release), volume button mutes on click with its slider in a hover flyout (pinned from right-click or Shift+F10) that holds the chrome, Full screen; shortcut tooltips, M mutes, ended reads the full duration; fades instant with Windows animations off; YouTube quality pill skipped until streaming. Gate green, 350 core tests. Not yet checked on screen) | M |
| 4.D5 | Open URL dialog with invalid and 405 states, progress, returning-user and missing-recent states (screen 2) — **Done** (dialog on the solid dialog fallback over a scrim, min(440, window − 32), prefilled from the clipboard only when it holds a link; Play enabled from http:// or https://, amber "That isn’t a web link" otherwise. Links can't play yet, so the hint reads "Link playback isn’t available yet" and Play keeps the dialog open with that line; Open URL pill on the empty state and Ctrl+Shift+O (the handoff's shortcut). Getting stream / still working (10 s) are built in the hero but unreachable until streaming. Recents store (`recents.json`, newest 3) fed by every file open, with the length once loaded; rows hidden until there is history, Clear list, and a missing file keeps its row with "File not found" and Remove. 7 link-failure and yt-dlp frames wait for streaming. Gate green, 397 core tests. Not yet checked on screen) | M |
| 4.D6 | Spike: Acrylic over the swap chain; confirm `MpvVideoSurface` is a `SwapChainPanel`. Result in `docs/DECISIONS.md` — **Done** (it is a `SwapChainPanel`; in-app Acrylic samples only the XAML tree behind it, so surfaces over video use the solid `AetherSurfaceDialogFallbackBrush`; OSD shadow deferred. ADR-0013. Not yet checked on screen) | S |
| 4.D7 | Tests for the settings store, token dictionary keys and strings coverage; CI build — **Done** (tokens, strings and settings suites added; gate green, 313 core tests) | S |

**Pass 4 means:** Edge-to-edge video player with translucent dark Mica chrome, auto-hiding OSD on mouse idle, graceful startup and error handling, and zero airspace clipping. *(270 tests passing, 0 warnings. 4.7 built: fast taps queue in one slot. 4.8 built: window fits the video and keeps its aspect ratio. Shell states and window behaviour checked on screen by the user. Open: 4.C2 (replay), 4.C5 (reference screenshot review).)*

---

### Pass 5: Transport, HUD & Lossless Stream Clipper

Mockups: screen 5 (transport).

| # | Task | Effort |
|:---|:---|:---:|
| 5.1 | `OverlayHud`: sub-millisecond timestamp, game tick (`floor(T*20)`), sub-tick %, frame count, source FPS — **Dropped** (not needed, per the user) | S |
| 5.2 | `PrecisionSeekBar` and transport pill: hover and drag tooltips, In (`I`) / Out (`O`) band, speed and quality menus, loop states, compact pill below 520 px — **Done** except the quality menu, which waits for streaming (two-row pill, `PrecisionSeekBar`, In/Out points, speed menu, loop and always-on-top buttons, compact More menu. Not yet checked on screen) | M |
| 5.3 | `ClipDrawer`: export drawer UI with drag-to-export into Discord / File Explorer (`DataPackage`). Merged with 5.4: the export chip above the pill grows upward into a drawer of download-style rows (thumbnail, name, grey size · length · time line, draggable out, Show in folder and Dismiss, up to about 3 rows) — **Done** (clip drawer on the export chip; not yet checked on screen) | M |
| 5.4 | Export chip and "Clip saved" result for the in/out range (built together with 5.3) — **Done** (built with 5.3) | S |
| 5.5 | Pre-warm FFmpeg on idle and execute sub-300ms `-c copy` export with atomic `.part` rename — **Done** (`ClipExportService`, `FfmpegArguments`; gate green) | S |
| 5.6 | Integration tests for FFmpeg argument building, GOP alignment, and export cancellation — **Done** | S |
| 5.7 | Open paths: command-line argument and file association registration for the four containers — **Done** | S |
| 5.8 | Loop playback: toggle (`L`), restart at end of file, state held in `PlayerViewModel` — **Done** | S |
| 5.9 | Always-on-top toggle (`T`) via `AppWindow.Presenter`, persisted with window state — **Done** | S |
| 5.10 | Resume position per file: save on close or switch, restore on open, skip near start/end, cap stored entries — **Done** | S |
| 5.11 | `PerformanceLoop`: async seek coalescing, zero-drop GPU decode verification, smooth 60fps+ UI under high-bitrate 1440p/4K load — **Done** (`SeekCoalescer`, `CompositeCommandReplyHandler`, exact landing preservation; gate green, 627 core tests) | M |
| 5.12 | Tests for 5.7–5.10: argument and extension parsing, loop wrap, resume store round-trip and pruning, speed bounds 0.1–4.0 — **Done** | S |

**Pass 5 means:** Real-time game tick HUD, the full transport with visual in/out bounds, and sub-300ms lossless clip generation draggable directly into Discord.

---

### Pass 6: Settings

Mockups: screen 7 (settings, all pages).

| # | Task | Effort |
|:---|:---|:---:|
| 6.1 | Settings page shell: custom section list plus `Frame` (not `NavigationView`), picker below 640 px, Back button in the title bar — **Done** | M |
| 6.2 | Controls without accent colour: toggle, segmented, combo, slider, keycap, expander rows — **Done** | M |
| 6.3 | Pages and bindings to the settings store: General, Playback, Video, Audio, Subtitles, Streaming, Shortcuts, Advanced, About — **Done** | M |
| 6.4 | Hardware decoding combo: Auto (`auto-safe`), Software only (`no`), Direct3D 11 (`d3d11va`), Direct3D 11 copy (`d3d11va-copy`), applied on restart — **Done** | S |
| 6.5 | Custom mpv options field (Cascadia Mono, per-line validation) and Streaming page state when yt-dlp is missing — **Done** | S |
| 6.6 | Settings tests: defaults, reset, restart flags, invalid option lines — **Done** | S |
| 6.7 | Configurable keyboard shortcuts on the Shortcuts page: defaults as today, Open URL = `Ctrl+Shift+O` with `Ctrl+L` offered as an alternative; conflicts flagged, per-shortcut reset — **Done** | S |

**Pass 6 means:** Every setting in the mockups is reachable, persisted and applied, and the page holds at 405 px.

---

### Pre-Pass 7: Engine Restart Without Relaunch

| # | Task | Effort |
|:---|:---|:---:|
| P7.1 | Restart the playback engine in place when a restart-only setting changes (hardware decoding, audio passthrough and the like) instead of asking for an app relaunch: tear down and recreate the mpv context and render loop on the same swap chain, then reopen the current file at the same position with the same pause, volume, speed and track choices | M |

**Pre-Pass 7 means:** Changing a restart-only setting applies straight away and playback resumes where it was, with the window still open.

---

### Pre-Pass 7: Player UI polish (match the design)

Numbered from P7.2 because P7.1 is the engine restart above. The design handoff (screens 3, 5 and 9, `aether.tokens.json`) is the target; values below are from the screen 5 redline. The recent restyle (radius 8, solid white 36 px play, plain-text speed, no outline on toggles) is reverted where it departs from it; the 640×400 minimum window stays. The bar looks like a solid slab today because the in-app Acrylic brush cannot see the swap chain (ADR-0013) and paints its own opaque luminosity layer; the handoff draws the pill as a plain 62% tint, which already lets the video through.

| # | Task | Effort |
|:---|:---|:---:|
| P7.2 | Glass that shows the video: the card's fill goes from `AetherSurfaceOsdAcrylicBrush` to the plain `AetherSurfaceOsdBrush` (#9E0E1014, 62% ink), which is what every handoff frame draws and what keeps TextPrimary at 5.35:1 over pure white. Retire the Acrylic brush. No bottom scrim: the handoff has none and 62% does not need one. This is also the fallback if P7.6 is dropped | S |
| P7.3 | Card shape back to the redline: `AetherTransportCornerRadius` 8 → 24 (16 button radius + 8 padding, concentric); button row 36 → 32, so the card is 84 tall (8 + 32 + 4 + 32 + 8); padding 8 (7 plus the stroke); centre group spacing 8 → 4, like the side groups; drop `AetherSizeTransportPlay` and size the row by `AetherSizeControl`. The chip, drawer and toast offset 120 → 116 (24 + 84 + 8). Width stays min(520, window − 32), so at the 640 px minimum the card is always the full 520 and More controls only appears in picture in picture (Pass 8) | S |
| P7.4 | Controls as drawn in 5.1 and 5.6. Play/Pause: 32 round, FillEmphasisRest (#24FFFFFF) with a 1 px StrokeControl (#1FFFFFFF), hover FillEmphasisHover, pressed FillEmphasisPressed, white 16 px glyph; remove `AetherFillPlay*` and `AetherTextOnPlay`. Speed: outline pill again, at least 52 wide (`AetherSizeSpeedPill` 40 → 52), padding 0,10, StrokeControl, Caption 12 tabular, FillEmphasisRest while its menu is open. Loop and Always on top when on: FillEmphasisRest plus StrokeControl, FillEmphasisHover on hover. Glyphs: back and forward 5 s use a circular-arrow glyph at 18 px (the current rewind and fast-forward glyphs read as track skip; the numbered Fluent skip glyphs say 10 and 30, so use one without a number); full screen uses the four-corner glyph, not the diagonal arrows. Seek hover line 12 → 16 tall | S |
| P7.5 | Title bar as in screen 3: the badge gets its SurfaceOsd fill (radius 4, padding 2,6, max 320 for files and 240 for streams); "Alpha" on the empty state stays plain. The gear and Back stay 46×32 and square with a 16 px glyph (the handoff value), but take their colours from the system caption buttons: glyph #D9FFFFFF, #73FFFFFF while the window is inactive, hover #14FFFFFF with a white glyph, pressed #0AFFFFFF, and they fade with the caption glyphs (300 ms out, 150 ms in). Move the caption colours from `MainWindow` into tokens so the gear and the system buttons read one source | S |
| P7.6 | Real blur of the video behind the card (spike first). After mpv renders into the back buffer, the render thread runs one more pass in the same ANGLE GL context: copy the card's rectangle (plus 24 px for the shadow) into a quarter-size texture, blur it in two passes, and draw it back clipped to the card's radius 24 shape, then draw the OSD shadow (0 8 24, black 32%) around it, before present. The XAML card keeps its 62% tint on top, so contrast does not change. The UI thread posts the card's rectangle in pixels, its visibility and the fade start time, like the swap-chain size; the render thread fades the blur over the same 300 ms / 150 ms (instant with reduced motion). While paused, any change to the card (show, hide, move, resize) asks for a redraw, and the fade forces redraws until it ends. Small textures are rebuilt per device generation. Off in picture in picture and with no card. Keep it in its own class that the loop calls once per frame, so the loop itself changes by a few lines. Record the outcome in ADR-0013. If the spike shows tearing between the card and the blur, or the pass costs more than about 0.3 ms at 1080p, stop and keep P7.2 | M |
| P7.7 | On-screen check at 640×400, 1280×720 and full screen, over a bright and a dark clip, playing and paused: card 520 × 84 with radius 24; play matches the handoff; the blur follows the video, fades with the card and stays put while paused; gear the same weight and hover as minimise; timecodes readable over white | S |

**Pre-Pass 7 means:** The player bar matches the screen 5 design: a soft rounded card that lets the video show through, blurred behind it, with a title bar that reads as one Windows 11 strip.

**Deferred (after the UI):**

- **Customisable bar:** choosing and ordering the bar's buttons. Not before the design is matched.
- **Settings in full screen:** the title bar is hidden there, so the gear is not reachable; the right-click menu (7.1) covers it with Settings Ctrl+,.
- **Appearance & theme customization:** exploring deeper theme accents and appearance tweaks post-Pass 7 before public release.

---

### Pass 7: Menus, Toasts, Diagnostics & Playback Engine Polish

Mockups: screens 9 (menus), 10 (notifications) and 8.1–8.2 (diagnostics).

| # | Task | Effort |
|:---|:---|:---:|
| 7.1 | Right-click `MenuFlyout` with icons, shortcuts, submenus (including Clips and Stream), max height with scroll; tracks and video flyouts | M |
| 7.2 | Toast control: look, placement, fade, reduced motion, live-region announcement | S |
| 7.2a | `ToastService`: one visible at a time, queued by priority, a toast with an action is never replaced, duplicates collapse, 4 s / 8 s with an action, pauses on hover and focus | M |
| 7.3 | Diagnostics panel (`Ctrl+I`) on a solid surface: decoder, video, frame rate (`container-fps` / `estimated-vf-fps`), display refresh (`display-fps`), dropped frames (`decoder-frame-drop-count` · `frame-drop-count`), cache ("—" when unavailable). No GPU row | S |
| 7.4 | Auto-detect display refresh rate & smooth motion display sync: query window monitor refresh via `MonitorFromWindow` + `EnumDisplaySettingsW` (or `DwmGetCompositionTimingInfo`), set mpv's `display-fps-override`, and update on `WM_DISPLAYCHANGE` and monitor transitions. Resolves 120 fps video decimation under `video-sync=display-resample` | M |
| 7.4a | 120 fps / high-refresh render loop pacing & VSync audit: review `MpvRenderLoop` latency wait order; evaluate `DXGI_PRESENT_ALLOW_TEARING` / VRR on 120Hz/144Hz/240Hz monitors to eliminate micro-judder | M |
| 7.4b | libmpv property wiring for diagnostics: observe `hwdec-current`, `video-format`, `video-params/w` and `h`, `container-fps`, `estimated-vf-fps`, `display-fps`, `decoder-frame-drop-count`, `frame-drop-count`, and `demuxer-cache-duration` in `PlayerEventDispatcher` | S |
| 7.5 | Wire engine failures, yt-dlp updates, frame copy and export results to the toast service | S |
| 7.6 | Tests for toast queue rules and diagnostics formatting | S |
| 7.7 | VLC parity settings (Settings → Video, Streaming, Advanced): deinterlacing (`deinterlace` & mode), frame snapshot settings (`screenshot-directory`, `screenshot-format`, `screenshot-template`), aspect ratio override (`video-aspect-override`), network cache buffer slider (`demuxer-max-bytes`, readahead), HTTP proxy (`http-proxy`), RTSP transport (`rtsp-transport`), corrupt AVI index repair (`index=recreate`), and skip H.264 deblocking (`vd-lavc-skiploopfilter`) | M |
| 7.8 | High-value playback & audio features: audio pitch correction on playback speed changes (`audio-pitch-correction=yes`), stereo downmix for multi-channel audio tracks, instant copy frame to clipboard (`Ctrl+C`), and display sleep prevention (`DisplayRequest` / `SetThreadExecutionState`) | S |
| 7.9 | Unit and integration tests for new playback options, settings sync mapping, and display FPS monitor tracking | S |

**Pass 7 means:** Menus, toasts and diagnostics behave as the redline describes, 120 fps and high-refresh recordings play with frame-accurate smoothness, and VLC-parity playback settings are configurable without compromising the minimalist shell.

---

### Pass 8: Picture in Picture

Mockups: screens 8.3–8.4.

| # | Task | Effort |
|:---|:---|:---:|
| 8.1 | Picture in picture: `AppWindow` CompactOverlay presenter at 400×225 (spike first: confirm the minimum size), hover controls, "Back to full window" beside the system close button, `P` shortcut (ignored when a text field has focus) | M |
| 8.2 | Engine hand-over between the main window and the overlay window without a decode restart (swap chain, device and lifetime safety) | M |
| 8.3 | Tests for the hand-over lifecycle and picture-in-picture state transitions | S |

**Pass 8 means:** The video can float in a small always-on-top window and return to the full window without a stall.

---

### Pass 9: Embedded Kestrel LAN Streamer & Release Verification

| # | Task | Effort |
|:---|:---|:---:|
| 9.1 | `KestrelStreamServer`: lightweight HTTP 206 partial content server with session token auth | M |
| 9.2 | Minimal HTML5 mobile player shell at `/watch?t={token}` with zero transcoding | S |
| 9.3 | `QrPairingService`: in-memory SVG/PNG QR code generator with local LAN IP flyout (`S` key) | S |
| 9.4 | Release hardening: Native AOT build (`dotnet publish -c Release -r win-x64`); fix the Release build's IL2104 trim warnings from `WinRT.Runtime` / `Microsoft.Windows.SDK.NET`, then move CI from Debug to Release | M |
| 9.5 | End-to-end performance audit: cold start < 300ms, zero hot-path GC allocations | M |
| 9.6 | `EngineResilience`: attach/detach context cleanup, attached core disposal device-loss safety, queue overflow resync, background core destruction | M |
| 9.7 | `BinaryVerification`: libmpv and ANGLE binary integration, real media decode smoke test | S |

**Pass 9 means:** Instant mobile pairing via QR code for LAN video streaming, fully verified Native AOT release build with 0 dropped frames.

---

### Parked

- **Hover rail (screen 6):** deferred. Clips and Stream are reached from the right-click menu and shortcuts. The spec stays in the design handoff.
- **Re-encode export (NVENC / QSV / AMF / x264):** not planned. Export is stream copy only. Revisit for non-keyframe cuts or size-limited exports.
- **GPU timing row in Diagnostics:** libmpv doesn't expose it. Would need our own D3D11 timestamp queries.
- **Link playback (yt-dlp streaming):** not scheduled yet. When it lands, the empty-state hint must switch to "Drop a file, paste a link or press Ctrl+O", and Play in the Open URL dialog must start the stream instead of showing "Links can’t be played yet." (`OpenUrlFlow` takes `streamingAvailable`).
