# Aether Media Player

**Aether** is an ultra-low-latency, minimalist Windows 11 media player engineered for high-framerate video playback (144HzÃ¢â‚¬â€œ360Hz), sub-millisecond competitive clip analysis, lossless stream-copy trimming, and zero-friction LAN streaming.

---

## Key Highlights

- **DirectX Flip-Model Presentation:** Direct3D 11 (`ID3D11Device5`) flip-discard swap chain hosted in WinUI 3 `SwapChainPanel` (`ISwapChainPanelNative`). Minimum queue depth with waitable object latency sync.
- **Zero Airspace Issues:** Windows 11 Mica backdrop, auto-hiding OSD, and HUD overlays compose directly over video frames within the native XAML tree.
- **PvP Review Toolkit:**
  - Frame-accurate bidirectional stepping (`,` and `.`).
  - Sub-millisecond & Minecraft Tick HUD overlay (`GameTick = floor(TimeInSeconds * 20)`).
  - Mark IN (`I`) / Mark OUT (`O`) visual bounding indicators.
- **Instant Lossless Trimming:** Sub-300 ms FFmpeg `-c copy` stream-copy export preserving native 240/360 fps streams with direct drag-and-drop export.
- **Embedded LAN Streamer:** In-memory Kestrel HTTP 206 server with QR code pairing for instant playback on mobile devices without transcoding.
- **Modern .NET & Native AOT:** Built with C# 13, .NET 9/10, Windows App SDK 1.6+, and source-generated `[LibraryImport]` P/Invoke bindings.

---

## Architecture & Modularity

Aether is built with clear modularity across its core components:
- **Aether.Core.Interop**: C-ABI source-generated [LibraryImport] bindings for libmpv and Direct3D 11 swap chain integration.
- **Aether.App**: Windows 11 WinUI 3 application with Mica styling, custom controls, and keybindings.
- **Aether.PvP.Review**: High-FPS frame-stepping analysis, tick HUD, and lossless stream-copy export.
- **Aether.Server.LanHost**: Embedded Kestrel HTTP 206 streaming server and QR code pairing.
---

## Project Structure

```
AetherPlayer/
Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ .github/workflows/       # CI / Native AOT build workflows
Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ docs/                    # Architecture (AGENTS.md, ROADMAP.md, CHANGELOG.md, DECISIONS.md)
Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ src/
Ã¢â€â€š   Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ Aether.App/          # WinUI 3 Presentation layer & controls
Ã¢â€â€š   Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ Aether.Core.Interop/ # C-ABI LibraryImport bindings & DirectX interop
Ã¢â€â€š   Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬ Aether.PvP.Review/   # High-FPS frame analysis & lossless clipping
Ã¢â€â€š   Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ Aether.Server.LanHost/ # Embedded Kestrel HTTP 206 streaming host & QR service
Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬ tests/                   # xUnit test suites (Core, PvP, Server)
```

---

## Building and Testing

Prerequisites: .NET 9+ SDK and Windows 10/11.

```powershell
# Restore dependencies
dotnet restore AetherPlayer.sln

# Build solution
dotnet build AetherPlayer.sln

# Run test suite
dotnet test AetherPlayer.sln
```
