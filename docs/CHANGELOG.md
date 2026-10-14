# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
- Housekeeping: the window title, About diagnostics lines, proxy hint and Diagnostics wording now come from the language file; unused language entries and design tokens, dead constants, the packaged-app manifest and logo images moved to `retired/`; native libraries for every architecture are ignored by git; the README no longer presents phone streaming as working before Pass 11.
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
- Copy frame (Ctrl+C) puts the current picture on the clipboard and Save frame (Ctrl+Shift+S) saves it to the snapshot folder from Settings → Video (Pictures\Aether when none is set), both with captions and without the on-screen display. A toast confirms, Save frame offers Show in folder, and a failure shows an amber toast. Both are in the right-click menu and can be changed in Settings → Shortcuts; Ctrl+C still copies text in a text box.
- Audio and Subtitles in the right-click menu now open a tracks panel above the controls. Each column lists the file's tracks as language · title · channels (5.1, 2.0), with SDH or Forced on subtitles, the current one checked and long names trimmed with the full name on hover. Subtitles have Off and Add subtitle file…, and both columns have a delay control that moves by 0.1 s.
- Restarting the engine from Settings now keeps your choice of audio track, subtitle track (or Off), the audio and subtitle delay and any subtitle files you added, instead of going back to the player's own picks.
- Settings → Subtitles has a colour (white, yellow, green or cyan) and a position (bottom, raised or top) for captions, applied straight away. Subtitle files with their own styling keep their look.
- Standalone Kestrel developer streaming host (`tools/host.ps1`, `tools/dev-host`).

### Fixed
- Changing hardware decoding, audio passthrough or custom mpv options now applies with Restart engine straight away, without relaunching the app; the video carries on where it was.
- A video opened at launch (command line, Open with, Play with Aether) opens again and shows its picture instead of playing sound only.
- Fixed Settings content layout squishing and extra dead space by ensuring content column expands up to max-width without false centering.
- Fixed volume slider hit-testing blocked by "Mute (M)" tooltip when hovering over the volume button.
- Fixed software decoding (`hwdec=no`) frame pacing by disabling video latency hacks on CPU decode and boosting render loop thread priority.
- Restored "Report an issue" link on the About settings page.
