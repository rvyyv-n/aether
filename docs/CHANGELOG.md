# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Added
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
- Standalone Kestrel developer streaming host (`tools/host.ps1`, `tools/dev-host`).

### Fixed
- Changing hardware decoding, audio passthrough or custom mpv options now applies with Restart engine straight away, without relaunching the app; the video carries on where it was.
- A video opened at launch (command line, Open with, Play with Aether) opens again and shows its picture instead of playing sound only.
- Fixed Settings content layout squishing and extra dead space by ensuring content column expands up to max-width without false centering.
- Fixed volume slider hit-testing blocked by "Mute (M)" tooltip when hovering over the volume button.
- Fixed software decoding (`hwdec=no`) frame pacing by disabling video latency hacks on CPU decode and boosting render loop thread priority.
- Restored "Report an issue" link on the About settings page.
