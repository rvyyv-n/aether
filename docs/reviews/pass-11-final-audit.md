# Pass 11 final audit (11.5 and pre-release sweep)

Scope: the whole app for performance and safety; the code added in Pass 11 and the light-mode work for design and quality.
Read against `master` at e7fef62. Nothing was launched; every finding below was checked against the code.

## Verdict

No blockers. Seven should-fix findings (two of them the 11.5 performance items themselves) and a short list of
nice-to-haves. Three questions need the owner's decision before the first public release.

## Should fix

| # | Finding | Where | Why it matters | Fix | Who | Needs |
|---|---|---|---|---|---|---|
| S1 | The low-pause collector settings are not applied anywhere. ADR-0003 says `SustainedLowLatency` during playback, but nothing sets it, and the project file has no collector settings, so the app runs with the .NET defaults. | `src/Aether.App/Aether.App.csproj` (no collector properties); `src/Aether.App/Program.cs:16` (`Main` never sets `GCSettings.LatencyMode`) | A full blocking collection during playback can stall the render thread long enough to drop a frame at 120 Hz and above. | Workstation collector with background collection on, and `SustainedLowLatency` set at the top of `Main`. Note: the roadmap asked for "non-concurrent", but `SustainedLowLatency` only works *with* concurrent (background) collection; with it off, the mode is silently ignored. So concurrent stays on. | Opus | A one-line check with the measurement script (it prints the mode the build really runs with) |
| S2 | An update download can report progress after it has finished. The late report turns "Ready" back into "Downloading". | `src/Aether.Core.Playback/Updates/UpdateController.cs:46-52` | The About page then says "Downloading" for the rest of the session and Check now does nothing (it refuses while a download runs). The update still installs on exit. | Check and set the status together under one lock, and raise status events in order. | Opus | No |
| S3 | 11.5 asks for a way to measure cold start and dropped frames, and there is none. | `tools/` | Without it the 300 ms target and "0 dropped frames" cannot be confirmed on real hardware. | An opt-in timing log in the app (only when `AETHER_PERF_LOG` is set) plus `tools/measure-performance.ps1`. | Opus | Owner runs it |
| S4 | The phone card puts focus on its switch even when the switch is disabled (a link is open, or there is no network). Focus then stays behind the dimmed scrim. | `src/Aether.App/MainPage.LanStream.cs:121` | Keyboard and screen-reader users cannot reach the card or learn why it is off. | Focus the switch only when it is enabled, otherwise the card itself. | Sonnet | On-screen check |
| S5 | The phone card's status line ("Waiting for phone", "1 phone watching") and its notes ("Streaming couldn't start") change silently. | `src/Aether.App/MainPage.xaml:1322`, `:1411` | Screen-reader users are not told that streaming started, failed or that a phone joined. | `AutomationProperties.LiveSetting` Polite on the status, Assertive on the note. | Sonnet | No |
| S6 | On the Streaming page the yt-dlp button says "Updating…" during a first install, and the button is hidden after the action. Keyboard focus is lost when it disappears. | `src/Aether.App/Settings/Pages/StreamingPage.xaml.cs:76-77`, `:65` | Wrong wording on first install, and keyboard users are dropped to the top of the page. | An "Installing…" string when installing; move focus to the row before hiding the button. | Sonnet | On-screen check |
| S7 | Four text entries are no longer used by anything. | `src/Aether.App/Strings/en-us/Resources.resw:80` (`EmptyState_SubtitleFilesOnly`), `:174` (`Shell_Loading`), `:1014` (`Settings_QualityPreset_High`), `:1125` (`Settings_Shortcuts_Reset`) | Dead text gets translated and maintained for nothing. | Delete them. | Sonnet | No |

## Checked and sound

- **Render hot path.** `MpvRenderLoop` waits on kernel events and allocates nothing per frame; the size lock is held for
  a few field copies; the blur's per-card state is read under a tiny lock as struct copies. The P8.7 fix holds: a redraw
  with no new video frame copies the cached frame instead of asking mpv to render again, and no swap is reported to mpv
  for it. 120 Hz+ pacing still follows ADR-0018 (wait for a frame, poll, wait for the swap chain, render, present).
- **Event dispatch.** One pending state under a small lock, a flush requested only on the clean-to-dirty change, and
  applied once per displayed frame through a cached handler. No per-event allocation apart from text properties that
  actually changed.
- **Seek coalescing** (`SeekCoalescer`) and frame stepping send at most one command in flight and allocate nothing to send.
- **Interop.** The libmpv signatures, structure layouts and callbacks match the C headers; DirectX objects are released
  exactly once in reverse order on the render thread; the UI thread's stretch-on-resize is guarded by the same lock that
  releases the swap chain, so it can never touch a freed one. The taskbar preview buttons (11.14) match the Windows
  structure layout, free every GDI object and icon, and never let an exception escape into Windows. Device loss, engine
  restart and background core destruction were audited in 11.6 and nothing has changed since.
- **Picture in picture** keeps one window, one engine and one render thread (ADR-0022); only a resize reaches the loop.
- **Phone server.** It serves exactly one file at a fixed address; there is no file name or path in any request, so no
  path traversal or directory listing is possible. Every request needs the 16-character random code, compared in
  constant time, and a new code is made every time the switch goes on. Range requests are handled by ASP.NET Core.
  Kestrel's default header and connection limits apply. It stops when switched off, when another file opens and at exit
  (Aether waits up to a second for the port to be released).
