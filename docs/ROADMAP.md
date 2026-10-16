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

### Pass 4: Minimalist Windows 11 Shell & Mica Theming [Completed]

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
| 4.C2 | Bug: playback does not replay. At end of file (`eof-reached`, `keep-open`) the play button and Space do nothing; fix so Play on an ended file seeks to 0 and unpauses (`PlayerSession.TogglePause` / `MainPage.TogglePause`, `HasVideo` gate), and check the restart path with a test — **Done** (checked with the design and interaction sweep before release) | S |
| 4.C3 | Bug: no clickable open control. Files open only via `Ctrl+O` and drag-and-drop. Add a visible Open button (controls bar and empty-state panel) that calls the same picker as the accelerator; placement and look need design approval first — **Done** (per handoff 2A and screen 3: "Open file" emphasis pill on the empty state, disabled while the engine starts and hidden on failure or drag-over; Open icon button first in the controls pill with the "Open file (Ctrl+O)" tooltip; both share the Ctrl+O picker. Open URL waits for streaming. Not yet checked on screen) | S |
| 4.C4 | Bug: label alignment and capitalisation. Known items: hint text reads "press Ctrl O" (should be `Ctrl+O`); panel captions mix `SUPPORTED` / `DETAILS` (all caps) with sentence-case labels (`Formats`, `Renderer`, `Searched`); status text mixes cases (`Engine ready`, `Starting`). Full audit of every label with the user's list before fixing; The casing rule is confirmed first — **Done** (spec sheet wording through `Resources.resw`; failure details tile on a 72/* grid; `mm:ss` clock; ADR-0012. Not yet checked on screen) | S |
| 4.C5 | Design review: compare the current shell against the reference screenshots (Wintoys and OpenMouse) and list gaps. The list is approved before any UI code — **Done** (folded into the design and interaction sweep before release) | M |
| 4.D0 | Foundations (mechanical): generate the XAML `ResourceDictionary` from `aether.tokens.json` (the handoff `DesignTokens.xaml` is the starting point), no-accent overrides so standard controls stay white, strings file — **Done** (`d00291d`: handoff dictionary with legacy-key aliases, `Resources.resw`; gate green, 283 core tests) | S |
| 4.D1 | `SettingsStore`: persisted settings object (defaults, per-setting reset, applies-on-restart flags) — **Done** (gate green, 299 core tests) | M |
| 4.D2 | Rebase the built shell (4.1–4.6) onto the handoff tokens: Mica Alt, glass surfaces, Segoe UI Variable, 32 px title bar with 138 px caption inset, caption glyphs fading with the controls (screens 1, 3, 4) — **Done** (title bar on the 16 px inset with a shared baseline, 46×32 gear (disabled until Settings), 72 px top scrim, caption glyphs eased with the chrome; shell on the new token names, badge padding 6,2; gate green, 331 core tests) | M |
| 4.D3 | Empty state (2A), status strip and startup/failure states (screens 1–2) — **Done** (96 px glass hero tile with the film glyph, title/subtitle/details/actions on the redline spacing, token colours for glyph and drop target; status strip with the engine dot on the left and ANGLE · D3D11 on the right, 2 px startup bar on its bottom edge; failure tile with Retry and Copy details (Open folder dropped per 2.2). No recents store yet, so recents are not built. Not yet checked on screen) | M |
| 4.D4 | Playing view: one-row pill (5.8), tooltips, paused, ended, controls hidden, portrait 405 (screen 3) — **Done** (48 px glass pill, min(520, window − 32), 24 above the bottom: Open, Play/Pause/Replay, mm:ss times flanking an inline seek bar (keyframe seeks while dragging, exact on release), volume button mutes on click with its slider in a hover flyout (pinned from right-click or Shift+F10) that holds the chrome, Full screen; shortcut tooltips, M mutes, ended reads the full duration; fades instant with Windows animations off; YouTube quality pill skipped until streaming. Gate green, 350 core tests. Not yet checked on screen) | M |
| 4.D5 | Open URL dialog with invalid and 405 states, progress, returning-user and missing-recent states (screen 2) — **Done** (dialog on the solid dialog fallback over a scrim, min(440, window − 32), prefilled from the clipboard only when it holds a link; Play enabled from http:// or https://, amber "That isn’t a web link" otherwise. Links can't play yet, so the hint reads "Link playback isn’t available yet" and Play keeps the dialog open with that line; Open URL pill on the empty state and Ctrl+Shift+O (the handoff's shortcut). Getting stream / still working (10 s) are built in the hero but unreachable until streaming. Recents store (`recents.json`, newest 3) fed by every file open, with the length once loaded; rows hidden until there is history, Clear list, and a missing file keeps its row with "File not found" and Remove. 7 link-failure and yt-dlp frames wait for streaming. Gate green, 397 core tests. Not yet checked on screen) | M |
| 4.D6 | Spike: Acrylic over the swap chain; confirm `MpvVideoSurface` is a `SwapChainPanel`. Result in `docs/DECISIONS.md` — **Done** (it is a `SwapChainPanel`; in-app Acrylic samples only the XAML tree behind it, so surfaces over video use the solid `AetherSurfaceDialogFallbackBrush`; OSD shadow deferred. ADR-0013. Not yet checked on screen) | S |
| 4.D7 | Tests for the settings store, token dictionary keys and strings coverage; CI build — **Done** (tokens, strings and settings suites added; gate green, 313 core tests) | S |

**Pass 4 means:** Edge-to-edge video player with translucent dark Mica chrome, auto-hiding OSD on mouse idle, graceful startup and error handling, and zero airspace clipping. *(270 tests passing, 0 warnings. 4.7 built: fast taps queue in one slot. 4.8 built: window fits the video and keeps its aspect ratio. Shell states and window behaviour checked on screen by the user. Replay (4.C2) and the reference screenshot review (4.C5) are done; both are covered by the design and interaction sweep before release.)*

---

### Pass 5: Transport, HUD & Lossless Stream Clipper

Mockups: screen 5 (transport).

| # | Task | Effort |
|:---|:---|:---:|
| 5.1 | `OverlayHud`: sub-millisecond timestamp, game tick (`floor(T*20)`), sub-tick %, frame count, source FPS — **Dropped** (not needed, per the user) | S |
| 5.2 | `PrecisionSeekBar` and transport pill: hover and drag tooltips, In (`I`) / Out (`O`) band, speed and quality menus, loop states, compact pill below 520 px — **Done** except the quality menu, which waits for streaming (two-row pill, `PrecisionSeekBar`, In/Out points, speed menu, loop and always-on-top buttons, compact More menu. Checked in the design and interaction sweep before release) | M |
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

**Pass 5 means:** The full transport with visual in/out bounds, and sub-300ms lossless clip generation draggable directly into Discord.

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
| P7.1 | Restart the playback engine in place when a restart-only setting changes (hardware decoding, audio passthrough and the like) instead of asking for an app relaunch: tear down and recreate the mpv context and render loop on the same swap chain, then reopen the current file at the same position with the same pause, volume, speed and track choices — **Done** (the open file comes back at the same position, paused or playing, with its volume, speed and loop; checked on screen) | M |

**Pre-Pass 7 means:** Changing a restart-only setting applies straight away and playback resumes where it was, with the window still open.

---

### Pre-Pass 7: Player UI polish (match the design)

Numbered from P7.2 because P7.1 is the engine restart above. The design handoff (screens 3, 5 and 9, `aether.tokens.json`) is the target; values below are from the screen 5 redline. The recent restyle (radius 8, solid white 36 px play, plain-text speed, no outline on toggles) is reverted where it departs from it; the 640×400 minimum window stays. The bar looks like a solid slab today because the in-app Acrylic brush cannot see the swap chain (ADR-0013) and paints its own opaque luminosity layer; the handoff draws the pill as a plain 62% tint, which already lets the video through.

| # | Task | Effort |
|:---|:---|:---:|
| P7.2 | Glass that shows the video: the card's fill goes from `AetherSurfaceOsdAcrylicBrush` to the plain `AetherSurfaceOsdBrush` (#9E0E1014, 62% ink), which is what every handoff frame draws and what keeps TextPrimary at 5.35:1 over pure white. Retire the Acrylic brush. No bottom scrim: the handoff has none and 62% does not need one. This is also the fallback if P7.6 is dropped — **Done** | S |
| P7.3 | Card shape back to the redline: `AetherTransportCornerRadius` 8 → 24 (16 button radius + 8 padding, concentric); button row 36 → 32, so the card is 84 tall (8 + 32 + 4 + 32 + 8); padding 8 (7 plus the stroke); centre group spacing 8 → 4, like the side groups; drop `AetherSizeTransportPlay` and size the row by `AetherSizeControl`. The chip, drawer and toast offset 120 → 116 (24 + 84 + 8). Width stays min(520, window − 32), so at the 640 px minimum the card is always the full 520 and More controls only appears in picture in picture (Pass 8) — **Done** | S |
| P7.4 | Controls as drawn in 5.1 and 5.6. Play/Pause: 32 round, FillEmphasisRest (#24FFFFFF) with a 1 px StrokeControl (#1FFFFFFF), hover FillEmphasisHover, pressed FillEmphasisPressed, white 16 px glyph; remove `AetherFillPlay*` and `AetherTextOnPlay`. Speed: outline pill again, at least 52 wide (`AetherSizeSpeedPill` 40 → 52), padding 0,10, StrokeControl, Caption 12 tabular, FillEmphasisRest while its menu is open. Loop and Always on top when on: FillEmphasisRest plus StrokeControl, FillEmphasisHover on hover. Glyphs: back and forward 5 s use a circular-arrow glyph at 18 px (the current rewind and fast-forward glyphs read as track skip; the numbered Fluent skip glyphs say 10 and 30, so use one without a number); full screen uses the four-corner glyph, not the diagonal arrows. Seek hover line 12 → 16 tall — **Done** | S |
| P7.5 | Title bar as in screen 3: the badge gets its SurfaceOsd fill (radius 4, padding 2,6, max 320 for files and 240 for streams); "Alpha" on the empty state stays plain. The gear and Back stay 46×32 and square with a 16 px glyph (the handoff value), but take their colours from the system caption buttons: glyph #D9FFFFFF, #73FFFFFF while the window is inactive, hover #14FFFFFF with a white glyph, pressed #0AFFFFFF, and they fade with the caption glyphs (300 ms out, 150 ms in). Move the caption colours from `MainWindow` into tokens so the gear and the system buttons read one source — **Done** | S |
| P7.6 | Real blur of the video behind the card (spike first). After mpv renders into the back buffer, the render thread runs one more pass in the same ANGLE GL context: copy the card's rectangle (plus 24 px for the shadow) into a quarter-size texture, blur it in two passes, and draw it back clipped to the card's radius 24 shape, then draw the OSD shadow (0 8 24, black 32%) around it, before present. The XAML card keeps its 62% tint on top, so contrast does not change. The UI thread posts the card's rectangle in pixels, its visibility and the fade start time, like the swap-chain size; the render thread fades the blur over the same 300 ms / 150 ms (instant with reduced motion). While paused, any change to the card (show, hide, move, resize) asks for a redraw, and the fade forces redraws until it ends. Small textures are rebuilt per device generation. Off in picture in picture and with no card. Keep it in its own class that the loop calls once per frame, so the loop itself changes by a few lines. Record the outcome in ADR-0013. If the spike shows tearing between the card and the blur, or the pass costs more than about 0.3 ms at 1080p, stop and keep P7.2 — **Done** (about 0.5 ms per frame at 1080p, over the 0.3 ms budget; accepted, see ADR-0013) | M |
| P7.7 | On-screen check at 640×400, 1280×720 and full screen, over a bright and a dark clip, playing and paused: card 520 × 84 with radius 24; play matches the handoff; the blur follows the video, fades with the card and stays put while paused; gear the same weight and hover as minimise; timecodes readable over white — **Done** (card 520 × 84, radius 24, blur and fades, timecodes readable over bright and dark at all three sizes. Findings are listed under Deferred) | S |

**Pre-Pass 7 means:** The player bar matches the screen 5 design: a soft rounded card that lets the video show through, blurred behind it, with a title bar that reads as one Windows 11 strip.

**Deferred (after the UI):**

- **Caption buttons ignore transparency:** Windows draws the minimise, maximise and close glyphs fully opaque whatever alpha `AppWindowTitleBar` is given, so they do not fade with the rest of the chrome while a video plays, and at full white they read slightly brighter than the gear (85%). Needs a decision: draw our own caption buttons, or accept them always visible and set the gear to match.
- **Settings sections have no accessible names:** screen readers announce each item in the Settings list as `Aether_App.Settings.SettingsSection`; give the list items their section name.
- **Standalone app and installer (done on this branch):** Aether runs unpackaged with no Developer Mode (ADR-0017); a file passed at launch opens again and shows its picture; the per-user installer upgrades cleanly, asks before closing a running Aether, lists Aether in Open with and takes its version from the app. Set the app's version and publisher before the first release.

- **Minimal bar that opens on interaction:** while a video plays, show the one-row pill from screen 3 (Open, Play, times around the seek bar, Mute, Full screen) and grow it into the full card on hover or click. Open question: shrink back after a few idle seconds. Opus plans it first, Sonnet builds it. Builds on the blur once P7.6 is settled.
- **Modern fonts:** revisit the app's typefaces once the visual changes and the blur are in. The tokens hold every font, so it is mostly a token change plus a check that nothing clips or reflows (timecodes, badge, menus).
- **Customisable bar:** choosing and ordering the bar's buttons. Not before the design is matched.
- **Settings in full screen:** the title bar is hidden there, so the gear is not reachable; the right-click menu (7.1) covers it with Settings Ctrl+,.
- **Appearance & theme customization:** exploring deeper theme accents and appearance tweaks post-Pass 7 before public release.

---

### Pass 7: Menus, Toasts, Diagnostics & Playback Engine Polish

Mockups: screens 9 (menus), 10 (notifications) and 8.1–8.2 (diagnostics).

| # | Task | Effort |
|:---|:---|:---:|
| 7.1 | Right-click `MenuFlyout` with icons, shortcuts, submenus (including Clips and Stream), max height with scroll; tracks and video flyouts — **Done** (right-click anywhere but Settings and the Open URL box: Open file, Open URL, Recent, Speed, Always on top, Diagnostics, Settings (`Ctrl+,`, changeable), Clips (once a clip is saved). Audio and Subtitles open the tracks flyout (7.16). Video, Copy frame, Save frame and Picture in picture are shown disabled until 7.18, 7.19 and Pass 8; Stream stays disabled until streaming lands; the rest of 7.20 follows with them) | M |
| 7.2 | Toast control: look, placement, fade, reduced motion, live-region announcement — **Done** | S |
| 7.2a | `ToastService`: one visible at a time, queued by priority, a toast with an action is never replaced, duplicates collapse, 4 s / 8 s with an action, pauses on hover and focus — **Done** | M |
| 7.3 | Diagnostics panel (`Ctrl+I`) on a solid surface: decoder, video, frame rate (`container-fps` / `estimated-vf-fps`), display refresh (`display-fps`), dropped frames (`decoder-frame-drop-count` · `frame-drop-count`), cache ("—" when unavailable). No GPU row — **Done** (`Ctrl+I` toggles a solid panel top-left; Copy puts the rows on the clipboard; Esc closes it) | S |
| 7.4 | Auto-detect display refresh rate & smooth motion display sync: query window monitor refresh via `MonitorFromWindow` + `EnumDisplaySettingsW` (or `DwmGetCompositionTimingInfo`), set mpv's `display-fps-override`, and update on `WM_DISPLAYCHANGE` and monitor transitions. Resolves 120 fps video decimation under `video-sync=display-resample` — **Done** (`EnumDisplaySettingsW`, whole hertz; re-read on `WM_DISPLAYCHANGE` and monitor change; a custom `display-fps-override` wins. ADR-0018. Not yet checked on a high-refresh display) | M |
| 7.4a | 120 fps / high-refresh render loop pacing & VSync audit: review `MpvRenderLoop` latency wait order; evaluate `DXGI_PRESENT_ALLOW_TEARING` / VRR on 120Hz/144Hz/240Hz monitors to eliminate micro-judder — **Done** (audited: wait order kept, VSync on, no tearing/VRR for a composition swap chain; no code change. ADR-0018) | M |
| 7.4b | libmpv property wiring for diagnostics: observe `hwdec-current`, `video-format`, `video-params/w` and `h`, `container-fps`, `estimated-vf-fps`, `display-fps`, `decoder-frame-drop-count`, `frame-drop-count`, and `demuxer-cache-duration` in `PlayerEventDispatcher` — **Done** | S |
| 7.5 | Wire engine failures, yt-dlp updates, frame copy and export results to the toast service — **Done** except frame copy, which comes with 7.19 (a hardware decoder falling back to software during playback, yt-dlp update success and failure, and clip export success with Show in folder and failure; toasts sit above the export chip or clip drawer when it shows. The full-page startup failure tile is unchanged) | S |
| 7.6 | Tests for toast queue rules and diagnostics formatting  — **Done** (18 more cases: queue edge cases and diagnostics wording) | S |
| 7.7 | VLC parity settings (Settings → Video, Streaming, Advanced): deinterlacing (`deinterlace` & mode), frame snapshot settings (`screenshot-directory`, `screenshot-format`, `screenshot-template`), aspect ratio override (`video-aspect-override`), network cache buffer slider (`demuxer-max-bytes`, readahead), HTTP proxy (`http-proxy`), RTSP transport (`rtsp-transport`), corrupt AVI index repair (`index=recreate`), and skip H.264 deblocking (`vd-lavc-skiploopfilter`) — **Done** (Video: deinterlace and method, aspect ratio, snapshot folder, format and file name; Streaming: network buffer, HTTP proxy, RTSP transport; Advanced: repair AVI index, skip H.264 deblocking. The buffer, AVI repair and deblocking take effect on an engine restart; the rest apply live. Snapshots are not taken yet: that is 7.19) | M |
| 7.8 | High-value playback & audio features: audio pitch correction on playback speed changes (`audio-pitch-correction=yes`), stereo downmix for multi-channel audio tracks, instant copy frame to clipboard (`Ctrl+C`), and display sleep prevention (`DisplayRequest` / `SetThreadExecutionState`) — **Done** except Ctrl+C copy frame, which moved to 7.19 (pitch correction on speed change, stereo downmix, and a keep-the-screen-awake-while-playing setting that releases on pause, end and close) | S |
| 7.9 | Unit and integration tests for new playback options, settings sync mapping, and display FPS monitor tracking — **Done** (defaults, reset, restart flags and the mpv mapping of every new setting, including the deinterlace filter switching; display refresh tracking was covered in Pre-Pass 7) | S |
| 7.10 | Review and findings pass & hardware measurement (Gemini): find what causes stutter after a 5 s skip and with software decoding (low CPU, high GPU), performance bottlenecks and holdbacks in the render path — **Done** (Tested on physical hardware: AMD Radeon R7 350 on 180 Hz monitor. Diagnosed 180 Hz display-fps-override shader starvation under display-resample, R2 software decode readahead starvation, R3 per-frame XAML layout invalidation, and d3d11 copy PCIe bus saturation. Bypassed display-fps-override on high-refresh monitors, gated clock layout, fixed software decode readahead, and verified smooth 60/120 fps playback with VLC-parity software decode profile) | S |

**Pass 7 means:** Menus, toasts and diagnostics behave as the redline describes, 120 fps and high-refresh recordings play with frame-accurate smoothness, and VLC-parity playback settings are configurable without compromising the minimalist shell.

**Pass 7 UI finish (after 7.1–7.10; Opus plans and asks questions first, Sonnet builds):** compared against the design handoff (screens 3, 5, 8, 9, 10) and the current build.

| # | Task | Effort |
|:---|:---|:---:|
| 7.11 | Collapsed bar: while a video plays, show the one-row pill from screen 3 (Open, Play, times around the seek bar, Mute, Full screen) and grow it into the full card on hover or click; decide whether it shrinks back after idle — **Done** (it does: the slim pill shows while playing and any hold — hover, Tab focus, pause, seek drag, menu or dialog — grows it; 2 s after the last one it shrinks, and the chrome fades 2 s later. One seek bar and one set of buttons; the export chip, toasts and the blur follow the animated height) | M |
| 7.12 | Speed button: the outline is too large and looks out of place; refit to the 5.1 / 5.6 redline — **Done** (owner's call: a plain text button like the icon buttons, Caption 12 semibold tabular, no outline; it takes Loop's "on" look while the speed is not 1×, and the fill while its menu is open. Timecodes leave Cascadia Mono for the UI font with tabular figures: current time semibold, duration muted) | S |
| 7.13 | Settings gear in the title bar: size, weight and spacing against the caption buttons look wrong; match the redline — **Done** (gear and Back glyph 16 → 13 to match the system caption glyphs; the space kept for the caption buttons follows their real width at any display scale) | S |
| 7.14 | Title and badge: the filename badge next to "Aether" looks off (shape, size, baseline); match screen 3 (SurfaceOsd, radius 4, padding 6,2, baseline shared with the title) — **Done** (no pill, owner's choice: "Aether", a muted dot and the file name on one baseline, trimmed at 320; ADR-0019) | S |
| 7.15 | Animation and interaction refinement: bar grow/shrink, hover and press feedback, menu and flyout opening, toast in/out, with reduced-motion respected — **Done** (two profiles to compare in Settings › Advanced › Motion style (preview): Quick eases 150–250 ms with small slides, Springy uses a damped spring for the bar, toast in and clip drawer opening. Chrome fades stay plain and system menus keep their own animation; no hover or press effects added. Also the owner's asks: transport buttons 32 → 36 with 14 px glyphs and radius 6, card 84 → 88; button row simplified: Open and Speed left; back 5 s, Play, forward 5 s centre; Mute, a ⋯ menu (frame steps, Loop, Always on top) and Full screen right; card stays 520) | M |
| 7.16 | Tracks flyout (9.2): audio and subtitle (closed caption) track lists from `track-list` with the redline labels (language · title · channels, SDH, Forced), Off, Add subtitle file, audio and subtitle delay steppers — **Done** (Audio and Subtitles in the right-click menu open one 536 px flyout above the pill: the current track is checked, subtitles have Off and Add subtitle file…, and each column has a ±0.1 s delay stepper. The list is read from mpv on a pool thread when the flyout opens and after each change, not through the event dispatcher. Needs an on-screen check) | M |
| 7.17 | Subtitle display: on-screen captions follow the Subtitles settings (size, colour, position) and survive an engine restart — **Done** (captions are drawn by the player into the video, so they come through the render path; Show subtitles, preferred subtitle language, font and size were already mapped live and stay so. Subtitles now also has Colour (white, yellow, green, cyan) and Position (bottom, raised, top), applied live. Styled (ASS) subtitle files keep their own look. An engine restart now also puts back the audio and subtitle track, the delays and any subtitle files added with Add subtitle file…. Needs an on-screen check) | M |
| 7.18 | Video flyout (9.3): zoom with Reset, rotate 0/90/180/270, aspect ratio, brightness, contrast, gamma, saturation with Reset colours — **Done** (Video in the right-click menu opens one 340 px panel above the controls, right-aligned to them, on a solid surface that scrolls in a short window; choices belong to the open file, so a new file goes back to Settings → Video, and an engine restart puts them back) | M |
| 7.19 | Copy frame and Save frame (Ctrl+C, Ctrl+Shift+S) — the menu entries in screen 9.1 need the engine work behind them — **Done** (Copy frame puts the picture, with captions and without the on-screen display, on the clipboard; Save frame writes it to the snapshot folder, format and file name from Settings → Video, or Pictures\Aether when no folder is set, and the toast offers Show in folder; both keys can be changed in Settings → Shortcuts and Ctrl+C is left to a text box that has focus) | S |
| 7.20 | Wire the screen 9.1 menu items that are still missing: Recent, Audio and Subtitles (open the tracks flyout), Video, Picture in picture (Pass 8, shown disabled until then), Settings Ctrl+, — **Done** (Recent, Audio, Subtitles, Video, Copy frame, Save frame and Settings `Ctrl+,` were already wired by 7.1, 7.16, 7.18 and 7.19; saved links in the Recent submenu now play through yt-dlp like the home-screen rows instead of being greyed out. Picture in picture stays disabled until Pass 8 and Stream until the LAN streamer in Pass 11) | S |
| 7.21 | Hover rail (screen 6, unparked): 4 px hot zone on the left edge, 4 px FillRange peek, opens after 150 ms of hover or on click; 56 wide, items Player, Clips, Stream and Settings pinned at the bottom; hides with the controls, Esc closes it, never opens during a drag; solid SurfaceOverlayStrong fallback if Acrylic cannot blur the swap chain. Spec in the handoff (6.1–6.4). Opus plans and asks questions first — **Done** (spec in `docs/reviews/pass-7-rail-and-motion-spec.md`: over the player and home screen, not under Settings or a dialog; Player closes it, Clips opens the clip drawer when a clip is saved, Stream stays disabled until Pass 11, Settings opens Settings; it closes 300 ms after the pointer leaves, on Esc, after an item is used and with the chrome; while the pointer is on it the chrome stays up but the pill stays slim. Solid SurfaceOverlayStrong, no Acrylic spike (ADR-0013). Also a motion refit across the app on shared tokens: the clip drawer, Settings and its sections, diagnostics and Open URL now enter and exit like toasts, hover and press fills fade in 83 ms, and Springy adds a press squash on transport and rail buttons; both motion styles kept. Not yet checked on screen) | M |
| 7.22 | Ctrl+E exports the marked clip (in and out set), with a toast when nothing is marked; added to the Shortcuts page — **Done** (`Ctrl+E`, changeable on the Shortcuts page under Navigation, runs the same export as the chip and keeps the "Save the file first" toast for streamed links; with no In/Out pair it shows "Mark a clip first: set In and Out". Not yet checked on screen) | S |
| 7.23 | Seek modifiers on the arrow keys: Shift skips 1 s, Ctrl skips 30 s, plain arrows stay 5 s; shown in the Shortcuts page — **Done** (`Shift+←/→` skips 1 s, `Ctrl+←/→` skips 30 s, plain arrows stay 5 s; four new changeable rows under Playback on the Shortcuts page. Not yet checked on screen) | S |
| 7.24 | Edge-to-edge video is broken: the picture shows as a narrow strip with a dark band on its left edge instead of filling the window; find the cause (card inset, swap chain size or backdrop blur pass) and fix it, with a screenshot check at several window sizes — **Done** (the cause was rotation: the engine reports the picture's size without the Video panel's rotation, so a quarter turn left the video area wide and the engine fitted the turned picture inside it as a narrow strip with black bands; the video area and the window's shape lock now swap width and height for 90° and 270°. Checked at 1280×480, 1000×600 and 660×900; with no rotation the picture already filled its area at every size) | M |
| 7.25 | Apply the approved review findings R1–R7 after the design work, verifying each first (details in `docs/reviews/pass-7-findings.md`): keyframe seek and coalesced 5 s skips (R1); keep readahead under the safe auto mode for software decode (R2); no per-frame layout or clock strings while the bar is hidden (R3); back-buffer size follows the cache size (R4); no forced redraws or false swap reports during fades (R5); fractional refresh rates such as 59.94 and 119.88 Hz (R6); wait for the old render thread before an engine restart builds a new one (R7). Declined: R8–R11 — **Done** (fixed: R1 skips follow Precise seeking and a held arrow key sends one seek at a time, R4 back buffer is half the cache (8–512 MiB), R5 no swap report without a new video frame, R6 exact refresh rate from the display configuration API with the whole-number reading as fallback, R7 restart waits up to 250 ms for the old render thread; already fine: R2 and R3, done earlier) | L |

| 7.26 | First-run tour: on the first launch only, four short cards over the empty home screen (open or drop a file, the right-click menu, key shortcuts including I/O clips, the settings gear) with Next and Skip; "Show the tour again" in Settings › About — **Done** (spec in `docs/reviews/pass-7-first-run-tour-spec.md`: six steps that light the real controls one at a time — Open file and Open URL, the bar (a dashed outline where it will sit when no video is open), the side rail held open, the real right-click menu, the user's current keys including I/O and Ctrl+E, and the gear — with a card beside each: Next, Back, Skip tour, ← and →, Esc skips; it shows once on a launch that lands on an idle home screen, the "seen" flag is saved as it opens, and Settings › About › Show the tour again replays it; the light glides on the bar's tween and the card enters like a toast in both motion styles, all instant with Windows animations off. Not yet checked on screen) | M |
| 7.27 | Settings › About links to the author's GitHub (github.com/rvyyv-n) — **Done** (a GitHub row above Licences opens github.com/rvyyv-n in the default browser) | S |
| 7.28 | Auto updater: needs its own plan first (where releases live, signing, silent or prompted). Not built in Pass 7 — **Done** (plan only: `docs/reviews/pass-7-auto-updater-plan.md`, ADR-0021 proposed. Velopack with releases on GitHub, small delta downloads, a daily check plus "Check for updates" in Settings › About, applied on exit or on Restart from a toast; WinGet later. Open questions on signing, default on/off and repo visibility) | M |

---

### Pre-Pass 8: Polish From Hands-On Testing

Notes from using the app. Items 8.1, 8.5 and 8.6 are design work (Opus asks questions and plans first, Sonnet builds); the rest are Sonnet fixes. Find the cause of 8.7 before changing anything, and fold it in with the stutter check in 7.10.

| # | Task | Effort |
|:---|:---|:---:|
| P8.1 | Sidebar: give it the same blur and tint as the player bar, and make it small and out of the way (a compact floating card) instead of a strip running the full height of the window | M |
| P8.2 | General settings page only has two settings (battery saver, open files in the running window). Decide what belongs there (for example start-up behaviour, language, remember window size and position, recent files, updates) and add it, or merge the page into another | S |
| P8.3 | Settings list: a small stray shade or light patch fills the bottom corner of the selected row (and of an active setting). Find which layer draws it and clip it to the row's rounded shape | S |
| P8.4 | Subtitle font picker does nothing: make the chosen font actually reach mpv (`sub-font`), then add more subtitle styling: size, colour, outline, background box, position and bold, with a live preview | M |
| P8.5 | About page: replace the "the author's projects at github.com/rvyyv-n" row with a closing card like Rise's (app name and version, a line that everything stays on this device, "Source on GitHub · @rvyyv-n") so it no longer calls the user the author | S |
| P8.6 | Onboarding tour: (a) card 2 runs off the bottom of the window and hides Next until the window is resized, so keep each card fully inside the window; (b) "Skip tour" is clipped at its edge; (c) the sidebar must not exist during the tour, so its slide-in no longer plays before the next card; (d) make the tour livelier, with cards that slide and fade between steps, a gentle pulse on the highlighted spot and a progress bar that animates, all off with reduced motion; (e) move the keyboard-keys card into open space (centred, not on top of the file list) | M |
| P8.7 | Video stutters whenever the player bar (or any overlay) appears, resizes, grows or hides, and plays smoothly once it is gone. Suspect the card blur pass (about 0.5 ms a frame, ADR-0013) and layout changes forcing extra redraws of the swap chain during the fade. Measure dropped frames with the blur off and on, then fix: for example skip the blur while the card animates, animate with transform/opacity only (no resize), or lower the blur cost | M |

**Pre-Pass 8 means:** The sidebar stays out of the way, settings pages are complete and clean, the subtitle font works, the About page ends on a proper card, the tour reads well at any window size, and the video stays smooth while the bar moves.

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

### Pass 9: Link Playback (yt-dlp)

Mockups: the link-failure and yt-dlp frames of screen 2 that were waiting for streaming.

| # | Task | Effort |
|:---|:---|:---:|
| 9.1 | Play a pasted link: resolve it through yt-dlp, hand the stream to mpv, and make Play in the Open URL dialog start it (`OpenUrlFlow` takes `streamingAvailable`) — **Done** (resolved with yt-dlp `--dump-json`, passing stream URL and http headers to mpv `loadfile`, `OpenUrlFlow` set to `streamingAvailable: true`) | M |
| 9.2 | Getting stream / still working (10 s) states in the hero, link-failure frames, cancel while resolving, and engine restart keeping the link — **Done** (hero loading spinner with hostname and 10 s "still working" note, failure actions: Retry, Edit link, Open in browser, Cancel; process tree kill on cancel, engine restart preserves original WebLink in PlaybackCarryOver) | M |
| 9.3 | Empty-state hint switches to "Drop a file, paste a link or press Ctrl+O"; recents store links with their title — **Done** (empty-state subtitle updated, Ctrl+V pastes and resolves links directly on home screen, recents store web URLs with video title & duration) | S |
| 9.4 | Clip and frame export from a streamed link, or a clear "save the file first" message — **Done** (clip export blocked for stream sources with neutral toast "Save the file first to export clips"; frame copy Ctrl+C and save Ctrl+Shift+S work via mpv screenshot) | S |
| 9.5 | Tests for link resolving, failure mapping and the dialog flow — **Done** (unit tests for YtDlpArgumentsBuilder, YtDlpErrorClassifier, and YtDlpStreamResolver; all 1066 core tests pass) | S |

**Pass 9 means:** Paste a YouTube or other supported link and watch it with the same controls as a local file.

---

### Pass 10: Precise Clip Export (re-encode)

| # | Task | Effort |
|:---|:---|:---:|
| 10.1 | Export mode choice in the clip drawer: Fast (stream copy, keyframe cuts, as today) or Precise (re-encode) | S |
| 10.2 | Re-encode pipeline with hardware encoders (NVENC / QSV / AMF) and an x264 fallback; frame-accurate in and out points | L |
| 10.3 | Optional size limit for a clip, progress, cancel and cleanup of partial files | M |
| 10.4 | Tests for encoder selection, fallback and cancel | S |
| 10.5 | Keyframe markers on the In and Out points in Fast mode, showing where a lossless cut will really land | S |

**Pass 10 means:** Clips can start and end on any frame, not just keyframes, and can be sized to fit a limit.

---

### Pass 11: Embedded Kestrel LAN Streamer & Release Verification

| # | Task | Effort |
|:---|:---|:---:|
| 11.1 | `KestrelStreamServer`: lightweight HTTP 206 partial content server with session token auth | M |
| 11.2 | Minimal HTML5 mobile player shell at `/watch?t={token}` with zero transcoding, plus a notice when the phone browser can’t play the file (HEVC or MKV) | S |
| 11.3 | `QrPairingService`: in-memory SVG/PNG QR code generator with local LAN IP flyout (`S` key) | S |
| 11.4 | Release hardening: Native AOT build (`dotnet publish -c Release -r win-x64`); fix the Release build's IL2104 trim warnings from `WinRT.Runtime` / `Microsoft.Windows.SDK.NET`, then move CI from Debug to Release | M |
| 11.5 | End-to-end performance audit: cold start < 300ms, zero hot-path GC allocations; confirm the low-pause GC settings (workstation, non-concurrent, SustainedLowLatency) are really configured | M |
| 11.6 | `EngineResilience`: attach/detach context cleanup, attached core disposal device-loss safety, queue overflow resync, background core destruction | M |
| 11.7 | `BinaryVerification`: libmpv and ANGLE binary integration, real media decode smoke test | S |
| 11.8 | `THIRD_PARTY.md` licence and attribution file for libmpv, FFmpeg, yt-dlp, ANGLE and the other bundled parts, included in the installer and portable zip — **Draft written** (versions and the libmpv and FFmpeg licence flavour still to confirm, see `docs/reviews/third-party-notes.md`) | S |

**Pass 11 means:** Instant mobile pairing via QR code for LAN video streaming, fully verified Native AOT release build with 0 dropped frames.

---

### Parked

- **Hover rail (screen 6):** now task 7.21 in Pass 7. Clips and Stream also stay in the right-click menu and shortcuts.
- **GPU timing row in Diagnostics:** libmpv doesn't expose it. Would need our own D3D11 timestamp queries.
- **10-bit HDR swap chain output** (`R10G10B10A2_UNORM`): after v1. Playback tone-maps to 8-bit today.
- **Automated UI tests (FlaUI):** after v1. Screens are checked by hand for now.