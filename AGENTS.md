# Agent rules

Full rules and technical invariants: [`docs/AGENTS.md`](docs/AGENTS.md).

Version, tag, merge, and release require the owner's say-so.

Skills: `pre-merge-gate` before any merge, `cut-release` for releases.

## Running and Visual Checks
Aether.App is unpackaged (ADR-0017): no MSIX, Developer Mode or certificate is needed. Agents can launch the app directly to verify UI and playback on-screen against the spec:
- `.\run.ps1` (or `.\run.ps1 "path\to\video.mp4"`): incrementally builds `Aether.App` (Debug x64, warnings as errors) and starts its exe straight from the build folder.
- `.\run.ps1 -NoBuild`: launches the existing build instantly without rebuilding.
- `.\run.ps1 -Host "path\to\video.mp4"` (or `.\tools\host.ps1`): spins up the Kestrel LAN server on `http://localhost:8443/watch` for web browser preview testing.
- `Aether.App.exe --visual-audit <outDir> [--video <path>] [--shots a,b]`: captures deterministic PNGs (D3D11 video + backdrop blur composited with WinUI shell and popups) plus layout/overlap telemetry into `<outDir>\manifest.json` using isolated in-memory storage.
- On-demand visual audit workflow (`aether-visual-audit`): kept strictly on-demand (no background schedule). Trigger it during testing when warranted—such as after completing a UI, theme, blur, or rendering build pass (`t3 workflow start aether-visual-audit --input '{"reason":"Post-pass visual check"}'`). By default it checks for UI changes, a clean working tree, AC power, PC idle state, and low CPU/GPU load before running; pass `"ignoreIdle":true` only when explicitly requested during active use.
Always terminate any launched test app or dev server before ending your turn (`Get-Process Aether.App, Aether.DevHost -ErrorAction SilentlyContinue | Stop-Process`).

## Verify
For a test handoff, from the repo root run:
```powershell
dotnet build AetherPlayer.sln -p:Platform=x64 --no-incremental -warnaserror
dotnet test AetherPlayer.sln --filter "Category!=UI"
.\tools\run-ui-tests.ps1   # when UI, windowing, or app startup changed
```

## Executing Roadmap Tasks
1. Pick an open task from `docs/ROADMAP.md`.
2. Implement cleanly with nullable reference annotations (views/controls in `src/Aether.App/Controls/` or `src/Aether.App/Settings/`).
3. Add unit or integration tests in the corresponding project under `tests/`.
4. Run `dotnet build AetherPlayer.sln -p:Platform=x64 -warnaserror` and `dotnet test AetherPlayer.sln --filter "Category!=UI"` (plus `.\tools\run-ui-tests.ps1` for UI changes).
5. Update `docs/CHANGELOG.md` (under `## [Unreleased]`) and mark the task `— **Done**` in `docs/ROADMAP.md` in the same commit as the feature, not afterwards.
