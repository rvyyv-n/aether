# Agent rules

Full rules and technical invariants: [`docs/AGENTS.md`](docs/AGENTS.md).

- **Opus**: `Aether.Core.Interop` (P/Invoke, DirectX, memory safety, event loop), architecture and design review, and all design work (Pass 4 shell/Mica, HUD, seek bar, clip drawer, mobile player shell, QR flyout). Before building design tasks it asks the user questions and requests reference screenshots.
- **Sonnet**: non-design wiring in `Aether.App`, `Aether.PvP.Review`, `Aether.Server.LanHost` (`MpvVideoSurface`, event dispatch, `FrameStepper`, `PlayerViewModel`, FFmpeg export, Kestrel host, trimming).
- **Gemini**: repo scouting (Mode D pre-build scout on Google quota to provide Sonnet a concise ~40-line spec), header ingestion, test harnesses, CI workflows, mechanical refactors, first-pass reviews.
- **Thread & subagent limits**: Retire a Claude thread at 8 human turns before context becomes bloated. Implementation subagents have a generous budget of up to 100k-120k tokens and ~25-30 tool calls; complete before auto-compaction triggers. Micro-batch multi-component passes (max 2-3 items per subagent). Targeted reference reading permitted when the brief lacks specific details.
- Worktrees and delegation follow the global rules in `~/.claude/CLAUDE.md`.

## Verify
For a test handoff, from the repo root run `dotnet build AetherPlayer.sln --no-incremental -warnaserror` then `dotnet test AetherPlayer.sln`.
