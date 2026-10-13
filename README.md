<p align="center">
  <img src="src/Aether.App/Assets/Square150x150Logo.scale-200.png" width="128" alt="Aether logo" />
</p>

<h1 align="center">Aether</h1>

<p align="center">
  <b>The video player Windows 11 deserved.</b><br />
  Instant, frame-accurate playback with a native look, lossless clipping, and one-scan streaming to your phone.
</p>

<p align="center">
  <a href="https://github.com/rvyyv-n/aether/actions/workflows/build-aot.yml">
    <img src="https://github.com/rvyyv-n/aether/actions/workflows/build-aot.yml/badge.svg" alt="Build & Test (Native AOT)" />
  </a>
  <img src="https://img.shields.io/badge/Tests-846%20Passing-brightgreen?logo=xunit&logoColor=white" alt="846 Tests Passing" />
  <img src="https://img.shields.io/badge/Platform-Windows_11-0078D4?logo=windows11&logoColor=white" alt="Windows 11" />
  <img src="https://img.shields.io/badge/UI-WinUI_3_%7C_Mica_Alt-005FB8?logo=windows&logoColor=white" alt="WinUI 3" />
  <img src="https://img.shields.io/badge/Engine-libmpv_%2B_Direct3D_11-5B2C6F" alt="libmpv" />
  <img src="https://img.shields.io/badge/.NET-10.0_%2F_9.0-512BD4?logo=dotnet&logoColor=white" alt=".NET" />
  <img src="https://img.shields.io/badge/AOT-Native_AOT_Ready-brightgreen" alt="Native AOT" />
</p>

---

## Why Aether

Most players are either heavy and slow to respond, or fast but dated. Aether is neither. It is a small, native Windows 11 player built on libmpv and WinUI 3, with no web layer in between. Seeking, scrubbing and frame stepping feel immediate, even on 144–360 Hz game recordings.

It is made for people who review footage, cut clips, and want a player that stays out of the way:

- **Look closely:** step one frame at a time in either direction and scrub smoothly.
- **Cut fast:** mark In and Out, and export a clip in a fraction of a second without re-encoding.
- **Keep watching anywhere:** press `S`, scan the QR code, and carry on on your phone over your home network.

---

## Key Features

- ⚡ **DirectX Flip-Model Presentation:** Direct3D 11 (`ID3D11Device5`) flip-discard swap chain hosted in WinUI 3 `SwapChainPanel` via native `ISwapChainPanelNative` interop with waitable object frame pacing.
- 🎯 **Hardware-Accelerated Zero-Copy Pipeline:** Zero-copy GPU decoding (`d3d11va`) direct into swap chain back buffers via libmpv and ANGLE.
- ⏱️ **Frame-Accurate Scrubbing & Stepping:** Bidirectional frame stepping (`,` and `.`), seek coalescing, and smooth timeline scrubbing with sub-millisecond precision.
- ✂️ **Lossless Sub-Second Trimming:** Instant In/Out mark trimming (`I` / `O`) with sub-300ms FFmpeg stream-copy (`-c copy`) export, preserving native stream quality without re-encoding.
- 🪟 **Minimalist Windows 11 Shell:** Borderless window chrome, native Windows 11 Mica Alt backdrop blending behind letterboxing, aspect-ratio snapping, and auto-hiding OSD and cursor on idle.
- 📂 **Folder Playlists & Resume:** Opening a video queues the rest of its folder and plays the next one automatically. Aether remembers where you left the window, and opening a file while Aether is running reuses the open window.
- ⚙️ **Comprehensive Settings & Keybinds:** Nine dedicated preference pages (Video, Audio, Subtitles, Playback, Shortcuts, Streaming, Advanced, General, About) with live shortcut conflict detection.
- 📡 **Instant LAN Mobile Streaming:** Embedded in-memory Kestrel HTTP 206 partial-content server with instant QR-code mobile pairing—scan to continue playback on any phone or tablet on your network.
- 🏎️ **Native AOT & Zero-Allocation Hot Path:** Built on C# 13 and .NET 10/9 with source-generated `[LibraryImport]` P/Invoke bindings and zero managed allocations on the render loop.

