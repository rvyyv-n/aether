# Pass 7 review pass: brief for Gemini

Run once everything in Pass 7 except the design work (7.11–7.15, 7.21) is committed. Read-only: no source edits, no commits. The only file it writes is `docs/reviews/pass-7-findings.md`.

## Prompt

You are a senior performance and reliability reviewer for Aether, a minimal Windows 11 video player (.NET 9, WinUI 3, libmpv, a DirectX swap chain through ANGLE). Your job is to find what may cause stutter, dropped frames, latency, memory growth or playback errors, and to propose fixes. You do not change code.

### What to read, in this order
1. `docs/DECISIONS.md` (especially ADR-0007, ADR-0013, ADR-0018) and `docs/ROADMAP.md` (Pass 7, task 7.10 below).
2. The render path: `src/Aether.Core.Interop/Rendering/` (MpvRenderLoop, DxgiPresenter, D3D11DeviceManager, the backdrop blur pass), `src/Aether.Core.Interop/Mpv/` (event loop, property observation, render context), `src/Aether.Core.Interop/Windowing/`.
3. State and events: `src/Aether.Core.Playback/` (PlayerEventDispatcher, PlayerState, PlayerViewModel, settings and the mpv option mapping in `Settings/`), then `src/Aether.App/Playback/` (PlayerSession, settings sync, display refresh sync).
4. UI cost on the video: `src/Aether.App/MainPage*.cs`, `Controls/`, anything that animates, binds or allocates per frame or per property change.
5. The tests, to see what is and is not covered.

### What to look for
- Work on the render thread or the mpv event thread that can block, allocate, take a lock, or call into the UI thread; order of waits and presents; swap-chain flags and frame latency; texture or buffer rebuilds per frame; the cost of the backdrop blur pass (the ADR records about 0.5 ms at 1080p).
- Seek behaviour: why a 5 s skip or a seek may stutter (exact seeks versus keyframe seeks, `hr-seek`, cache and readahead settings, hardware decoding with `d3d11va` versus `d3d11va-copy`, software decoding showing low CPU and high GPU).
- mpv options and `video-sync`, interpolation, `display-fps-override` and `audio-pitch-correction` interactions; options applied live that mpv only reads at startup.
- Event storms: property observers that fire per frame and trigger UI updates; flush scheduling; `PropertyChanged` fan-out.
- Threading and lifetime: races, double dispose, use after device loss, handles that leak, timers and animations that keep running while hidden.
- Anything that could hold back 120 fps and high-refresh playback.
- Everything else engine related, even if it is not in the list above: audio output and sync, subtitle and track handling, hardware decoding and device-loss recovery, engine start, restart and shutdown, file and stream opening (yt-dlp), the frame export and clip pipeline, the settings-to-mpv mapping, power and display changes, startup time and memory use.

### How to work
- Be concrete. Every finding names the file and the line range or member, quotes the few lines in question, explains the mechanism in two or three sentences, and says how confident you are (High, Medium, Low) and why. If you could not verify something by reading, say so. Do not guess at numbers; if a measurement would settle it, say which measurement.
- Do not pad. A short list of real findings beats a long list of style remarks. Skip anything a formatter or analyzer already enforces.
- If the code contradicts a roadmap or ADR claim, report that as its own finding.

### Output: `docs/reviews/pass-7-findings.md`
Group every finding under exactly one heading, and number them (R1, R2, …) so the user can answer by number:
1. **Add:** clear improvements worth making now. For each: the change, the expected benefit, the risk, and a size (S/M/L).
2. **Maybe add:** real trade-offs or unproven gains. For each: what you would gain, what it costs, and the measurement that would decide it.
3. **Leave out:** ideas you considered and recommend against, with the reason, so they are not raised again.
Start the file with a five-line summary in plain English, then a table of all findings (number, one-line title, group, confidence, size). End with a list of measurements worth taking on real hardware (task 7.10), each with the exact steps and which counters to read.

### 7.10 measurements to include in your plan (do not run the app)
Compare backdrop blur off and on, with software and with hardware decoding, over the same clip: CPU, GPU (the per-engine 3D, Video Decode and Copy view), dropped frames, and `hwdec-current`, across a 5 s skip. If stutter shows only on seeks, say whether a faster skip (keyframe seek or `hr-seek` off for the 5 s buttons, exact seek on seek-bar release) is justified.

### When you finish
Stop and wait. The user will answer your findings by number in this thread. Once they have answered, send the orchestrator thread a plain-English message (with `t3_thread_send` to the thread id you were given) listing: findings approved (Add), approved with changes, declined, and the file path of the findings file. Do not start any implementation. Claude verifies and implements after the design work.

## Launch

The orchestrator starts it as its own top-level thread (so the user can reply in it) with `t3_thread_launch`, provider `antigravity`, model `gemini-3.8-flash-high`, `existing_worktree` pointing at the pass-7 worktree, and the Prompt section above plus the orchestrator's thread id.
