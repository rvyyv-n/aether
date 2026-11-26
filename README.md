<p align="center">
  <picture>
    <source media="(prefers-color-scheme: light)" srcset="docs/assets/aether-icon-light.png" />
    <img src="docs/assets/aether-icon.png" width="108" alt="Aether logo" />
  </picture>
</p>

<h1 align="center">Aether</h1>

<p align="center">
  <strong>The video player Windows 11 deserved.</strong><br />
  Instant, frame-accurate playback &middot; Lossless &amp; GPU-encoded clips &middot; Scan a QR code to keep watching on your phone.
</p>

<p align="center">
  <a href="https://github.com/rvyyv-n/aether/releases/latest"><img src="https://img.shields.io/github/v/release/rvyyv-n/aether?label=release&style=flat-square&color=6b5bff" alt="Latest release" /></a>
  <a href="https://github.com/rvyyv-n/aether/actions/workflows/build-aot.yml"><img src="https://img.shields.io/github/actions/workflow/status/rvyyv-n/aether/build-aot.yml?branch=master&style=flat-square&label=build" alt="Build status" /></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078d4?style=flat-square" alt="Windows 10 and 11" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-GPL--3.0-lightgrey?style=flat-square" alt="GPL-3.0 license" /></a>
</p>

<p align="center">
  <a href="#install"><b>Download</b></a> &nbsp;&middot;&nbsp;
  <a href="#highlights">Highlights</a> &nbsp;&middot;&nbsp;
  <a href="#features">Features</a> &nbsp;&middot;&nbsp;
  <a href="#keyboard-shortcuts">Shortcuts</a> &nbsp;&middot;&nbsp;
  <a href="#under-the-hood">Architecture</a> &nbsp;&middot;&nbsp;
  <a href="docs/CHANGELOG.md">Changelog</a> &nbsp;&middot;&nbsp;
  <a href="docs/ROADMAP.md">Roadmap</a>
</p>

---

## Why Aether

Most desktop video players make you pick between bloated web wrappers and fast tools that feel twenty years old. **Aether** is a native Windows 11 player built directly on WinUI 3, Direct3D 11, and `libmpv`—no Electron, no browser layer, and zero per-frame managed allocations while playing.

Scrubbing, stepping frame by frame, cutting clips, and jumping through 144–360 Hz gameplay recordings feel immediate, while the chrome gets out of your way the moment you press play.

## Highlights

<table>
  <tr>
    <td width="33%" valign="top">
      <strong>Frame-accurate playback</strong><br />
      Hardware-decoded video paced to your monitor's real refresh rate, with hover seek-bar thumbnail previews, chapter quick-jump, and global Windows media keys.
    </td>
    <td width="33%" valign="top">
      <strong>Instant &amp; precise clips</strong><br />
      Mark <kbd>I</kbd> / <kbd>O</kbd> and export with <kbd>Ctrl</kbd>+<kbd>E</kbd>. Stream-copy in a fraction of a second, or GPU-encode frame-exact clips sized for Discord.
    </td>
    <td width="33%" valign="top">
      <strong>Phone streaming</strong><br />
      Press <kbd>S</kbd>, toggle streaming on, and scan the QR code to continue watching in your phone's browser with touch gestures—strictly over your local network.
    </td>
  </tr>
  <tr>
    <td width="33%" valign="top">
      <strong>Glass, OLED &amp; Native looks</strong><br />
      Choose Aether's frosted glass, pure black <code>#000000</code> OLED Dark, or Windows 11 Native Desktop Acrylic—with custom accent colours or crisp monochrome.
    </td>
    <td width="33%" valign="top">
      <strong>Audio &amp; subtitle studio</strong><br />
      10-band EQ, Night mode, multi-track game/mic/chat mixing, live Windows audio device switching, dual subtitles, and a searchable <kbd>Ctrl</kbd>+<kbd>T</kbd> transcript.
    </td>
    <td width="33%" valign="top">
      <strong>Streams &amp; smart queues</strong><br />
      Paste links from 1,000+ sites via built-in <code>yt-dlp</code> with SponsorBlock and live-stream DVR, or drop folders into a reorderable <kbd>Q</kbd> queue.
    </td>
  </tr>
