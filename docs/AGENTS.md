# Agent rules

## Stack and build
Native AOT player on .NET 10 (WinUI 3): libmpv C API bindings, with DirectComposition / Direct3D 11 interop for the swap chain. Everything builds and ships on x64.

### Architecture map
```
AetherPlayer.sln (.slnx alongside)
├── src/
│   ├── Aether.Core.Interop/     [.NET 10] C-ABI LibraryImport bindings, DirectX 11 SwapChainPanel,
│   │                                      ANGLE EGL, DirectComposition, unmanaged memory safety.
│   ├── Aether.Core.Playback/    [.NET 10] UI-agnostic playback session, throttled dispatch,
│   │                                      seeking coalescer, chapters, subtitle styling, settings.
│   ├── Aether.App/              [.NET 10 WinUI 3] Presentation layer, Mica/Acrylic/OLED backdrops,
│   │                                      PrecisionSeekBar, ClipDrawer, VideoPanel, TracksFlyout,
│   │                                      HoverRail, Theme/DesignTokens.xaml, Settings pages.
│   ├── Aether.PvP.Review/       [.NET 10] High-FPS frame stepping, lossless FFmpeg clipping,
│   │                                      hardware-accelerated re-encoding pipeline.
│   └── Aether.Server.LanHost/   [.NET 10] Embedded Kestrel HTTP 206 streaming host & QR flyout.
└── tests/
    ├── Aether.Core.Tests/       [xUnit] Interop, playback, event loop, tokens, and settings tests.
    ├── Aether.PvP.Tests/        [xUnit] GOP alignment, FFmpeg args, frame stepper tests.
    ├── Aether.Server.Tests/     [xUnit] HTTP 206 range requests, token authentication, QR tests.
    └── Aether.App.UiTests/      [xUnit + FlaUI] Live UIA3 smoke, navigation, and playback checks.
```

```powershell
dotnet restore AetherPlayer.sln
dotnet build AetherPlayer.sln -p:Platform=x64 --no-incremental -warnaserror   # warnings are errors (Directory.Build.props)
dotnet test AetherPlayer.sln --filter "Category!=UI"
.\tools\run-ui-tests.ps1                                                      # live FlaUI UI automation suite
```
CI builds and tests in Release (`.github/workflows/build-aot.yml` for unit/integration tests and `.github/workflows/ui-tests.yml` for the FlaUI suite).

Running the app: `Aether.App` is an unpackaged, self-contained WinUI 3 app (ADR-0017); no Developer Mode, certificate or MSIX is involved. `.\run.ps1` (forwards to `tools/run.ps1`) does an incremental Debug x64 `dotnet build -warnaserror` of `Aether.App` (retrying once after a restore from the local NuGet cache), then starts `Aether.App.exe` straight from its build folder, optionally with a video path. `-NoBuild` skips the build, `-Rebuild` clears `obj` first, `-Platform`/`-Configuration` override x64/Debug. To locally host a video via the Kestrel stream server, run `.\run.ps1 -Host <video-path>` (runs `tools/dev-host`). Installers and the portable zip come from `tools/package-standalone.ps1` (unpackaged publish, then `dist/AetherInstaller.exe` and `dist/Aether-portable.zip`). libmpv and ANGLE (`libmpv-2.dll`, `libEGL.dll`, `libGLESv2.dll`) go in `src/Aether.App/Native/<Platform>/` (git-ignored) and are packaged next to the exe; only x64 binaries exist so far, so a playback run needs `-p:Platform=x64`.

## Rules
1. **P/Invoke**: no `[DllImport]`. Use source-generated `[LibraryImport]` with explicit marshaling (Native AOT).
2. **Frame loop**: zero allocations in `MpvRenderLoop`, swap chain present, and libmpv callbacks — never marshal managed objects per tick.
3. **Threads**: `mpv_wait_event` runs on a dedicated `TaskCreationOptions.LongRunning` thread. Never block the WinUI `DispatcherQueue`; marshal throttled UI state updates through `DispatcherQueueFlushScheduler`.
4. **Errors & COM**: guard unmanaged pointer operations. Check Direct3D `HRESULT`s with `Marshal.ThrowExceptionForHR` or log the failure. Release backbuffer references before resizing swap chains (`ResizeBuffers`).
5. **UI**: minimal Windows 11 design. No window borders, Mica/Acrylic/OLED surfaces, auto-hiding scrubbers, high-contrast type. Keep colors, brushes, and dimension tokens in `src/Aether.App/Theme/DesignTokens.xaml`. Confirm design direction with the user before building major new UI surfaces.
6. **Callbacks**: `[UnmanagedCallersOnly]` plus `delegate* unmanaged[Cdecl]` for libmpv. No managed delegates.
7. **Processes**: build arguments with `ProcessStartInfo.ArgumentList`.
8. **Decisions**: record architecture choices in `docs/DECISIONS.md`.
9. **Clean exits**: never leave `Aether.App`, `Aether.DevHost`, or a file watcher running at the end of a turn.

## Commits
Author and committer follow the repo owner's identity (`rvyyv-n`) only. No co-author, generated-by, or assistant mentions in commits, PRs, or files; the global git hooks reject them. Never `--no-verify`.
