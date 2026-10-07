# Agent rules

## Stack and build
Native AOT player on .NET 9/10 (WinUI 3; `Aether.App` and the test projects target .NET 10, Interop and LanHost .NET 9): libmpv C API bindings, with DirectComposition / Direct3D 11 interop for the swap chain. (Local offline gate builds on x86 by default; CI builds on x64). Solution `AetherPlayer.sln` (`.slnx` alongside):
- `src/Aether.Core.Interop`: C-ABI `LibraryImport` bindings and DirectX interop.
- `src/Aether.Core.Playback`: UI-agnostic playback session, throttled dispatch, and frame stepping.
- `src/Aether.App`: WinUI 3 presentation layer and controls.
- `src/Aether.PvP.Review`: high-FPS frame analysis and lossless clipping.
- `src/Aether.Server.LanHost`: embedded Kestrel HTTP 206 streaming host and QR service.
- `tests/`: xUnit suites (`Aether.Core.Tests`, `Aether.PvP.Tests`, `Aether.Server.Tests`).

```powershell
dotnet restore AetherPlayer.sln
dotnet build AetherPlayer.sln
dotnet test AetherPlayer.sln
dotnet build AetherPlayer.sln --no-incremental -warnaserror   # warnings are errors (Directory.Build.props)
```
CI (`.github/workflows/build-aot.yml`) builds and tests in Release.

## Roles
- **Opus**: `Aether.Core.Interop`. Unsafe structs, P/Invoke signatures, DirectX COM wrappers (`ISwapChainPanelNative`), memory lifetimes (verify no unmanaged leaks), MPV event-loop thread safety, and architecture decisions. Use it for 1-3 turns per task, not for file reading or test-fix loops.
- **Opus (design)**: all design work: custom chrome, Mica, `DesignTokens`, auto-hiding OSD, drag-and-drop, startup/error states, `OverlayHud`, `PrecisionSeekBar`, `ClipDrawer`, the mobile player shell and the QR flyout (roadmap tasks 4.1-4.6, 5.1-5.3, 6.2, 6.3). Before building any of them it asks the user design questions and requests reference screenshots, then confirms the direction before writing UI code.
- **Sonnet**: non-design wiring in `Aether.App`, `Aether.Core.Playback`, `Aether.PvP.Review`, `Aether.Server.LanHost`. `MpvVideoSurface`, throttled UI dispatch, `FrameStepper`, `PlayerViewModel`, keyboard accelerators, FFmpeg command builder and export, Kestrel host. Max 8 turns per thread.
- **Gemini**: scouting and header ingestion (`mpv.h`, `render.h`), test suites and harnesses, build-error loops, CI, multi-file restructuring. Gemini doesn't auto-delegate; it names a fork candidate in its build summary.

## Rules
1. **P/Invoke**: no `[DllImport]`. Use source-generated `[LibraryImport]` with explicit marshaling (Native AOT).
2. **Frame loop**: zero allocations. libmpv callbacks don't marshal objects per tick.
3. **Threads**: `mpv_wait_event` runs on a dedicated `TaskCreationOptions.LongRunning` thread. Never block the WinUI `DispatcherQueue`.
4. **Errors**: guard unmanaged pointer operations. Check Direct3D `HRESULT`s with `Marshal.ThrowExceptionForHR` or log the failure.
5. **UI**: minimal Windows 11 design. No window borders, Mica backdrops, auto-hiding scrubbers, high-contrast type.
6. **Callbacks**: `[UnmanagedCallersOnly]` plus `delegate* unmanaged[Cdecl]` for libmpv. No managed delegates.
7. **Processes**: build arguments with `ProcessStartInfo.ArgumentList`.
8. **Decisions**: record architecture choices in `docs/DECISIONS.md`.

## Orchestration
- At a build checkpoint, fork with `t3_thread_fork` (from `latest_stable`) into a worktree (`claude/pass-<n>` or `claude/eval`). Don't chain `delegate_task` calls between models, unless the user asks for a lead thread to delegate to subagents (as in Passes 1 and 2: Opus lead, Sonnet and Gemini helpers).
  - Fork to Opus for Interop memory safety, P/Invoke signatures and COM wrappers.
  - Fork to Opus for WinUI design, Mica and UI polish (ask the user questions and request reference screenshots first).
  - Fork to Sonnet for MVVM wiring, PvP review plumbing and the Kestrel host.
- Retire a thread at 8 turns or 150k tokens: commit, then fork or start fresh.
- Fork checkpoints replace delegation loops: finish and commit the pass, then fork.
- Never leave a dev server or file watcher running at the end of a turn.
- Call `link_pull_request` for every PR you open.

## Commits
Author and committer are `rvyyv-n` only. No co-author, generated-by or assistant mentions in commits, PRs or files; the global git hooks reject them. Never `--no-verify`.