---

## Keyboard Shortcuts

| Shortcut | Action |
|:---|:---|
| `Space` | Play / Pause toggle |
| `,` / `.` | Step one frame backward / forward |
| `Left` / `Right` | Jump backward / forward |
| `Up` / `Down` | Volume up / down |
| `M` | Mute / Unmute audio |
| `F` / `F11` | Fullscreen toggle |
| `Esc` | Exit fullscreen |
| `Ctrl + O` | Open video file |
| `Ctrl + Shift + O` *(or `Ctrl + L`)* | Open streaming URL |
| `I` / `O` | Set In / Out trim markers |
| `L` | Toggle loop playback |
| `T` | Toggle always-on-top |
| `S` | Show LAN pairing QR flyout |

*All shortcuts are fully customizable under **Settings → Shortcuts** with conflict detection.*

---

## Architecture

Aether is structured into modular, decoupled subsystems:

| Subsystem | Target | Description |
|:---|:---|:---|
| **Aether.Core.Interop** | `.NET 9` | C-ABI `[LibraryImport]` bindings for libmpv, DirectX Direct3D 11 COM interop, and Win32 window hooks |
| **Aether.Core.Playback** | `.NET 9` | UI-agnostic playback engine, seek coalescing, settings store, and event coordination |
| **Aether.App** | `.NET 10` (WinUI 3) | Presentation shell: Mica Alt backdrop, auto-hiding OSD, settings pages, single instance router |
| **Aether.PvP.Review** | `.NET 10` | High-FPS frame analysis, game tick HUD calculations, and lossless FFmpeg clipping |
| **Aether.Server.LanHost** | `.NET 9` | Embedded Kestrel HTTP 206 streaming host and QR pairing service |

---

## Project Structure

```text
aether/
├── .github/workflows/          # CI and Native AOT Release build pipelines
├── docs/                       # Architecture specs, decision logs, and roadmaps
├── src/
│   ├── Aether.App/             # WinUI 3 desktop shell, Mica themes, and settings
│   ├── Aether.Core.Interop/    # Native Win32, D3D11, and libmpv P/Invoke bindings
│   ├── Aether.Core.Playback/   # Headless playback loop, shortcuts, and settings map
│   ├── Aether.PvP.Review/      # Analysis HUD and lossless clipping engine
│   └── Aether.Server.LanHost/  # Embedded Kestrel LAN stream server
└── tests/                      # Automated test suites (846 unit tests)
    ├── Aether.Core.Tests/      # Playback, settings, shortcuts, and windowing tests
    ├── Aether.PvP.Tests/       # Frame timing, clipping, and tick analysis tests
    └── Aether.Server.Tests/    # Kestrel HTTP 206 streaming and QR tests
```

---

## Building & Running

### Prerequisites
- Windows 10/11 (version 1809+, build 17763 or newer)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) and .NET 9 SDK
- [Windows App SDK 1.6+](https://learn.microsoft.com/windows/apps/windows-app-sdk/)

### Build & Test Gate

```powershell
# Restore dependencies
dotnet restore AetherPlayer.sln -p:Platform=x64

# Build solution (warnings treated as errors)
dotnet build AetherPlayer.sln -c Debug -p:Platform=x64 --no-restore -warnaserror

# Run all 846 unit tests
dotnet test AetherPlayer.sln -c Debug -p:Platform=x64 --no-build
```

### Running the App
`Aether.App` is an MSIX-packaged application. Run with:
```powershell
dotnet run --project src/Aether.App -p:Platform=x64
```
*(Requires Windows Developer Mode enabled under **Settings → System → For developers**, or launch directly from Visual Studio via F5).*
