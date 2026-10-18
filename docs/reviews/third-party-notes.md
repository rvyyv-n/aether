# Third-Party Attribution & Licensing Notes

**Branch / Checkout:** `pass-7-menus-toasts-diagnostics`  
**Related Documents:** [ADR-0016 (Licensing & Architecture)](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/docs/DECISIONS.md#adr-0016-unified-utility-suite-proprietary-licensing--lgpl-architecture), [ROADMAP Task 11.8](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/docs/ROADMAP.md#pass-11-embedded-kestrel-lan-streamer--release-verification), [THIRD_PARTY.md](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/THIRD_PARTY.md)

---

## 1. The GPL vs. LGPL Question (Priority 1)

The most critical licensing decision for Aether is the **build flavour of `libmpv` (`libmpv-2.dll`) and `FFmpeg` (`ffmpeg.exe`)**.

### Why This Decides How Aether May Be Distributed
1. **Architectural & Commercial Policy ([ADR-0016](file:///C:/Users/rayyan/.t3/worktrees/aether/pass-7-menus-toasts-diagnostics/docs/DECISIONS.md#adr-0016-unified-utility-suite-proprietary-licensing--lgpl-architecture)):**
   - Aether's application shell, presentation layer, C# playback logic, and companion utilities are **proprietary / source-available** with a restrictive license prohibiting unauthorized commercial redistribution, re-branding, or closed forks.
   - ADR-0016 explicitly establishes that `libmpv` is consumed via **standard C-ABI dynamic linking** (`[LibraryImport]` / source-generated P/Invoke in `Aether.Core.Interop`) to comply with **LGPL v2.1+**.
2. **The Danger of GPL libmpv:**
   - Mainline `libmpv` can be compiled as either **LGPL v2.1+** (using `--enable-lgpl`) or **GPL v2+ / GPL v3+** (the default when optional GPL features or dependencies such as `libx264` or GPL FFmpeg filters are enabled).
   - If `libmpv-2.dll` is compiled under GPL, dynamic linking from Aether causes the combined executable work to be subject to the GPL copyleft terms. Distributing Aether bundled with a GPL `libmpv-2.dll` in `AetherInstaller.exe` or `Aether-portable.zip` would **legally compel Aether to license its own proprietary C# source code under GPL**.
   - **Conclusion:** `libmpv-2.dll` **MUST** be an LGPL build (LGPL v2.1 or LGPL v3).
3. **User Relinking / Replacement Requirement (LGPL v2.1 § 6(b)):**
   - Under LGPL v2.1 Section 6(b), a proprietary application may dynamically link an unmanaged shared library provided the user can replace the library with a compatible alternative.
   - Aether complies with this because `libmpv-2.dll` sits as a separate loose DLL in `%LOCALAPPDATA%\Programs\Aether` (installed app) or in `dist/Aether-portable/` (portable release), resolved at runtime via `NativeLibrary.SetDllImportResolver` in `Aether.Core.Interop.NativeLibraries`. The user can drop in an updated or custom `libmpv-2.dll`.
4. **FFmpeg Build Flavour (`ffmpeg.exe`):**
   - In Pass 5 (`Aether.PvP.Review.Clipping.ClipExportService`), FFmpeg is invoked strictly as an **external command-line process** (`Process.Start("ffmpeg.exe", ...)`) communicating via process arguments and pipes.
   - Under Free Software Foundation (FSF) licensing principles, invoking a CLI binary via standard OS process execution is generally regarded as "mere aggregation" (separate programs), meaning a GPL `ffmpeg.exe` does not infect Aether's managed code as a derivative work.
   - However, if `ffmpeg.exe` is **bundled and redistributed** inside `AetherInstaller.exe` or `Aether-portable.zip`:
     - If `ffmpeg.exe` is GPL, the distributor must accompany it with the exact source code of FFmpeg (or a written offer valid for at least 3 years), publish the build scripts, and provide the GPL license text.
     - If `ffmpeg.exe` is LGPL, the same source offer applies under LGPL terms.
   - Note for **Pass 10 (Precise Clip Export)**: If Pass 10 ever moves from process execution (`Process.Start`) to native in-process linking against `libavcodec` / `libavformat`, FFmpeg must strictly be LGPL and cannot enable GPL encoders (`libx264`, `libx265`).

### What to Check on the Build Machine
Before publishing release artifacts:
1. **Check `libmpv-2.dll` build configuration:**
   ```powershell
   # Look for configuration flags in the binary
   Get-Content -Path "src\Aether.App\Native\x64\libmpv-2.dll" -Encoding Byte -Raw | ...
   # Or run mpv CLI if available from the same build:
   mpv --version
   ```
   Verify that `--enable-lgpl` is present and that `--enable-gpl` is NOT enabled.
2. **Check `ffmpeg.exe` build configuration:**
   ```powershell
   ffmpeg -version
   ```
   Check the configuration banner: look for `--enable-gpl`. If GPL is enabled, decide whether to ship an LGPL-only FFmpeg build or provide the required source code offer for the bundled GPL CLI tool.

---

## 2. Complete Inventory of Covered Dependencies

### A. Managed NuGet Packages & Framework References
Inspected from `Directory.Build.props`, all `.csproj` files (`src/` and `tests/`), and `project.assets.json`:

1. **`Microsoft.WindowsAppSDK` (v2.5.1)**
   - *Referenced in:* `src/Aether.App/Aether.App.csproj`
   - *Licence:* MIT License
   - *Components included:* `Microsoft.WindowsAppSDK.Foundation (2.3.12)`, `Microsoft.WindowsAppSDK.WinUI (2.3.9)`, `Microsoft.WindowsAppSDK.Runtime (2.5.1)`, `Microsoft.WindowsAppSDK.InteractiveExperiences (2.1.9)`, `Microsoft.WindowsAppSDK.Base (2.0.4)`, `Microsoft.WindowsAppSDK.Widgets (2.0.5)`, `Microsoft.WindowsAppSDK.AI (2.5.5)`, `Microsoft.WindowsAppSDK.Search (2.5.5)`, `Microsoft.WindowsAppSDK.ML (2.1.94)`, `Microsoft.WindowsAppSDK.DWrite (2.1.0)`.
   - *Redistribution:* Permitted under MIT. Shipped unpackaged self-contained via `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`.
2. **`Microsoft.Web.WebView2` (v1.0.3719.77)**
   - *Referenced in:* Transitive runtime dependency of WindowsAppSDK.
   - *Licence:* Microsoft Software License Terms.
   - *Redistribution:* Packaged with WinUI 3 runtime binaries.
3. **`Microsoft.Windows.SDK.BuildTools` (v10.0.28000.2705)**
   - *Referenced in:* `src/Aether.App/Aether.App.csproj`
   - *Licence:* Microsoft Software License Terms.
   - *Role:* Build-time compiler and code-generator (XAML parsing, PRI generation via `makepri.exe`). Not redistributed to end users as standalone software.
4. **`QRCoder` (v1.6.0)**
   - *Referenced in:* `src/Aether.Server.LanHost/Aether.Server.LanHost.csproj`
   - *Licence:* MIT License (Copyright (c) 2013-2024 Raffael Herrmann)
   - *Role:* Pure C# QR code matrix generator for mobile LAN stream pairing.
5. **`Microsoft.AspNetCore.App` (.NET 9 / 10 Framework Reference)**
   - *Referenced in:* `src/Aether.Server.LanHost/Aether.Server.LanHost.csproj`
   - *Licence:* MIT License (.NET Foundation)
   - *Role:* In-memory Kestrel HTTP 206 partial content streaming server.
6. **Test & Analysis Packages (Not shipped in release bundles):**
   - `xunit` (2.9.3) - Apache-2.0
   - `xunit.runner.visualstudio` (3.1.4) - Apache-2.0
   - `Microsoft.NET.Test.Sdk` (17.14.1) - Microsoft Software License
   - `coverlet.collector` (6.0.4) - MIT

### B. Unmanaged / Native Binaries
1. **`libmpv` (`libmpv-2.dll` / `mpv-2.dll`):**
   - *Expected location:* `src/Aether.App/Native/{x64|arm64|x86}/` (deployed via `<None CopyToPublishDirectory="PreserveNewest">`)
   - *Runtime loading:* Dynamically loaded via standard C-ABI P/Invoke in `Aether.Core.Interop.NativeLibraries`.
   - *Target version:* `v0.41.0` (Client API 2.5).
   - *Licence:* LGPL v2.1+ (conditional on build configuration).
2. **`ANGLE` (`libEGL.dll` and `libGLESv2.dll`):**
   - *Expected location:* `src/Aether.App/Native/{x64|arm64|x86}/`
   - *Runtime loading:* Loaded by `Aether.Core.Interop.Angle.AngleBridge` before mpv context creation.
   - *Licence:* BSD 3-Clause License (Google Inc. / Chromium Authors).
3. **`FFmpeg` (`ffmpeg.exe`):**
   - *Usage:* `Aether.PvP.Review.Clipping.ClipExportService` for sub-300ms `-c copy` lossless stream clipping.
   - *Discovery:* Evaluated via `ProcessStartInfo("ffmpeg.exe")` on `PATH` or process working directory.
   - *Licence:* LGPL v2.1+ / GPL v2+ (depends on build configuration).
4. **`yt-dlp` (`yt-dlp.exe`):**
   - *Usage:* `Aether_App.Settings.ExternalTools` for stream link resolution and self-updating (`yt-dlp -U`).
   - *Discovery:* Checked next to application (`AppContext.BaseDirectory`), then searched across `PATH`.
   - *Licence:* The Unlicense / Public Domain dedication.

### C. Fonts, Iconography & Assets
1. **`Segoe UI Variable` (Text & Display):**
   - *Usage:* Primary shell typography defined in `DesignTokens.xaml` (`AetherUiFont`, `AetherDisplayFont`).
   - *Licence:* System font provided by Windows 11 / Windows App SDK runtime. Not bundled as raw `.ttf` / `.otf` files.
2. **`Cascadia Mono` / `Cascadia Code`:**
   - *Usage:* Monospaced typography for player timecodes (`AetherTimecodeTextStyle`), diagnostics panel, and the custom mpv options text box.
   - *Licence:* SIL Open Font License 1.1 (OFL-1.1).
   - *Delivery:* System font installed with Windows Terminal and Windows 11.
3. **`Segoe Fluent Icons` / `SymbolThemeFontFamily`:**
   - *Usage:* Transport buttons, settings glyphs, flyout icons.
   - *Licence:* Microsoft Windows App SDK asset.
4. **`AppIcon.ico` & Branding Artwork:**
   - *Usage:* Application icon (a pale star for dark taskbars, `AppIcon.light.ico` for light ones), installer icon, README logo and the watch page's tab icon. All generated by `tools/make_app_icon.py`.
   - *Licence:* Proprietary / Copyright © Aether Project. All rights reserved.

---

## 3. Decisions & Verification Checklist for the User

| # | Item | Current State | Required Decision / Action |
|:---|:---|:---|:---|
| **1** | **`libmpv-2.dll` Build Flavour** | Referenced as target `v0.41.0`, binary is gitignored in `src/Aether.App/Native/` | **Confirm LGPL build.** Verify that the binary intended for distribution was compiled with `--enable-lgpl` (or `--enable-gpl=no`) so that Aether can distribute it lawfully under ADR-0016 without triggering GPL copyleft on Aether's C# code. |
| **2** | **Native Binary Sourcing & CI** | `src/Aether.App/Native/*/*.dll` is gitignored; `.github/workflows/build-aot.yml` builds without native DLLs | **Establish binary download/build pipeline.** Decide whether CI downloads verified prebuilt LGPL libmpv & ANGLE binaries (e.g., from an internal release bucket or an official LGPL mpv release), or builds them from source. |
| **3** | **FFmpeg Distribution Mode** | `ClipExportService` defaults to resolving `"ffmpeg.exe"` from `PATH` or adjacent | **Decide bundling policy:**<br>(A) Bundle an LGPL-built `ffmpeg.exe` inside `AetherInstaller.exe` and `Aether-portable.zip`.<br>(B) Keep `ffmpeg.exe` unbundled; prompt users or look for it on `PATH`.<br>*(If bundled, provide source code download link / written offer).* |
| **4** | **yt-dlp Distribution Mode** | Looked up on `PATH` or adjacent to exe; in-app `yt-dlp -U` updater implemented | **Decide bundling policy:**<br>(A) Ship `yt-dlp.exe` inside installer.<br>(B) Download on demand when the user first opens a URL (Pass 9). *(The Unlicense allows bundling with zero copyleft restrictions).* |
| **5** | **Exact Version Pinning** | `libmpv` is ~v0.41.0; ANGLE, FFmpeg, and yt-dlp versions are floating / environment-dependent | Record exact version numbers in `THIRD_PARTY.md` once release binaries are locked into `src/Aether.App/Native/`. |
| **6** | **Unpackaged WebView2 Runtime** | `Microsoft.Web.WebView2` is pulled transitively by WinUI 3 | Confirm whether the installer needs the WebView2 Evergreen Bootstrapper or relies on Windows 11's built-in WebView2 runtime (standard on Win11 build 22000+). |
