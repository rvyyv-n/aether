# Agent rules

Full rules and technical invariants: [`docs/AGENTS.md`](docs/AGENTS.md).

- **Opus**: `Aether.Core.Interop` (P/Invoke, DirectX, memory safety, event loop), plus architecture and design review.
- **Sonnet**: `Aether.App`, `Aether.PvP.Review`, `Aether.Server.LanHost` (WinUI 3, controls, trimming, Kestrel).
- **Gemini**: repo scouting, header ingestion, test harnesses, CI workflows, mechanical refactors.
- Threads retire at 8 turns or 150k tokens. Build passes run in a worktree.
