# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- Settings › About now has a GitHub row that opens the author's page (github.com/rvyyv-n) in the browser.
- Saved links in the right-click Recent menu can now be played, the same as from the home screen; Picture in picture and Stream stay greyed out until Pass 8 and Pass 11.
- Seek modifiers: `Shift+←/→` skips 1 second and `Ctrl+←/→` skips 30 seconds (plain arrows stay 5 seconds); all four keys are changeable on the Shortcuts page.
- Clip export shortcut: `Ctrl+E` exports the marked clip (changeable on the Shortcuts page), and shows a toast when no In and Out are set.
- Link Playback (yt-dlp, Pass 9):
  - Stream resolution with yt-dlp: resolves YouTube and 1000+ streaming sites using `--dump-json`, passing extracted direct stream URLs and required HTTP headers directly to libmpv.
  - Stream failure handling & user recovery: classified errors (private video, sign-in required, region-blocked, live not started, offline, unsupported site, missing yt-dlp) with actionable recovery options: Retry, Edit link, Open in browser, and Cancel.
  - Process tree isolation: concurrent asynchronous stdout/stderr draining with clean `Kill(entireProcessTree: true)` on user cancellation or timeout.
  - Hero state indicators: loading spinner displaying domain name with a 10s "still working..." notice.
  - Home screen link paste: `Ctrl+V` on empty state immediately triggers link resolution and playback; updated subtitle hint to "Drop a file, paste a link or press Ctrl+O".
  - Recents integration: remote streams saved in recent history with extracted video titles, durations, and original URLs.
  - Remote stream clipping guard: lossless clip export displays neutral toast "Save the file first to export clips" while frame copy (`Ctrl+C`) and frame save (`Ctrl+Shift+S`) continue working via mpv screenshot.
  - Engine restart carry-over: preserves `OriginalWebLink` across engine re-initialization and settings changes.
