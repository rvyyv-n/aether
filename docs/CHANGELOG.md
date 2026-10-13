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
- Fast developer runner script (`run.ps1`, `tools/run.ps1`) for sub-3s incremental builds and loose Developer Mode registration without full MSIX packaging.
- Standalone Kestrel developer streaming host (`tools/host.ps1`, `tools/dev-host`).

### Fixed
- Fixed Settings content layout squishing and extra dead space by ensuring content column expands up to max-width without false centering.
- Fixed volume slider hit-testing blocked by "Mute (M)" tooltip when hovering over the volume button.
- Fixed software decoding (`hwdec=no`) frame pacing by disabling video latency hacks on CPU decode and boosting render loop thread priority.
- Restored "Report an issue" link on the About settings page.
