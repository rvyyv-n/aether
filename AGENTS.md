# Operational Instructions for AI Agents

See detailed operational guidelines and role division in [`docs/AGENTS.md`](docs/AGENTS.md).

- **Claude Opus 5.5**: `Aether.Core.Interop` (P/Invoke, DirectX, memory safety, event loop).
- **Claude Sonnet 5.5**: `Aether.App`, `Aether.PvP.Review`, `Aether.Server.LanHost` (WinUI 3 XAML, controls, trimming, Kestrel).
- **Gemini**: Scaffolding, refactoring, header ingestion, test harness generation, CI workflows.
