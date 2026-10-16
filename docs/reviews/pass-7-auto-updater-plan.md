# Pass 7: auto-updater plan (7.28)

Plan only; nothing is built in Pass 7. Decision recorded as ADR-0021 (Proposed) in `docs/DECISIONS.md`.

## 1. Where Aether stands today

- Aether runs **unpackaged** (no MSIX, no package identity; ADR-0017). It ships two ways: `AetherInstaller.exe`, a
  home-made per-user installer (about 215 MB, no admin, installs to `%LOCALAPPDATA%\Programs\Aether`, adds shortcuts,
  Installed apps entry, App Paths, "Play with Aether" and Open with), and a portable zip. Both come from
  `tools/package-standalone.ps1`.
- Nothing is code-signed, the app has no version number set (it reports the .NET default), and there are no
  GitHub Releases yet. CI builds and tests only.
- User data lives in `%LOCALAPPDATA%\Aether\` (settings, resume, recents). Aether makes no network calls of its own
  except yt-dlp for links and its `yt-dlp -U` self-update.
- Most of each release is the same large native libraries (libmpv, ANGLE, Windows App SDK), so a full download per
  update is wasteful.

## 2. Options

| | Fit with how Aether ships | Signing | Delta updates | User control | Effort |
|:---|:---|:---|:---|:---|:---:|
| **Velopack** (open source, successor to Squirrel) | Good: built for unpackaged per-user .NET apps; replaces the home-made installer with its own Setup.exe; reads releases straight from GitHub | Optional; strongly advised (SmartScreen) | Yes, built in | Full: the app decides when to check, download and restart | M |
| **MSIX + App Installer file** | Poor: undoes ADR-0017 (packaging, black-window and resource fixes) | Required, from a trusted certificate | Yes (block-level) | Mostly Windows-driven; weak "check now" | L |
| **Microsoft Store** | Poor as MSIX (same as above); as a plain installer listing the Store does not update the app | Store signs MSIX for free | Yes (MSIX only) | Windows/Store-driven | L |
| **WinGet** | Good as an extra channel: points at the existing installer, which already has `--silent` | Not required, advised | No | Only when the user runs `winget upgrade` | S |
| **Custom check against GitHub Releases** | Good: reuses the current installer's silent upgrade | Not required, advised | No (full 215 MB each time) | Full, but all built by hand | S–M |

Notes: offline and privacy are the same for Velopack and the custom check (one request to GitHub, no account, no ID
sent). MSIX and the Store hand timing to Windows.

## 3. Recommendation: Velopack, releases on GitHub, WinGet later as an extra

Velopack gives small delta downloads, swaps the new version in safely and can roll back if an update is cut short,
handles restart, and needs no change to the unpackaged setup. Its cost is that its Setup.exe takes over from
`AetherInstaller.exe`, so the extras Aether's installer does today move into Velopack's install and uninstall hooks.
A WinGet manifest pointing at the Velopack Setup.exe can follow once releases exist.

**Behaviour for the user:** off by default until the first signed release, then on (asked on first run after
updating). Aether checks at most once a day, a few seconds after launch, never during playback start. A found update
downloads quietly and a toast says "Aether 1.2 is ready, restart to update" with **Restart** and **Later**; it is
otherwise applied on the next exit. Nothing ever restarts on its own while a video is open.

## 4. Build steps for a later pass

1. **Versioning:** set `<Version>` (SemVer, e.g. `0.8.0`) in the app's project file; About and the installer already
   read it. Tag releases `v0.8.0`.
2. **Hook Velopack in:** add the `Velopack` package and call `VelopackApp.Build()...Run()` first thing in
   `Program.Main` (before the working-directory fix). Install hook: shortcuts, App Paths, "Play with Aether", Open
   with (moved from `Aether.Installer`). Uninstall hook: remove them. Pack ID **`AetherPlayer`**, not `Aether` (see
   risks).
3. **Settings keys** (in `SettingDefinitions`, General or About group): `updates.autoCheck` (bool),
   `updates.channel` (`stable` | `beta`), `updates.lastCheck` (time, hidden), `updates.skippedVersion` (hidden).
4. **About page:** a new row under the app version: "Check for updates" button with status text (Up to date ·
   Checking… · Downloading 40% · Ready, restart to update · Couldn't reach GitHub), and a toggle "Check for updates
   automatically". All strings in the language file. Portable copies show "Updates: download a new zip" and no button.
5. **Updater service:** small `UpdateService` in the app (check, download, apply-on-exit, restart) behind an
   interface so tests can fake Velopack; the toast uses the existing toast host.
6. **Release pipeline:** a GitHub Actions workflow on tag push: publish (same flags as `package-standalone.ps1`),
   `vpk pack` with the version from the tag, sign, `vpk upload github` (needs the previous release to build deltas),
   and attach the portable zip. Keep `package-standalone.ps1` for local builds.
7. **Migration:** the first Velopack Setup removes the old `%LOCALAPPDATA%\Programs\Aether` install (runs its
   `--uninstall --silent`) but keeps user data.
8. **Testing:** unit tests for the version compare, daily throttle, skipped version and portable detection; a manual
   run on a clean Windows account: install 0.8.0, publish 0.8.1 to a test repo or local folder, check now, delta
   download, restart, rollback after killing mid-update, uninstall leaves settings alone.

## 5. Risks

- **User data folder clash:** Velopack installs to `%LOCALAPPDATA%\<pack ID>` and deletes that folder on uninstall.
  With the ID `Aether` it would delete settings, resume and recents. Use `AetherPlayer`.
- **Unsigned updates:** SmartScreen will warn on the first download until the publisher builds reputation; an
  unsigned update channel is also easier to tamper with. Velopack checks file hashes from the release, but signing
  is the real fix.
- **Native AOT (Pass 11.4):** confirm Velopack's library trims cleanly before the Release/AOT switch.
- **Licences:** each update redistributes libmpv and, if bundled, FFmpeg; the licence choices in
  `third-party-notes.md` must be settled first.
- **yt-dlp:** keeps its own `-U` updater; the app updater must not overwrite a newer yt-dlp with the bundled one.

## 6. Open questions for you

1. Signing: pay for Azure Trusted Signing (about $10 a month, needs identity checks; individuals are only accepted
   in some countries) or ship unsigned at first and accept SmartScreen warnings?
2. Should updates check by default, or stay opt-in?
3. Do you want a beta channel from the start?
4. Is the GitHub repo going to be public? Private releases need a token inside the app, which is not workable.
5. Keep the home-made `AetherInstaller.exe` as an offline full installer alongside Velopack, or retire it?
