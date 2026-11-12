# Conventions Audit: Recent Commits & Architecture

## Summary

Recent development across the last 25 commits on `pass-7-menus-toasts-diagnostics` adheres exceptionally well to the project's written conventions, architectural invariants, and workflow requirements. 

### Followed Correctly
- **Author Identity & Clean Commits:** All 25 commits are authored and committed exclusively by `rvyyv-n <296653698+rvyyv-n@users.noreply.github.com>`. There are zero AI, assistant, model, or co-authorship attributions in commit messages, metadata, diffs, or code.
- **Commit Titles:** Commit titles are concise, plain-English sentences in the imperative mood explaining what changed and why it matters (for example, "Match mpv's display sync to the refresh rate of the monitor Aether is on, so 120 fps clips stop dropping frames").
- **Quality Gates & Clean Build:** The full solution builds cleanly with zero warnings under `-warnaserror` (`dotnet build AetherPlayer.sln --no-incremental -warnaserror`). All 1,067 unit tests pass across `Aether.Core.Tests` (1,029), `Aether.PvP.Tests` (36), and `Aether.Server.Tests` (2).
- **Zero Stray Debug Output:** No `Console.WriteLine`, `Debug.WriteLine`, or tracing statements exist in `src/`. `Console.WriteLine` is restricted solely to the standalone console installer (`tools/installer/Aether.Installer/Program.cs`).
- **Meaningful Code Comments:** Comments in diffs adhere to the rule of explaining *why* (system quirks, race prevention, DPI scaling, hardware decoder tradeoffs) rather than restating what the code does.
- **Localization & String Centralization:** User-facing strings across all new menus, flyouts (Tracks, Video), diagnostics, and toast notifications are centralized in `src/Aether.App/Strings/en-us/Resources.resw` and accessed via `AppStrings.Get(...)`. Automated tests in `ResourcesStringsTests.cs` confirm key uniqueness, casing conventions (`Area_Name`), shortcut notation (`Ctrl+`), and key presence.
- **ADR-0013 Surface Rules (No Acrylic over Video):** All overlays and flyouts presented over the video swap chain (`MenuFlyoutPresenterStyle`, `TracksPanel`, `VideoPanel`, `VolumeFlyout`, `DiagnosticsPanel`, `Toast`) strictly use solid fallback brushes (`AetherSurfaceDialogFallbackBrush` `#F20E1014` or `AetherSurfaceOverlayStrongBrush`), avoiding in-app Acrylic which fails to sample composition swap chains.
- **ADR-0003 & Threading Rules:** Video playback hot-paths and event processing loops remain zero-allocation. Diagnostics panel updates are throttled and bypassed when the panel is collapsed. Asynchronous property updates (video adjustments, track switching) run on thread pool tasks (`Task.Run` with coalescing and `SemaphoreSlim` serialization) without blocking the WinUI `DispatcherQueue`. The libmpv event loop remains on its dedicated `LongRunning` thread.
- **Test Conventions:** New functionality (context menus, track selection, video adjustments, toast queue, diagnostics rows, display refresh tracking) was implemented with pure, UI-agnostic models in `Aether.Core.Playback` accompanied by comprehensive xUnit tests.

### Deviations
- **Per-Commit Roadmap and Changelog Synchronization:** Pass 7 feature commits (`c2c59ff`, `22daa28`, `197edfd`, `cc49be0`, `058fd2c`, `caaf864`) consistently updated both `docs/ROADMAP.md` and `docs/CHANGELOG.md` in the same commit. However, several earlier foundational commits deviated by leaving documentation updates for subsequent catch-up commits:
  - Commit `003b14a` (monitor refresh sync) did not tick `ROADMAP.md` or update `CHANGELOG.md`; it was retroactively ticked in `caaf864`.
  - Commit `7171765` (in-place engine restart) and Commit `82bb115` (unpackaged runner and standalone installer) omitted roadmap and changelog updates; they were retroactively recorded in catch-up commit `21d46ef`.
  - Bugfix and styling commits (`50f188e`, `39f5efc`, `a4471b9`, `10a21c3`, `326bce9`) did not update changelog or roadmap files.