</table>

## Features

### Playback, previews & navigation
- **High-refresh pacing & resume:** Direct hardware decoding straight to a Direct3D 11 swap chain, synced to fractional and high-refresh monitors, with automatic per-file and per-link resume.
- **Seek-bar hover previews:** Hover or drag across the timeline for thumbnail previews on local videos (lazy background decoder with LRU cache) and YouTube storyboards, automatically paused on live streams.
- **Chapter quick-jump:** Press <kbd>C</kbd>, open the right-click or `⋯` menu, or enable the Chapters bar button to jump through timestamped chapters.
- **Home Recents shelf:** Resume recent local files and links from a minimalist 3-card 16:9 thumbnail grid with duration badges, progress hairlines, and quick hover actions.
- **Global Windows media controls:** Hardware media keys work even when Aether is in the background, with live artwork, title, and seekable timeline in Windows Quick Settings and on the lock screen.

### Clips without the wait
- Press <kbd>I</kbd> and <kbd>O</kbd> to mark a range, then <kbd>Ctrl</kbd>+<kbd>E</kbd> (or open the Clips drawer) to export.
- **Fast mode** copies the compressed stream losslessly in a blink and previews the real keyframe cut start on the seek bar.
- **Precise mode** re-encodes on your GPU (NVIDIA, Intel, or AMD, with automatic CPU fallback) for frame-exact cuts, optional target size limits (`10` / `25` / `50 MB` or custom), and optional baked-in Video Looks.
- Drag finished clips straight out of the drawer into Discord, Explorer, or an editor.

### Your phone as a second screen
- Press <kbd>S</kbd> (or choose **Stream** from the right-click menu), switch streaming on, and scan the QR code.
- Opens a full-bleed mobile player (`/watch`) with auto-hiding controls, horizontal seek and vertical volume touch gestures, speed/mute/fullscreen controls, lock-screen media controls, and a live connection quality indicator.
- Everything stays on your local Wi-Fi—nothing touches the cloud, and the server stops automatically when you switch it off, close the file, or quit.

### Sound, subtitles & transcripts
- **Audio shaping:** 10-band equalizer with presets (Flat, Bass boost, Vocal, Treble boost, Rock, Electronic, Cinema), Night mode to lift quiet dialogue, and a multi-track mixer for multi-channel recordings (game, mic, chat, music).
- **Live output switching:** Pick any active Windows audio endpoint inside the Audio & Subtitles flyout or **Settings › Audio** without changing Windows' system default.
- **Smart subtitles:** Auto-detects nearby subtitle files even with fuzzy names, supports **two simultaneous subtitles** (bottom + top) for language learning, nudges sync by 50 ms with <kbd>[</kbd> / <kbd>]</kbd>, and ships four readable fonts (Inter, Lexend, Atkinson Hyperlegible Next, Source Sans 3).
- **Searchable transcript:** Press <kbd>Ctrl</kbd>+<kbd>T</kbd> to search every subtitle line and click any entry to jump straight to that moment.

### Web links, SponsorBlock & queues
- **1,000+ sites built in:** Paste a link with <kbd>Ctrl</kbd>+<kbd>V</kbd> to stream with instant quality switching, subtitles, chapters, and automatic `yt-dlp` updates.
- **SponsorBlock & live streams:** Optional YouTube SponsorBlock strips on the seek bar with auto-skip (and an undo toast) or manual skip; live streams get a `LIVE` badge with back-buffer scrubbing and one-click catch-up.
- **Smart play queue:** Opening a file queues the rest of its folder; dropping files or folders plays them in order. Press <kbd>Q</kbd> to filter, drag-reorder, shuffle, or repeat while carrying over audio/subtitle language, zoom, and rotation.

