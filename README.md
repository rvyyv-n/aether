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

It's made for people who review footage, cut clips, and want a player that stays out of the way.

<table>
  <tr>
    <td width="33%" valign="top">
      <h3>🎯 Look closely</h3>
      Step one frame at a time in either direction, scrub smoothly, and jump between chapters.
    </td>
    <td width="33%" valign="top">
      <h3>✂️ Cut fast</h3>
      Mark In and Out, then export. Fast cuts take a fraction of a second; Precise cuts land on the exact frame.
    </td>
    <td width="33%" valign="top">
      <h3>📱 Watch anywhere</h3>
      Press <kbd>S</kbd>, switch streaming on and scan the code to carry on watching on your phone.
    </td>
  </tr>
</table>

<br />

## Features

### Playback that keeps up

Hardware decoding straight to the screen, paced to your monitor's real refresh rate, so 120 and 144 fps clips play every frame. Aether remembers where you left off in every file and link.

<!-- SCREENSHOT: slim bar over a playing video, or the Diagnostics panel (Ctrl+I) showing frame rate and refresh.
<img src="docs/screenshots/playback.png" width="820" alt="Aether's slim controls over a playing video" />
-->

### Clips without the wait

Press <kbd>I</kbd> and <kbd>O</kbd>, then <kbd>Ctrl</kbd>+<kbd>E</kbd>. **Fast** copies the stream as-is with no quality loss. **Precise** re-encodes on your graphics card (NVIDIA, Intel or AMD, with a software fallback) so the clip starts and ends on the exact frame, and can fit a size limit like 10 or 25 MB for Discord.

<!-- SCREENSHOT: clip drawer open, showing Fast/Precise, size limit and a clip row exporting.
<img src="docs/screenshots/clips.png" width="820" alt="The clip drawer with Fast and Precise export" />
-->

### Your phone as a second screen

Press <kbd>S</kbd> (or pick Stream in the right-click menu), switch streaming on and scan the QR code. The open file plays in your phone's browser over your home network. Nothing leaves your network, and it stops when you switch it off, close the file or quit.

<!-- SCREENSHOT: the streaming card with QR code, ideally beside a phone photo playing the same video.
<img src="docs/screenshots/stream.png" width="820" alt="The phone streaming card with its QR code" />
-->

### A queue that follows you

Open a video and the rest of its folder lines up behind it. Drop several videos or a whole folder and they play in turn. Press <kbd>Q</kbd> for the queue: drag to reorder, filter by name, shuffle or repeat. <kbd>N</kbd> and <kbd>Shift</kbd>+<kbd>N</kbd> (or your keyboard's media keys) move through it, and your audio language, subtitles, zoom and rotation carry over to the next video.

### Sound you can shape

A 10-band equalizer with presets like Bass boost, Vocal and Cinema. Night mode softens explosions and lifts quiet dialogue for late-night watching. Game recordings with separate game, mic and chat tracks can play them all at once, each at its own level.

### Subtitles that work for you

Subtitle files next to the video are found even when their names don't quite match, and the one in your language turns on by itself. Show two at once to learn a language, nudge them into sync with <kbd>[</kbd> and <kbd>]</kbd>, or press <kbd>Ctrl</kbd>+<kbd>T</kbd> for a searchable transcript and click any line to jump there.

### Links too

Paste a YouTube link (or one from 1,000+ other sites) with <kbd>Ctrl</kbd>+<kbd>V</kbd> and it plays, with a quality picker, subtitles and chapter marks. Turn on SponsorBlock and sponsor segments show as coloured strips on the seek bar and are skipped as they play, with a Back button if you wanted to see one after all. Live streams get a LIVE badge: scrub back through what has loaded, and click LIVE to catch up. The link helper (yt-dlp) comes built in and keeps itself up to date.

### Make it look right

Grades, sharpen, grain and denoise in the Video panel, with your own `.cube` looks if you have them. Zoom, rotate and adjust colour per file. Pop the video into a small always-on-top window with <kbd>P</kbd>.

<!-- SCREENSHOT: Video panel open with a Grade applied.
<img src="docs/screenshots/look.png" width="820" alt="The Video panel with a colour grade applied" />
-->

### Feels like Windows

Mica backdrop, borderless window, and controls that fade away while you watch. Light and dark themes are both complete, down to the frosted menus and pop-ups, and you can pick an accent colour or keep it black and white. Every shortcut can be changed, and settings are laid out like the Windows Settings app.

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
| <kbd>N</kbd> / <kbd>Shift</kbd>+<kbd>N</kbd> | Next / previous video | | <kbd>Q</kbd> | Queue |
| <kbd>[</kbd> / <kbd>]</kbd> | Subtitles 50 ms earlier / later | | <kbd>Ctrl</kbd>+<kbd>T</kbd> | Transcript |
| <kbd>Shift</kbd>+<kbd>[</kbd> | Reset subtitle timing | | | |

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
.\tools\fetch-native-deps.ps1   # libmpv and ANGLE, once
dotnet build AetherPlayer.sln -c Release -p:Platform=x64 -warnaserror
dotnet test  AetherPlayer.sln -c Release -p:Platform=x64 --no-build
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
