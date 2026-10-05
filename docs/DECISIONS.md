# Architecture Decision Records (ADRs)

## ADR-0001: P/Invoke Strategy & Native AOT
- **Context:** .NET 9 and Windows App SDK 1.6 support Native AOT. Legacy `[DllImport]` incurs runtime marshaling stubs and inhibits complete AOT trimming.
- **Decision:** All native C-ABI bindings (`mpv-2.dll`, DirectX COM interfaces) must use modern source-generated `[LibraryImport]` and `[GeneratedComInterface]`. Callbacks must use unmanaged function pointers (`delegate* unmanaged[Cdecl]`) with `[UnmanagedCallersOnly]`.
- **Status:** Accepted.

## ADR-0002: Direct3D 11 & SwapChainPanel Fast Path
- **Context:** Video rendering in WinUI 3 must avoid airspace limitations to allow native XAML Mica backdrops, auto-hiding OSD, and analytical HUD overlays to composite directly over video frames.
- **Decision:** Host Direct3D 11 flip-model swap chain (`DXGI_SWAP_EFFECT_FLIP_DISCARD`) via `ISwapChainPanelNative::SetSwapChain`. The primary rendering target is native DXGI render API if exposed by libmpv; otherwise ANGLE zero-copy EGL client buffer wrapping the swap chain back buffer.
- **Status:** Accepted.

## ADR-0003: Zero Allocations on Video Hot-Paths
- **Context:** High refresh rate playback (144Hz–360Hz) will suffer micro-stutter if garbage collection pauses occur during rendering or event handling.
- **Decision:** No heap allocations on frame rendering or high-frequency event pump loops. Struct-backed view models, preallocated parameter blocks, and `GCSettings.LatencyMode = SustainedLowLatency` during playback.
- **Status:** Accepted.

## ADR-0004: Multi-Model Agent Specialization in T3 Code
- **Context:** Complex systems require different reasoning depths and velocities across low-level interop vs. XAML UI vs. architectural refactoring.
- **Decision:** T3 Code orchestration will delegate low-level interop (`Aether.Core.Interop`) to Claude Opus 5.5, high-velocity UX and features (`Aether.App`, `Aether.PvP.Review`, `Aether.Server.LanHost`) to Claude Sonnet 5.5, and cross-repo context, header ingestion, scaffolding, and test generation to Gemini.
- **Status:** Accepted.