### Looks, themes & Windows 11 craft
- **Live Video Looks:** Built-in colour grades (Warm, Cool, Vivid, Teal & Orange, Faded Film, Black & White), custom `.cube` LUTs, sharpen, grain, denoise, per-file zoom/rotation/aspect ratio, and always-on-top Picture in Picture (<kbd>P</kbd>).
- **Appearance & Look mode:** Complete **Auto**, **Light**, **Dark**, and pure-black **OLED** themes, paired with either **Aether** frosted glass (GPU-blurred cards, menus, and drawers) or **Native** Windows 11 Desktop Acrylic with `8 px` system corners—plus accent presets or custom hex colours.
- **Make it yours:** Show, hide, and reorder optional player-bar controls in **Settings › Playback**, reveal the floating sidebar via the proximity left-edge handle or <kbd>B</kbd>, and rebind every keyboard shortcut with live conflict checks.

---

## Install

Requires **Windows 10 (1809+)** or **Windows 11**, 64-bit (`x64`).

| Option | Download | Details |
|:---|:---|:---|
| **Installer (recommended)** | [`AetherPlayer-win-Setup.exe`](https://github.com/rvyyv-n/aether/releases/latest) | Per-user install with no admin rights, certificates, or Developer Mode. Adds Start menu & desktop shortcuts, **Play with Aether** context menu entry, clean uninstall, and automatic background updates. |
| **Portable** | [`Aether-portable.zip`](https://github.com/rvyyv-n/aether/releases/latest) | Unzip anywhere (including a USB drive) and run `Aether.App.exe` or `run-portable.cmd`. Self-contained; does not auto-update. |

> [!NOTE]
> **Windows SmartScreen:** Aether builds are not code-signed yet, so Windows SmartScreen may warn on first run. Click **More info**, then **Run anyway** to continue.

---

## Keyboard shortcuts

Every shortcut can be customized in **Settings › Shortcuts**.

| Key | Action | | Key | Action |
|:---|:---|---|:---|:---|
| <kbd>Space</kbd> | Play / pause | | <kbd>I</kbd> / <kbd>O</kbd> | Set In / Out clip markers |
| <kbd>,</kbd> / <kbd>.</kbd> | Step back / forward one frame | | <kbd>Ctrl</kbd>+<kbd>E</kbd> | Export marked clip |
| <kbd>←</kbd> / <kbd>→</kbd> | Skip 5 seconds | | <kbd>Ctrl</kbd>+<kbd>C</kbd> | Copy current frame |
| <kbd>Shift</kbd>+<kbd>←</kbd> / <kbd>→</kbd> | Skip 1 second | | <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> | Save current frame |
| <kbd>Ctrl</kbd>+<kbd>←</kbd> / <kbd>→</kbd> | Skip 30 seconds | | <kbd>S</kbd> | Stream to phone |
| <kbd>C</kbd> | Chapter list | | <kbd>P</kbd> | Picture in picture |
| <kbd>PgUp</kbd> / <kbd>PgDn</kbd> | Previous / next chapter | | <kbd>T</kbd> | Always on top |
| <kbd>M</kbd> | Mute | | <kbd>L</kbd> | Loop |
| <kbd>F</kbd> / <kbd>F11</kbd> | Full screen | | <kbd>Q</kbd> | Queue drawer |
| <kbd>N</kbd> / <kbd>Shift</kbd>+<kbd>N</kbd> | Next / previous video | | <kbd>Ctrl</kbd>+<kbd>T</kbd> | Searchable transcript |
| <kbd>[</kbd> / <kbd>]</kbd> | Subtitles 50 ms earlier / later | | <kbd>B</kbd> | Side rail |
| <kbd>Shift</kbd>+<kbd>[</kbd> | Reset subtitle timing | | <kbd>Ctrl</kbd>+<kbd>I</kbd> | Diagnostics overlay |
| <kbd>Ctrl</kbd>+<kbd>O</kbd> / <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd> | Open file / Open link | | <kbd>Ctrl</kbd>+<kbd>,</kbd> | Settings |
| <kbd>Ctrl</kbd>+<kbd>V</kbd> | Play copied link (Home) | | | |

---

## Under the hood

Aether is written in C# on **.NET 10** and **WinUI 3** (`x64`), combining [libmpv](https://mpv.io) with Direct3D 11 / ANGLE GPU interop and source-generated `[LibraryImport]` P/Invoke bindings:

- **Direct-to-backbuffer fast path:** Bypasses offscreen frame copies whenever no frosted overlay is visible, keeping normal playback lean and smooth.
- **Batched GPU backdrop blur:** Multi-card Gaussian blur runs on the render thread in ~0.08 ms at 1080p, synced to card bounds, opacity fades, and corner radii.
- **Zero-allocation render loop & live GPU metrics:** `MpvRenderLoop` allocates nothing per frame and reports live GPU frame time (ms) via Direct3D 11 timestamp queries in Diagnostics (<kbd>Ctrl</kbd>+<kbd>I</kbd>).
- **Deferred UI loading:** Non-initial drawers, flyouts, overlays, and settings styles load on first use (~347 ms warm ReadyToRun startup).

| Project | Responsibility |
|:---|:---|
| `src/Aether.App` | WinUI 3 presentation layer, Mica/Acrylic/OLED surfaces, controls, drawers, and Settings |
| `src/Aether.Core.Playback` | UI-agnostic playback session, seek coalescing, thumbnails, chapters, subtitles, and settings |
| `src/Aether.Core.Interop` | Native AOT `[LibraryImport]` bindings for libmpv, Direct3D 11, ANGLE EGL, and Win32 |
| `src/Aether.PvP.Review` | Frame stepping, zero-decode keyframe probing, and lossless / GPU FFmpeg clipping |
| `src/Aether.Server.LanHost` | Embedded Kestrel HTTP 206 LAN streaming host, QR pairing, and `/watch` mobile player |
| `tests/*` | 2,000+ xUnit unit & integration tests plus live FlaUI (UIA3) end-to-end UI automation |

Architectural decisions are documented in [`docs/DECISIONS.md`](docs/DECISIONS.md).

<details>
<summary><b>Building &amp; testing from source</b></summary>

<br />

Requires Windows 10 1809+ (`x64`) and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
# Download libmpv and ANGLE native libraries (once)
.\tools\fetch-native-deps.ps1

# Incremental Debug x64 build & launch
.\run.ps1
.\run.ps1 "path\to\video.mp4"       # open a specific file
.\run.ps1 -NoBuild                  # relaunch without rebuilding
.\run.ps1 -Host "path\to\video.mp4" # preview /watch at http://localhost:8443/watch
```

Run the full CI verification suite locally:

```powershell
dotnet restore AetherPlayer.sln -p:Platform=x64
dotnet build AetherPlayer.sln -c Release -p:Platform=x64 --no-restore --no-incremental -warnaserror
dotnet test AetherPlayer.sln -c Release -p:Platform=x64 --no-build --filter "Category!=UI"
.\tools\run-ui-tests.ps1            # live FlaUI UIA3 automation suite
```

Package a local standalone portable build into `dist/`:

```powershell
.\tools\package-standalone.ps1
```

Tagged `v*` pushes run `.github/workflows/release.yml` to build `AetherPlayer-win-Setup.exe`, `Aether-portable.zip`, and Velopack update packages into a draft GitHub release.

</details>

---

## License

Aether is by [rvyyv-n](https://github.com/rvyyv-n) and licensed under the **GNU General Public License v3.0 only**. See [LICENSE](LICENSE).

- [NOTICE.md](NOTICE.md) — attribution terms (the icon, logo, and brand artwork are not covered by the GPL and remain all rights reserved).
- [TRADEMARKS.md](TRADEMARKS.md) — guidelines for forks using the Aether name and logo.
- [THIRD_PARTY.md](THIRD_PARTY.md) — licenses for bundled components (`libmpv`, `FFmpeg`, `yt-dlp`, fonts) and the written source offer.
