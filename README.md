<p align="center">
  <picture>
    <source media="(prefers-color-scheme: light)" srcset="docs/assets/aether-icon-light.png" />
    <img src="docs/assets/aether-icon.png" width="112" alt="Aether logo" />
  </picture>
</p>

<h1 align="center">Aether</h1>

<p align="center">
  <b>The video player Windows 11 deserved.</b><br />
  Instant, frame-accurate playback. Lossless clips in a blink. Scan a code and keep watching on your phone.
</p>

<p align="center">
  <a href="https://github.com/rvyyv-n/aether/releases/latest"><img src="https://img.shields.io/github/v/release/rvyyv-n/aether?include_prereleases&label=download&style=flat-square&color=6b5bff" alt="Latest release" /></a>
  <a href="https://github.com/rvyyv-n/aether/actions/workflows/build-aot.yml"><img src="https://img.shields.io/github/actions/workflow/status/rvyyv-n/aether/build-aot.yml?branch=master&style=flat-square&label=build" alt="Build status" /></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078d4?style=flat-square&logo=windows11&logoColor=white" alt="Windows 10 and 11" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0-lightgrey?style=flat-square" alt="GPL-3.0 licence" /></a>
</p>

<p align="center">
  <a href="https://github.com/rvyyv-n/aether/releases/latest"><b>Download</b></a> &nbsp;·&nbsp;
  <a href="#features">Features</a> &nbsp;·&nbsp;
  <a href="#keyboard-shortcuts">Shortcuts</a> &nbsp;·&nbsp;
  <a href="docs/CHANGELOG.md">Changelog</a> &nbsp;·&nbsp;
  <a href="docs/ROADMAP.md">Roadmap</a>
</p>

<!-- SCREENSHOT: hero. Wide shot of a video playing with the bar showing, Mica window, ~1600px wide.
<p align="center">
  <img src="docs/screenshots/hero.png" width="900" alt="Aether playing a video on Windows 11" />
</p>
-->

<br />

## Why Aether

Most players are either heavy and slow to respond, or fast but dated. Aether is a small, native Windows 11 player with no web layer in between. Seeking, scrubbing and frame stepping feel immediate, even on 144–360 Hz game recordings.

### Play anything, instantly

