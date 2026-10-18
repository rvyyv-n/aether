# Third-Party Attribution & Licensing Notes

**Branch / Checkout:** `wip/pass-11` (`D:\Code\Worktrees\aether\wip-pass-11`)  
**Related Documents:** [ADR-0016 (Licensing & Architecture)](../../docs/DECISIONS.md#adr-0016-unified-utility-suite-proprietary-licensing--lgpl-architecture), [ROADMAP Task 11.8](../../docs/ROADMAP.md#pass-11-embedded-kestrel-lan-streamer--release-verification), [THIRD_PARTY.md](../../THIRD_PARTY.md)

---

## 1. The GPL vs. LGPL Question (Priority 1 Audit Results)

The most critical licensing constraint for Aether is the **build flavour of `libmpv` (`libmpv-2.dll`) and `FFmpeg` (`ffmpeg.exe`)**.

### A. libmpv (`libmpv-2.dll`): CRITICAL RELEASE BLOCKER
- **Binary Inspected:** `src\Aether.App\Native\x64\libmpv-2.dll` (staged size: 120,938,496 bytes)
- **Reported Version:** `v0.41.0-1122-gaccb7b282` (Client API 2.5)
- **Binary Metadata (VersionInfo Comments):**  
  `mpv is distributed under the terms of the GNU General Public License Version 2 or later.`
- **Build Configuration & Meson Flags (extracted from DLL image):**  
  Built via `mpv-winbuild-cmake` (MinGW-w64 x86_64).  
  Meson cross configuration: `-Ddefault_library=shared -Dprefer_static=true -Dlibmpv=true -Dpdf-build=enabled -Dlua=enabled -Djavascript=enabled -Dsdl2-gamepad=enabled -Dlibarchive=enabled -Dlibbluray=enabled -Ddvdnav=enabled -Duchardet=enabled -Drubberband=enabled -Dlcms2=enabled -Dopenal=enabled -Dspirv-cross=enabled -Dvulkan=enabled -Dvapoursynth=enabled -Dsubrandr=enabled -Dsixel=enabled -Dgl=enabled -Degl-angle=enabled -Dlibcurl=enabled ...`  
  **Active Enabled Features:** `amf build-date cplugins cuda-hwaccel cuda-interop d3d-hwaccel d3d11 d3d9-hwaccel debug direct3d dos-paths dvda dvdnav dxgi-debug-d3d11 egl-angle egl-angle-win32 ffmpeg ffnvcodec gl gl-dxinterop gl-dxinterop-d3d9 gl-win32 glob glob-win32 gpl iconv javascript jpeg lcms2 libarchive libass libavdevice libbluray libcurl libplacebo luajit openal rubberband rubberband-3 sdl2-gamepad shaderc sixel spirv-cross subrandr uchardet vaapi vaapi-win32 vapoursynth vector vk-khr-display vulkan wasapi win32 win32-desktop win32-executable win32-smtc win32-threads zimg zimg-st428 zlib`
- **Flavour Determination:** **GPL v2.0 or later (GPL-2.0+)**. The binary has the `gpl` feature explicitly active.
- **Architectural & Legal Impact ([ADR-0016](../../docs/DECISIONS.md#adr-0016-unified-utility-suite-proprietary-licensing--lgpl-architecture)):**  
  Aether dynamically links `libmpv-2.dll` in-process using standard C-ABI P/Invoke in `Aether.Core.Interop`. If Aether distributes a GPL-built `libmpv-2.dll` bundled inside `AetherInstaller.exe` or `Aether-portable.zip`, the combined executable work falls under the GPL copyleft clause, legally compelling Aether to open-source its proprietary C# application code.
- **Action Required:** **DO NOT SHIP THIS BINARY.** Replace `libmpv-2.dll` with an LGPL-compiled build (`-Dgpl=false` / `--enable-lgpl`) before creating release installers or publishing portable packages.

### B. FFmpeg (`ffmpeg.exe`): GPL CLI Tool (Safe via Process Execution; Written Offer Drafted)
- **Binary Inspected:** `ffmpeg.exe` located on system PATH (`C:\Users\rayyan\AppData\Local\Microsoft\WinGet\Links\ffmpeg.exe`)
- **Reported Version:** `ffmpeg version 9.0.2-full_build-www.gyan.dev` (built with gcc 16.2.0, MSYS2)
- **Build Configuration:**  
  `--enable-gpl --enable-version3 --enable-static --disable-w32threads --disable-autodetect --enable-cairo --enable-fontconfig --enable-iconv --enable-gnutls --enable-lcms2 --enable-libxml2 --enable-gmp --enable-bzlib --enable-lzma --enable-libsnappy --enable-zlib --enable-librist --enable-libsrt --enable-libssh --enable-libzmq --enable-avisynth --enable-libbluray --enable-libcaca --enable-libdvdnav --enable-libdvdread --enable-sdl2 --enable-libaribb24 --enable-libaribcaption --enable-libdav1d --enable-libdavs2 --enable-libopenjpeg --enable-libquirc --enable-libuavs3d --enable-libxevd --enable-libzvbi --enable-liboapv --enable-libqrencode --enable-librav1e --enable-libsvtav1 --enable-libvvenc --enable-libwebp --enable-libx264 --enable-libx265 --enable-libxavs2 --enable-libxeve --enable-libxvid --enable-libaom --enable-libjxl --enable-libsvtjpegxs --enable-libvpx --enable-mediafoundation --enable-libass --enable-frei0r --enable-libfreetype --enable-libfribidi --enable-libharfbuzz --enable-liblensfun --enable-libvidstab --enable-libvmaf --enable-libzimg --enable-amf --enable-cuda-llvm --enable-cuvid --enable-dxva2 --enable-d3d11va --enable-d3d12va --enable-ffnvcodec --enable-libvpl --enable-nvdec --enable-nvenc --enable-vaapi --enable-vulkan --enable-libplacebo --enable-opencl --enable-libcdio --enable-openal --enable-libgme --enable-libmodplug --enable-libopenmpt --enable-libopencore-amrwb --enable-libmp3lame --enable-libshine --enable-libtheora --enable-libtwolame --enable-libvo-amrwbenc --enable-libcodec2 --enable-libilbc --enable-libgsm --enable-liblc3 --enable-libopencore-amrnb --enable-libopus --enable-libspeex --enable-libvorbis --enable-ladspa --enable-libbs2b --enable-libflite --enable-libmysofa --enable-librubberband --enable-libsoxr --enable-chromaprint --enable-whisper`
- **Flavour Determination:** **GPL v3.0 or later (GPL-3.0+)** due to `--enable-gpl --enable-version3` and GPL libraries (`libx264`, `libx265`, `libxvid`, `libdavs2`).
- **Architectural & Legal Impact:**  
  In `Aether.PvP.Review.Clipping.ClipExportService`, FFmpeg is invoked strictly as an external CLI process via `Process.Start("ffmpeg.exe", ...)` using pipes and command-line arguments. Under FSF licensing doctrine, calling an independent CLI tool across the OS process boundary constitutes "mere aggregation" rather than a derivative work, meaning a GPL `ffmpeg.exe` on the user's system does not copyleft Aether's C# code.
- **Bundling & Distribution Requirements:**  
  FFmpeg is currently unbundled in `dist/Aether-portable/`. If Aether ever bundles `ffmpeg.exe` into `AetherInstaller.exe` or portable packages, Aether must:
  1. Accompany the distribution with a written offer valid for at least three (3) years to provide the exact corresponding FFmpeg source code and build scripts (or provide direct source access).
  2. Include the full text of the GNU General Public License Version 3.  
  *Both requirements have been drafted and included in [THIRD_PARTY.md](../../THIRD_PARTY.md).*

---

## 2. Complete Inventory of Covered Dependencies

### A. Managed NuGet Packages & Framework References
Verified against `Directory.Build.props`, project files in `src/` and `tests/`, and `project.assets.json`:

1. **`Microsoft.WindowsAppSDK` (v2.5.1)**
   - *Referenced in:* `src/Aether.App/Aether.App.csproj`
   - *Licence:* MIT License
   - *Included assemblies:* `Microsoft.WindowsAppSDK.Foundation (2.3.12)`, `Microsoft.WindowsAppSDK.WinUI (2.3.9)`, `Microsoft.WindowsAppSDK.Runtime (2.5.1)`, `Microsoft.WindowsAppSDK.InteractiveExperiences (2.1.9)`, `Microsoft.WindowsAppSDK.Base (2.0.4)`, `Microsoft.WindowsAppSDK.Widgets (2.0.5)`, `Microsoft.WindowsAppSDK.AI (2.5.5)`, `Microsoft.WindowsAppSDK.Search (2.5.5)`, `Microsoft.WindowsAppSDK.ML (2.1.94)`, `Microsoft.WindowsAppSDK.DWrite (2.1.0)`.
   - *Redistribution:* Permitted under MIT. Shipped self-contained unpackaged via `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`.
2. **`Microsoft.Web.WebView2` (v1.0.3719.77)**
   - *Referenced in:* Transitive runtime dependency of Windows App SDK.
   - *Licence:* Microsoft Software License Terms.
   - *Redistribution:* Bundled with WinUI 3 runtime binaries (`WebView2Loader.dll`).
3. **`Microsoft.Windows.SDK.BuildTools` (v10.0.28000.2705)**
   - *Referenced in:* `src/Aether.App/Aether.App.csproj`
   - *Licence:* Microsoft Software License Terms.
   - *Role:* Build-time MSBuild compiler targets and PRI code generator (`makepri.exe`). Not redistributed as standalone software.
4. **`QRCoder` (v1.6.0)**
   - *Referenced in:* `src/Aether.Server.LanHost/Aether.Server.LanHost.csproj`
   - *Licence:* MIT License (Copyright © 2013-2024 Raffael Herrmann)
   - *Role:* Pure C# QR code matrix generator for mobile LAN streaming device pairing.
5. **`Microsoft.AspNetCore.App` (.NET 9 Framework Reference)**
   - *Referenced in:* `src/Aether.Server.LanHost/Aether.Server.LanHost.csproj`
   - *Licence:* MIT License (.NET Foundation)
   - *Role:* In-memory Kestrel HTTP 206 partial content streaming server.
6. **Test & Analysis Packages (Build and CI only; not redistributed to end users):**
   - `xunit` (`2.9.3`) - Apache-2.0
   - `xunit.runner.visualstudio` (`3.1.4`) - Apache-2.0
   - `Microsoft.NET.Test.Sdk` (`17.14.1`) - Microsoft Software License
   - `coverlet.collector` (`6.0.4`) - MIT

### B. Unmanaged / Native Binaries
1. **`libmpv` (`libmpv-2.dll`):**
   - *Staged location:* `src/Aether.App/Native/x64/libmpv-2.dll`
   - *Runtime loading:* Dynamically loaded via standard C-ABI P/Invoke in `Aether.Core.Interop.NativeLibraries`.
   - *Audited version:* `v0.41.0-1122-gaccb7b282` (Client API 2.5).
   - *Licence / Flavour:* **GPL-2.0+ (current staged binary; release blocker)**. Target release build must be **LGPL-2.1+**.
2. **`ANGLE` (`libEGL.dll` and `libGLESv2.dll`):**
   - *Staged location:* `src/Aether.App/Native/x64/`
   - *Runtime loading:* Loaded by `Aether.Core.Interop.Angle.AngleBridge` before mpv render context creation.
   - *Audited version:* `2.1.0.0` (Chromium ANGLE 2.1, Copyright © 2015 Google Inc.).
   - *Licence:* BSD 3-Clause License. Safe to redistribute.
3. **`FFmpeg` (`ffmpeg.exe`):**
   - *Usage:* `Aether.PvP.Review.Clipping.ClipExportService` for sub-300ms `-c copy` stream-copy clipping.
   - *Audited version:* `9.0.2-full_build-www.gyan.dev`.
   - *Licence / Flavour:* GPL-3.0-or-later (`--enable-gpl --enable-version3`).
   - *Delivery:* Resolved externally from `PATH`; covered by 3-year source offer in `THIRD_PARTY.md` if bundled.
4. **`yt-dlp` (`yt-dlp.exe`):**
   - *Usage:* `Aether_App.Settings.ExternalTools` for stream link resolution and self-updating (`yt-dlp -U`).
   - *Audited version on dev host:* `2026.07.04` (target bundling version for roadmap 11.9).
   - *Licence:* The Unlicense / Public Domain dedication. Zero copyleft restrictions.

### C. Fonts, Iconography & Assets
1. **`Segoe UI Variable` (Text & Display):**
   - *Usage:* Primary shell typography defined in `DesignTokens.xaml` (`AetherUiFont`, `AetherDisplayFont`).
   - *Licence:* System font provided by Windows 11 / Windows App SDK runtime. Not bundled as raw font files.
2. **`Cascadia Mono` / `Cascadia Code`:**
   - *Usage:* Monospaced typography for player timecodes (`AetherTimecodeTextStyle`), diagnostics panel, and the mpv options text box.
   - *Licence:* SIL Open Font License 1.1 (OFL-1.1).
   - *Delivery:* Windows 11 system font.
3. **`Segoe Fluent Icons` / `SymbolThemeFontFamily`:**
   - *Usage:* Transport buttons, settings glyphs, flyout icons.
   - *Licence:* Microsoft Windows App SDK asset.
4. **`AppIcon.ico` & Branding Artwork:**
   - *Usage:* Application icon (pale star), installer icon, README logo, and web streamer tab icon. Generated by `tools/make_app_icon.py`.
   - *Licence:* Proprietary / Copyright © Aether Project. All rights reserved.

---

## 3. Decisions & Verification Checklist for the User

| # | Item | Current State | Resolved Status & Required Action |
|:---|:---|:---|:---|
| **1** | **`libmpv-2.dll` Build Flavour** | Binary inspected: `v0.41.0-1122-gaccb7b282` with `gpl` feature enabled. | **CRITICAL RELEASE BLOCKER.** The current binary is GPL-2.0+. It CANNOT be bundled with Aether under ADR-0016 without infecting Aether's C# code under GPL copyleft. **Action:** Replace with an LGPL-built binary (`-Dgpl=false` / `--enable-lgpl`) before publishing releases. |
| **2** | **Native Binary Sourcing & CI** | `src/Aether.App/Native/*/*.dll` is gitignored; `.github/workflows/build-aot.yml` builds without native DLLs. | **In Progress.** A clean LGPL libmpv and ANGLE binary download step must be added to release packaging workflows once the LGPL binary artifact URL is established. |
| **3** | **FFmpeg Distribution Mode** | `ClipExportService` resolves `ffmpeg.exe` from `PATH`. System binary is GPL-3.0 (`9.0.2-full_build-www.gyan.dev`). | **Compliant.** Process execution via `Process.Start` constitutes "mere aggregation" and does not infect Aether. If bundled in the future, the 3-year written source code offer and GPL-3.0 text added to `THIRD_PARTY.md` satisfy distribution compliance. |
| **4** | **yt-dlp Distribution Mode** | Looked up on `PATH` or adjacent; version `2026.07.04` verified on dev machine. | **Ready for Roadmap 11.9.** yt-dlp is public domain (The Unlicense). May be safely bundled into installer/portable zip or downloaded on demand with zero copyleft risk. |
| **5** | **Exact Version Pinning** | All components audited from project files, packages, and binaries. | **Complete.** Exact versions pinned across `THIRD_PARTY.md` and this document: Windows App SDK 2.5.1, WebView2 1.0.3719.77, BuildTools 10.0.28000.2705, QRCoder 1.6.0, ASP.NET Core 9.0, ANGLE 2.1.0.0, FFmpeg 9.0.2, yt-dlp 2026.07.04, libmpv v0.41.0-1122-gaccb7b282. |
| **6** | **Unpackaged WebView2 Runtime** | `Microsoft.Web.WebView2` 1.0.3719.77 bundled with WinUI 3 runtime. | **Resolved.** Runtime DLLs are deployed with self-contained unpackaged publish. Windows 11 includes WebView2 system-wide. |