### Unclear, Stale, or Contradictory Rules
- **Stale Packaging Instructions (`docs/AGENTS.md` vs ADR-0017):** `docs/AGENTS.md` and `AGENTS.md` still state that `Aether.App` is a packaged MSIX app requiring Windows Developer Mode loose package registration, and that unpackaged builds fail with a black window. Commit `82bb115` implemented ADR-0017, transitioning the app to unpackaged WinUI 3 execution via loose `resources.pri` generation and `Program.Main` base directory alignment. `docs/AGENTS.md` was never updated to reflect this architectural shift.
- **Dropped Roadmap Tasks Listed as Active in `docs/AGENTS.md`:** `docs/AGENTS.md` explicitly lists roadmap task 5.1 (`OverlayHud`) under Opus design responsibilities, but task 5.1 was officially dropped per user request in `ROADMAP.md`.
- **Commit Format Specification:** `~/.claude/CLAUDE.md` requires plain-English commit titles explaining what changed and why, but defers to project rules where they disagree. The project has no formal written commit syntax specification beyond identity constraints. Earlier commits used Conventional Commits (`feat(app):`), whereas recent commits strictly use plain-English full sentences.
- **Changelog & Roadmap Cadence:** There is no written policy defining whether *every* bugfix or helper commit requires a changelog entry versus aggregating notable changes per feature milestone.

---

## Conventions Audit Table