- **Plays almost every container and codec** through an embedded [libmpv](https://mpv.io) engine: MKV, MP4, WebM, AVI, TS, ProRes, AV1, HEVC, H.264, VP9 and more.
- **Hardware-accelerated decode** via Direct3D 11 (`d3d11va`) with graceful fallback to software decode.
- **Zero-drop presentation loop**: frames flow from the decoder straight to a DirectX 11 swap chain; no managed copies in the presentation path.
- **High-refresh sync**: reads the monitor's native refresh rate (60, 120, 144, 240, 360 Hz) and paces frames smoothly with no micro-judder.
- **Web links**: paste a YouTube or other web video link (<kbd>Ctrl</kbd>+<kbd>V</kbd> or <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd>) to stream directly at full resolution with chapter marks, subtitles, quality selection and resume. Ships with [yt-dlp](https://github.com/yt-dlp/yt-dlp) and updates it automatically.

### Lossless clips & frame-accurate cuts

- Mark In (<kbd>I</kbd>) and Out (<kbd>O</kbd>) directly on the seek bar.
- **Fast mode**: saves a lossless clip in under 300 ms with stream copy (`-c copy`) via FFmpeg, with keyframe start indicators showing exact boundary landings.
- **Precise mode**: frame-accurate re-encode using your graphics card's hardware encoder (NVIDIA NVENC, Intel Quick Sync, AMD AMF, or CPU x264), with optional target file size limits (10 MB, 25 MB, 50 MB, or custom) and progress tracking.
- Drag finished clips directly from the drawer into Discord, File Explorer, or a web browser.
- Instant frame copy (<kbd>Ctrl</kbd>+<kbd>C</kbd>) and snapshot save (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd>) at native source resolution.

### Watch on your phone

- Press <kbd>S</kbd> to open the phone streaming card.
- Aether starts a lightweight local streaming server and displays a QR code.
- Scan it with your phone's camera to watch the video on your phone over Wi-Fi with instant seeking and no transcoding delay.
- The server stops as soon as you close the card, file, or app.

### Picture in picture

- Press <kbd>P</kbd> or right-click to float the video in a compact, always-on-top window.
- The playback engine and swap chain transition smoothly with no restart or frame stutter.

### Feels like Windows

Mica backdrop, borderless window, light and dark themes, and controls that fade away while you watch. Every shortcut can be changed, and settings are laid out like the Windows Settings app.

<!-- SCREENSHOT: Settings page (Shortcuts or Video), light and dark side by side if possible.
<img src="docs/screenshots/settings.png" width="820" alt="Aether's settings" />
-->

<br />

## Install

**[Download `AetherPlayer-win-Setup.exe`](https://github.com/rvyyv-n/aether/releases/latest)** and run it.

- Installs for your account only. No admin rights, certificates or Developer Mode.
- Adds Start menu and desktop shortcuts, **Play with Aether** in the right-click menu, and a clean uninstall in Settings › Installed apps.
- Updates itself: it checks once a day and installs when you close Aether. Switch this off in Settings › About.

**Prefer portable?** Grab `Aether-portable.zip` from the same release, unzip it anywhere (a USB stick works) and run `run-portable.cmd`. The portable copy doesn't update itself.

Requires Windows 10 version 1809 or newer, 64-bit.

<br />

## Keyboard shortcuts

| Key | Action | | Key | Action |
|:---|:---|---|:---|:---|
| <kbd>Space</kbd> | Play / pause | | <kbd>I</kbd> / <kbd>O</kbd> | Set In / Out |
| <kbd>,</kbd> / <kbd>.</kbd> | Back / forward one frame | | <kbd>Ctrl</kbd>+<kbd>E</kbd> | Export clip |
| <kbd>←</kbd> / <kbd>→</kbd> | Skip 5 seconds | | <kbd>Ctrl</kbd>+<kbd>C</kbd> | Copy frame |
| <kbd>Shift</kbd>+<kbd>←</kbd> / <kbd>→</kbd> | Skip 1 second | | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> | Save frame |
| <kbd>Ctrl</kbd>+<kbd>←</kbd> / <kbd>→</kbd> | Skip 30 seconds | | <kbd>S</kbd> | Stream to phone |
| <kbd>PgUp</kbd> / <kbd>PgDn</kbd> | Previous / next chapter | | <kbd>P</kbd> | Picture in picture |
| <kbd>M</kbd> | Mute | | <kbd>T</kbd> | Always on top |
| <kbd>F</kbd> / <kbd>F11</kbd> | Full screen | | <kbd>L</kbd> | Loop |
| <kbd>Ctrl</kbd>+<kbd>O</kbd> | Open file | | <kbd>B</kbd> | Side rail |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd> | Open link | | <kbd>Ctrl</kbd>+<kbd>I</kbd> | Diagnostics |
| <kbd>Ctrl</kbd>+<kbd>V</kbd> | Play a copied link (home screen) | | <kbd>Ctrl</kbd>+<kbd>,</kbd> | Settings |

Change any of them in **Settings › Shortcuts**; Aether warns you if two actions clash.

<br />

## Under the hood

Aether is written in C# on .NET with WinUI 3 and plays video through [libmpv](https://mpv.io). Decoded frames go from the graphics card straight into a Direct3D 11 swap chain with no copies in between, and the playback loop allocates no memory while a video plays, so there are no garbage-collection stutters. Clips are cut with FFmpeg, and phone streaming runs on a small built-in web server.

| Project | What it does |
|:---|:---|
| `Aether.App` | The window, controls, panels and settings (WinUI 3) |
| `Aether.Core.Playback` | The playback engine, settings and shortcuts, with no UI |
| `Aether.Core.Interop` | Native bindings for libmpv, Direct3D 11 and Win32 |
| `Aether.PvP.Review` | Frame stepping and clip export |
| `Aether.Server.LanHost` | The phone streaming server |

Design decisions are recorded in [`docs/DECISIONS.md`](docs/DECISIONS.md).

<details>
<summary><b>Building from source</b></summary>

<br />

You need Windows 10 1809+ with the [.NET 10 and .NET 9 SDKs](https://dotnet.microsoft.com/download) and the [Windows App SDK 1.6+](https://learn.microsoft.com/windows/apps/windows-app-sdk/).

```powershell
# Build and launch (a few seconds after the first build)
.\run.ps1

# Open a specific video
.\run.ps1 "path\to\video.mp4"

# Launch the last build without rebuilding
.\run.ps1 -NoBuild

# Serve a video to a browser at http://localhost:8443/watch
.\run.ps1 -Host "path\to\video.mp4"
```

Build and test the way CI does:

```powershell
dotnet build AetherPlayer.sln -c Debug -p:Platform=x64 -warnaserror
dotnet test  AetherPlayer.sln -c Debug -p:Platform=x64 --no-build
```

`.\tools\package-standalone.ps1` builds the portable folder and zip into `dist/`. Tagged releases are built by GitHub Actions, which also makes the installer and update packages.

</details>

<br />

## Part of a suite

Aether is one of a family of native Windows 11 apps that share the same design language and the same rule: fast, small, and no web layer.

## License

Aether is by [rvyyv-n](https://github.com/rvyyv-n) and licensed under the **GNU General Public License v3.0 only**. See [LICENSE](LICENSE).

- [NOTICE.md](NOTICE.md): attribution terms. The icon, logo and brand artwork are not covered by the GPL and remain all rights reserved.
- [TRADEMARKS.md](TRADEMARKS.md): what forks may do with the name and logo.
- [THIRD_PARTY.md](THIRD_PARTY.md): licences of bundled components (libmpv and FFmpeg are GPL builds) and the written source offer.