- **Settings and upgrades.** Settings files are written to a temporary file and swapped in, unknown entries are ignored
  and a broken file falls back to defaults, so an older or newer settings file never stops Aether opening. The update
  package id (`AetherPlayer`) differs from the data folder (`Aether`), so uninstalling never removes user data.
- **Updater when offline.** Every failure (no network, rate limit, bad download, failed hash) ends as "couldn't reach
  GitHub" with the installed copy untouched; Velopack checks the package hash before swapping anything.
- **yt-dlp.** Downloads go to a `.download` file, are checked against the release's SHA-256 and only then replace the
  copy in use; a mismatch or failure deletes the partial file. Nothing downloads until the user asks or a copy already
  exists for the daily update.
- **Settings and JSON under Native AOT.** All JSON is read with `JsonDocument` and written with `Utf8JsonWriter`: no
  reflection. The web server uses the request delegate generator. CI's Native AOT publish passes with warnings as errors.
- **Reduced motion.** Every Pass 11 animation goes through `ChromeFader` or `Motion`, which honour "Animation effects"
  off. The phone card is forced dark, so its text keeps its contrast in light mode.
- **Release workflow.** The v0.9.0-alpha.9 cloud build failed because the engine files were missing; the tag predates
  the fix that fetches them (b228f51), and the next run with the fix (v0.9.0-alpha.8-citest) passed.

## Nice to have (not fixed)

1. The frame cache copies every video frame once more even when no card is on screen
   (`src/Aether.Core.Interop/Rendering/AngleMpvRenderBackend.cs:104-114`). Cheap on modern GPUs (one full-screen copy),
   but on the R7 350 class of card at 4K it is measurable. Render straight to the back buffer while no card is visible
   or fading. Needs measuring on the owner's PC first.
2. `MainPage.PostBackdropRegion` runs on every layout pass and creates a few small transform objects each time
   (`src/Aether.App/MainPage.xaml.cs:125`, `MainPage.Backdrop.cs`). UI-thread garbage only, not the render thread.
3. The first window can wait up to 250 ms for the settings and window files on a very slow disk
   (`src/Aether.App/App.xaml.cs:117`). Normally a few milliseconds; the measurement script will show it.
4. A failure writing yt-dlp's last-check file ends that session's daily update loop
   (`src/Aether.App/Settings/YtDlpManager.cs:163`, `:216`).
5. Taskbar preview icons keep their first size after the window moves to a monitor with another scale
   (`src/Aether.Core.Interop/Windowing/TaskbarThumbButtons.cs:242`).
6. Design tokens bypassed on the About page: icon sizes (`AboutPage.xaml:42`, `:65`, `:84`, `:103`), a chevron size
   with no token (`:70`, `:89`, `:108`), three copies of the same row layout (`:54-110`), and the white caption glyph
   on hover (`MainWindow.xaml.cs:534`) has no token. The 2-second "Copied" delay is written out in three places.
7. Wording: the phone page is titled "Aether LAN Player" and its "can't play" notice names raw codecs
   (`src/Aether.Server.LanHost/WatchPage.cs:14`, `:46-55`); the busy-port note shows a port number
   (`LanStream_NotePortBusy`); the hash failure says "checksum" (`YtDlp_InstallFailedHash`). Duplicate strings:
   `Settings_Updates_Version` / `Settings_About_Version`, `Settings_YtDlp_Download` / `Stream_InstallYtDlp`.
8. Small tidy-ups in `MainWindow.xaml.cs`: the taskbar icon is applied twice on every theme change (`:128`, `:488`),
   two settings handlers and two `Closed` blocks could be one. `LanStreamTone.Warning` is never used.
9. Engine restart still waits up to 250 ms on the UI thread for the old render thread (known, 11.6).

## Owner decisions

1. **Native AOT or not for 1.0.** Release builds compile as Native AOT in CI, but the installer, the portable zip and
   the update packages are built as a normal self-contained app (`tools/package-standalone.ps1`, `-NativeAot` is off),
   because the AOT build has never been run end to end. Shipping as is is the safe choice; AOT would start faster.
   Either way, the measurement script works on both.
2. **Phone streaming on public networks.** The server listens on every network adapter, including public Wi-Fi, over
   plain HTTP. The random code keeps strangers out, but anyone on the same café network who can watch traffic can read
   the code and the video. Options: keep as is; refuse to start on networks Windows marks as Public; or show a warning
   there. The first switch-on also raises the Windows Firewall prompt, which needs an on-screen check.
3. **Pre-release tags.** The updater ignores releases marked "pre-release" on GitHub. If the beta is published as a
   pre-release, installed copies will not update to the next beta. Publish betas as normal releases, or switch the
   feed to include pre-releases for the beta period.

## On-screen and on-PC checks for the owner

- Run `.\tools\measure-performance.ps1 -Video "<a 60 or 120 fps clip>"` with Aether closed. The window should appear in
  under 300 ms (median after the first run), the collector line should read `server=False, latency mode=SustainedLowLatency`,
  and dropped frames should be 0 apart from a few while the video loads.
- Phone card: with a link open (switch disabled) press `S` and Tab; focus should land inside the card. With Narrator on,
  switch streaming on and off and connect a phone; each change should be read out.
- Streaming settings with yt-dlp removed: Install should say "Installing…", and focus should stay on the yt-dlp row
  when the button disappears.
- First switch-on of phone streaming: note what the Windows Firewall prompt asks and what happens if it is cancelled.