| Rule | Where Written | Evidence | Status | Commit / File |
| :--- | :--- | :--- | :--- | :--- |
| **Commit author and committer identity** | `docs/AGENTS.md`, `pre-commit`, `commit-msg` | All 25 commits have author and committer `rvyyv-n <296653698+rvyyv-n@users.noreply.github.com>`. Zero bot, helper, or AI identities. | **Followed** | All 25 commits (`1fcecc7` through `326bce9`) |
| **No AI or model attribution** | `docs/AGENTS.md`, `~/.claude/CLAUDE.md`, global hooks | Verified via git log and commit-msg regex; no mentions of Claude, Gemini, Copilot, Antigravity, or co-authorship. | **Followed** | All 25 commits |
| **Plain-English commit titles** | `~/.claude/CLAUDE.md` | All 25 commit messages are clear, human-readable sentences describing what changed and why without jargon or raw code excerpts. | **Followed** | All 25 commits |
| **Roadmap tick & CHANGELOG per feature commit** | User brief, `docs/CHANGELOG.md` | Feature commits `caaf864`, `058fd2c`, `cc49be0`, `197edfd`, `22daa28`, `c2c59ff` updated both files in the same commit. | **Followed** | `c2c59ff`, `22daa28`, `197edfd`, `cc49be0`, `058fd2c`, `caaf864` |
| **Roadmap tick & CHANGELOG per foundational commit** | User brief, `docs/CHANGELOG.md` | Commits `003b14a` (refresh sync), `7171765` (engine restart), and `82bb115` (standalone runner) did not update roadmap/changelog in-commit; caught up in `21d46ef` and `caaf864`. | **Deviated** | `003b14a`, `7171765`, `82bb115` |
| **No stray debug output in source** | `~/.claude/CLAUDE.md` ("Before handing back") | Zero `Console.WriteLine`, `Debug.WriteLine`, or tracing calls added across `src/`. Console writes are confined to `Aether.Installer`. | **Followed** | `src/Aether.App`, `src/Aether.Core.*` |
| **Explanatory code comments (no restating)** | `~/.claude/CLAUDE.md` ("Before handing back") | Comments explain system quirks, timing, hardware decoder tradeoffs, and DPI constraints rather than restating code operations. | **Followed** | `src/Aether.App/MainPage.*.cs`, `src/Aether.Core.Playback/` |
| **User-facing strings in language file** | ADR-0012, `docs/DECISIONS.md`, `~/.claude/CLAUDE.md` | Context menu, tracks panel, video panel, diagnostics, and toasts load text via `AppStrings.Get(...)` from `Resources.resw`. | **Followed** | `src/Aether.App/Strings/en-us/Resources.resw`, `ResourcesStringsTests.cs` |
| **Sentence-case UI labels and shortcuts** | ADR-0012, `docs/DECISIONS.md` | All menu items and panel labels use sentence case. Accelerators formatted as `Ctrl+<Key>` with no spaces. | **Followed** | `MainPage.ContextMenu.cs`, `TracksPanel.xaml.cs`, `VideoPanel.xaml.cs` |
| **Solid surfaces over video (No Acrylic)** | ADR-0013, `docs/DECISIONS.md` | Context menus, flyouts, and panels over the swap chain use `AetherSurfaceDialogFallbackBrush` and `AetherOverlayStrongPanelStyle`. | **Followed** | `MainPage.xaml`, `VideoPanel.xaml`, `DiagnosticsPanel.xaml`, `DesignTokens.xaml` |
| **Zero allocations on video hot paths** | ADR-0003, `docs/AGENTS.md` | Diagnostics string generation only executes when panel visibility is `Visible`. Frame loops and event sinks allocate zero heap objects. | **Followed** | `DiagnosticsRows.cs`, `DiagnosticsPanel.xaml.cs`, `MpvRenderLoop.cs` |
| **Thread safety and non-blocking UI** | Rule 3, `docs/AGENTS.md`, ADR-0009 | Property pushing (`PushVideo`) uses background tasks with coalescing. Track reading uses `Task.Run` and `SemaphoreSlim`. UI thread never blocked. | **Followed** | `MainPage.Video.cs`, `MainPage.Tracks.cs`, `PlayerSession.cs` |
| **Display refresh rate sync** | ADR-0018, `docs/DECISIONS.md` | Implemented via `DisplayRefreshTracker` and `DisplayChangeWatcher` to match mpv display sync to monitor refresh rates. | **Followed** | `003b14a`, `DisplayRefreshTracker.cs`, `DisplayRefreshSync.cs` |
| **Gate commands and warnings-as-errors** | `docs/AGENTS.md` | `dotnet build AetherPlayer.sln --no-incremental -warnaserror` succeeds with 0 warnings, 0 errors. | **Followed** | `AetherPlayer.sln`, `Directory.Build.props` |
| **Test suite conventions** | `docs/AGENTS.md`, `aether-spec.md` | 1,067 unit tests pass in xUnit across Core, PvP, and Server. Pure logic kept in `Aether.Core.Playback` for testability. | **Followed** | `tests/Aether.Core.Tests/`, `tests/Aether.PvP.Tests/`, `tests/Aether.Server.Tests/` |
| **Naming and project file layout** | `docs/AGENTS.md`, `aether-spec.md` | Clean separation between `Aether.App`, `Aether.Core.Interop`, `Aether.Core.Playback`, `Aether.PvP.Review`, `Aether.Server.LanHost`, `tools/`. | **Followed** | Repo directory structure |
| **Documentation accuracy on app execution** | `docs/AGENTS.md` vs ADR-0017 | `docs/AGENTS.md` claims app is packaged MSIX needing Developer Mode; ADR-0017 established unpackaged execution via `run.ps1`. | **Stale** | `docs/AGENTS.md`, `AGENTS.md` |
| **Roadmap task assignment accuracy** | `docs/AGENTS.md` vs `docs/ROADMAP.md` | `docs/AGENTS.md` assigns task 5.1 (`OverlayHud`) to Opus, but task 5.1 was marked Dropped in `ROADMAP.md`. | **Contradictory** | `docs/AGENTS.md`, `docs/ROADMAP.md` |
| **Commit message format consistency** | `~/.claude/CLAUDE.md` | Earlier commits used Conventional Commits (`feat(app):`); recent commits use plain English sentences; no repo-level standard is documented. | **Unclear** | Git commit history |
