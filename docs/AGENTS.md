# Operational Instructions for AI Agents (Antigravity / Claude)

## Role Division & Model Assignment
- **Claude Opus 5.5 / Reasoning Specialists:**
  - Strictly assigned to `Aether.Core.Interop`.
  - Authoring low-level unsafe structs, P/Invoke signatures, DirectX COM wrappers (`ISwapChainPanelNative`), and memory lifetime safety.
  - Verification of zero unmanaged memory leaks and thread safety on the MPV event loop.
- **Claude Sonnet 5.5 / Velocity Specialists:**
  - Assigned to `Aether.App`, `Aether.PvP.Review`, and `Aether.Server.LanHost`.
  - WinUI 3 XAML styling, MVVM wiring, keyboard accelerators, and FFmpeg command builder integration.
- **Gemini / Context Refactoring:**
  - Large header ingestions (`mpv.h`, `render.h`), multi-file project restructuring, build issue resolution, and test generation.

## Technical Rules of Engagement
1. **P/Invoke Standard:** DO NOT use legacy `[DllImport]`. Use modern .NET 9 source-generated `[LibraryImport]` with explicit marshaling to ensure Native AOT compatibility.
2. **Memory Allocation:** Zero allocations on frame-rendering loops. Interop callbacks from `libmpv` must not marshal objects across every tick.
3. **No Thread Starvation:** The `mpv_wait_event` listener must operate on a dedicated long-running background thread (`TaskCreationOptions.LongRunning`). Never block the WinUI `DispatcherQueue`.
4. **Error Handling:** Unmanaged pointer operations must be guarded. Direct3D calls must validate `HRESULT` returns with `Marshal.ThrowExceptionForHR` or explicit failure logging.
5. **No Visual Bloat:** Adhere strictly to the minimal Windows 11 design philosophy: zero window borders, fluid Mica backdrops, auto-hiding scrubbers, and high-contrast analytical typography.
6. **Native Callbacks:** Use `[UnmanagedCallersOnly]` + function pointers (`delegate* unmanaged[Cdecl]`) for libmpv callbacks — no managed delegates (AOT-safe, GC-safe).
7. **Process Arguments:** Build external process arguments with `ProcessStartInfo.ArgumentList`.
8. **Decisions:** Record architecture choices in `docs/DECISIONS.md`.

## T3 Code Orchestration Guidelines
- **Child Tasks / Subagents:** Use `delegate_task` to dispatch specialized subtasks:
  - Low-level Interop / DirectX / Memory Safety -> Claude Opus 5.5 (`claudeAgent` / `claude-opus-5-5`).
  - WinUI 3 UX / PvP Review / Kestrel LAN Host -> Claude Sonnet 5.5 (`claudeAgent` / `claude-sonnet-5-5`).
  - Refactoring, Headers, Test Suite & CI -> Gemini 3.8 Flash (`acpRegistry_google_antigravity`).
- **Feature Branches & Worktrees:** Use `t3_thread_launch` with `workspaceStrategy: {"type": "worktree", ...}` for isolated feature implementation.
- **Pull Requests:** Call `link_pull_request` for every branch/PR opened to bind it to the T3 thread.
