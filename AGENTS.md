# Agent rules

Full rules and technical invariants: [`docs/AGENTS.md`](docs/AGENTS.md).

- **Opus**: `Aether.Core.Interop` (P/Invoke, DirectX, memory safety, event loop), architecture and design review, and all design work (Pass 4 shell/Mica, HUD, seek bar, clip drawer, mobile player shell, QR flyout). Before building design tasks it asks the user questions and requests reference screenshots.
- **Sonnet**: non-design wiring in `Aether.App`, `Aether.PvP.Review`, `Aether.Server.LanHost` (`MpvVideoSurface`, event dispatch, `FrameStepper`, `PlayerViewModel`, FFmpeg export, Kestrel host, trimming).
- **Gemini**: repo scouting, header ingestion, test harnesses, CI workflows, mechanical refactors.
- Threads retire at 8 turns or 150k tokens. Build passes run in a worktree.