- Playback & high-refresh display pacing: bypassed forced display-fps-override on high-refresh displays (>60 Hz), restoring native presentation timing ("0") to prevent GPU 3D engine starvation under display-resample and spline36 on high-refresh displays (120/144/180 Hz).
- Playback performance: software decoding fallback under auto-safe now retains full frame readahead queue (fix R2); XAML layout updates and string allocations for clock and seek bar gated while controls are hidden (fix R3).
- UI finish: More (…) menu items now feature Segoe Fluent Icons (Back 1 frame, Forward 1 frame, Loop, Always on top); Springy and Quick motion profiles tuned for smooth, refined transitions.
- Housekeeping: the window title, About diagnostics lines, proxy hint and Diagnostics wording now come from the language file; unused language entries and design tokens, dead constants, the packaged-app manifest and logo images moved to `retired/`; native libraries for every architecture are ignored by git; the README no longer presents phone streaming as working before Pass 11.
- Roadmap: task 7.25 for the approved review fixes (R1-R7); review findings report added.
- Roadmap: task 7.24 for the broken edge-to-edge video.
- Roadmap: added Ctrl+E clip export and Shift/Ctrl seek steps (Pass 7), keyframe markers (Pass 10), a phone-browser support notice, GC check and licence file (Pass 11); 10-bit HDR and UI automation parked for after v1.
- Roadmap: link playback (yt-dlp) is now Pass 9 and precise re-encode clip export Pass 10; the LAN streamer and release work moved to Pass 11.
- Repository initialization.
- Architecture blueprints for WinUI 3 SwapChainPanel and libmpv D3D11 interop.
- PvP analysis toolset design specifications (Frame stepping, tick HUD, lossless trimming).
- Embedded Kestrel streaming host architecture with local QR code generation.
- Pass 6: Full Settings UI implementation adhering to Screen 7 design mockups across all sections (General, Playback, Video, Audio, Subtitles, Streaming, Shortcuts, Advanced, About) and compact responsive breakpoints.
- Custom title bar navigation transitioning smoothly to `← Settings`.
- Fast developer runner script (`run.ps1`, `tools/run.ps1`) for sub-3s incremental builds that launches the app directly.
- Aether runs as a standalone app without Developer Mode or certificates, with a per-user installer (`AetherInstaller.exe`, or `tools/Install.ps1`) and a portable build. The installer needs no admin rights, upgrades cleanly, asks before closing a running Aether, and adds Aether to Open with and to Installed apps.
- Player bar matches the design: a rounded see-through card with the video blurred behind it, the design's play, speed, skip and full-screen controls, and a title bar whose gear and Back match the window buttons.
- Diagnostics panel (Ctrl+I, changeable in Settings → Shortcuts): decoder, video, frame rate, display refresh, dropped frames and cache on a solid panel, with a Copy button.
- Aether now reads the refresh rate of the monitor the window is on (and follows it to another monitor or a changed display mode) and tells the player, so 120 fps clips no longer lose every other frame with Smooth motion on.
- Toast notifications: a small card above the controls (or near the bottom when they hide) with an optional dot and action and a Dismiss button. One shows at a time, failures go first, a toast with an action is never replaced, repeats show once, and the timer (4 s, or 8 s with an action) pauses while the pointer or keyboard focus is on it or a menu or dialog is open. Nothing shows them yet.
- Settings for VLC-style options: deinterlacing (with method), aspect ratio, snapshot folder, format and file name, network buffer size, HTTP proxy, RTSP transport, repair of damaged AVI files and skipping H.264 deblocking. Audio keeps pitch when you change speed, and surround sound can be mixed down to stereo. The screen stays awake while a file plays (switch off in Settings → Playback). The buffer size, AVI repair and deblocking apply after an engine restart; the rest apply straight away.
- Right-click menu on the player: Open file, Open URL, Recent files, Speed, Always on top, Diagnostics, Settings and Clips, with each shortcut shown as you have set it. Audio, Subtitles, Video, Copy frame, Save frame, Picture in picture and Stream appear greyed out until they are built.
- Ctrl+, opens Settings (changeable in Settings → Shortcuts). It was the one key the shortcut list used to refuse; only Ctrl with comma or full stop is now allowed, and the frame-step keys ignore Ctrl.
- Toasts now appear for a hardware decoder falling back to software (with a Settings button), a yt-dlp update succeeding or failing, and a saved or failed clip export (Show in folder opens the folder with the clip selected). They sit above the export chip or clip drawer when it shows.
- Video in the right-click menu now opens a panel above the controls with zoom (and Reset), rotate 0°/90°/180°/270°, aspect ratio and brightness, contrast, gamma and saturation sliders with Reset colours. The choices last for the open file and come back after an engine restart; a new file starts from Settings → Video again.
- Speed button: now a plain text button that sits like the icons beside it, and lights up when the speed is not 1×. The bar's times use the app's normal font instead of a typewriter one, with the total length muted.
- Title bar: the settings gear and Back now match the weight of minimise, maximise and close, and stay flush with them at any display scale.
- Title bar: the file name shows as plain muted text after "Aether" instead of a grey pill.
- Slim controls while a video plays: the bar shrinks to one row (Open, Play, the times around the seek bar, Mute and Full screen) and grows back into the full controls when you point at it, tab to it, pause, drag the seek bar or open a menu. Two seconds after you leave it, it shrinks again, and the controls then fade as before. The export chip and toasts move with it.
- Motion: the bar, toasts and the clip drawer now move in and out instead of popping. Settings → Advanced → Motion style (preview) switches between Quick (short, smooth, Windows-like) and Springy (a light bounce), so you can compare them; with Windows animations turned off everything is instant.
- The control bar's buttons are a little bigger, with slightly larger icons and crisper corners, and the full controls are calmer: Open and Speed on the left, back 5 s, Play and forward 5 s in the middle, and Mute, a ⋯ menu and Full screen on the right. The ⋯ menu holds frame back and forward, Loop and Always on top (their keys still work).
- Fixed: the app closed straight away on launch, because the Video panel's zoom slider reported a change while it was still loading.
- Copy frame (Ctrl+C) puts the current picture on the clipboard and Save frame (Ctrl+Shift+S) saves it to the snapshot folder from Settings → Video (Pictures\Aether when none is set), both with captions and without the on-screen display. A toast confirms, Save frame offers Show in folder, and a failure shows an amber toast. Both are in the right-click menu and can be changed in Settings → Shortcuts; Ctrl+C still copies text in a text box.
- Audio and Subtitles in the right-click menu now open a tracks panel above the controls. Each column lists the file's tracks as language · title · channels (5.1, 2.0), with SDH or Forced on subtitles, the current one checked and long names trimmed with the full name on hover. Subtitles have Off and Add subtitle file…, and both columns have a delay control that moves by 0.1 s.
- Restarting the engine from Settings now keeps your choice of audio track, subtitle track (or Off), the audio and subtitle delay and any subtitle files you added, instead of going back to the player's own picks.
- Settings → Subtitles has a colour (white, yellow, green or cyan) and a position (bottom, raised or top) for captions, applied straight away. Subtitle files with their own styling keep their look.
- Standalone Kestrel developer streaming host (`tools/host.ps1`, `tools/dev-host`).
- A slim navigation rail on the left edge: rest the pointer on the edge (a thin strip lights up) or click it, and Player, Clips, Stream and Settings slide in; it closes when you move away, press Esc or the controls hide. Every panel now moves the same way: the clip list, Settings and its sections, Diagnostics and the Open URL box fade and slide in and out instead of popping, button highlights fade, and the Springy motion style also gives buttons a small squash when pressed.

### Fixed
- Rotating the video from the Video panel no longer squeezes the picture into a narrow strip with black bands; the picture now fills its window area at the rotated shape.
- Changing hardware decoding, audio passthrough or custom mpv options now applies with Restart engine straight away, without relaunching the app; the video carries on where it was.
- A video opened at launch (command line, Open with, Play with Aether) opens again and shows its picture instead of playing sound only.
- Fixed Settings content layout squishing and extra dead space by ensuring content column expands up to max-width without false centering.
- Fixed volume slider hit-testing blocked by "Mute (M)" tooltip when hovering over the volume button.
- Fixed software decoding (`hwdec=no`) frame pacing by disabling video latency hacks on CPU decode and boosting render loop thread priority.
- Restored "Report an issue" link on the About settings page.
