# Aether

<p align="center">
  <strong>Ultra-low-latency, minimalist Windows 11 media player.</strong><br />
  Engineered for fluid high-framerate playback, zero-latency scrubbing, and lossless stream clipping.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows_11-0078D4?logo=windows11&logoColor=white" alt="Windows 11" />
  <img src="https://img.shields.io/badge/UI-WinUI_3_%7C_Mica_Alt-005FB8" alt="WinUI 3" />
  <img src="https://img.shields.io/badge/Engine-libmpv_+_D3D11-5B2C6F" alt="libmpv" />
  <img src="https://img.shields.io/badge/.NET-9.0_%2F_10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET" />
  <img src="https://img.shields.io/badge/AOT-Native_AOT_Ready-brightgreen" alt="Native AOT" />
</p>

---

## Overview

**Aether** is a modern, high-performance media player designed from the ground up for Windows 11. It eliminates heavyweight player bloat and sluggish electron wrappers in favor of raw C-ABI bindings, direct Direct3D 11 swap chain presentation, and native WinUI 3 controls seamlessly blended with Windows 11 Mica Alt.

Whether analyzing fast 144Hz–360Hz game recordings or playing back standard 4K media, Aether ensures instant response, zero UI lag, and pixel-perfect presentation.

---

## Core Capabilities

- **DirectX Flip-Model Swap Chain:** Direct3D 11 (`ID3D11Device5`) flip-discard swap chain hosted in WinUI 3 `SwapChainPanel` via `ISwapChainPanelNative`. Minimum latency queue depth with waitable object frame synchronization.
- **Hardware Acceleration (`d3d11va`):** Zero-copy GPU decoding directly into the swap chain back buffer via libmpv and ANGLE.
- **Minimalist Windows 11 Shell:** Borderless window chrome, native Windows 11 Mica Alt backdrop behind letterboxing, and custom auto-hiding OSD and cursor on idle.
- **Window Edge Snapping:** Automatically resizes window boundaries to match the aspect ratio of the loaded video without letterbox gaps.
- **Precision Scrubbing & Stepping:** Frame-accurate bidirectional stepping (`,` and `.`), fast-tap queue arbitration, and smooth timeline scrubbing.
- **Lossless Stream Trimming:** Sub-300ms FFmpeg stream-copy (`-c copy`) export preserving native audio/video streams without re-encoding.
- **Embedded LAN Streaming:** In-memory Kestrel HTTP 206 server with instant QR-code mobile pairing.
- **Native AOT & Zero-Allocation Hot Path:** Built on C# 13 and .NET 9/10 with source-generated `[LibraryImport]` P/Invoke bindings.

---

## Architecture

Aether is organized into cleanly separated subsystems:

| Component | Target | Responsibility |
|:---|:---|:---|
| **Aether.Core.Interop** | `.NET 9` | C-ABI `[LibraryImport]` bindings for libmpv, DirectX Direct3D 11 COM wrappers, and native window hooks |
| **Aether.Core.Playback** | `.NET 9` | UI-agnostic playback engine, throttled event dispatch, and frame-stepping logic |
| **Aether.App** | `.NET 10` (WinUI 3) | Presentation layer: Mica Alt backdrop, auto-hiding OSD, custom chrome, and drag-and-drop listener |
| **Aether.PvP.Review** | `.NET 10` | High-FPS frame analysis, timecode calculation, and lossless FFmpeg clipping |
| **Aether.Server.LanHost** | `.NET 9` | Embedded Kestrel HTTP 206 partial-content server with session auth |

---

## Project Structure

```text
AetherPlayer/
|-- .github/workflows/          # CI and Native AOT Release build pipelines
|-- docs/                       # Architecture specs (AGENTS.md, ROADMAP.md, DECISIONS.md)
|-- src/
|   |-- Aether.App/             # WinUI 3 desktop shell, Mica themes, and controls
|   |-- Aether.Core.Interop/    # Native Win32, D3D11, and libmpv P/Invoke bindings
|   |-- Aether.Core.Playback/   # Headless playback loop and event coordinator
|   |-- Aether.PvP.Review/      # Analysis HUD and lossless clipping engine
|   `-- Aether.Server.LanHost/  # Embedded Kestrel LAN stream server
`-- tests/                      # Comprehensive xUnit suites (291 passing tests)
```

---

## Keyboard Shortcuts

| Shortcut | Action |
|:---|:---|
| `Space` | Play / Pause toggle |
| `,` / `.` | Step one frame backward / forward |
| `Left` / `Right` | Jump backward / forward (5 seconds) |
| `Up` / `Down` | Volume up / down |
| `M` | Mute / Unmute audio |
| `F` / `F11` | Fullscreen toggle |
| `Esc` | Exit fullscreen |
| `Ctrl + O` | Open video file picker |
| `I` / `O` | Set In / Out trim markers |
| `S` | Show LAN pairing QR flyout |

---

## Building & Running

### Prerequisites
- Windows 10/11 (version 1809+, build 17763 or newer)
- .NET 10 SDK and .NET 9 SDK
- Windows App SDK 1.6+

### Build & Test Gate

```powershell
# Restore packages
dotnet restore AetherPlayer.sln

# Build with zero warnings
dotnet build AetherPlayer.sln --no-incremental -warnaserror

# Run all 291 unit tests
dotnet test AetherPlayer.sln --no-restore
```

### Running the App
`Aether.App` is packaged with MSIX. Run with:
```powershell
dotnet run --project src/Aether.App -p:Platform=x64
```
*(Requires Windows Developer Mode enabled under Settings -> System -> For developers, or launch directly from Visual Studio via F5).*