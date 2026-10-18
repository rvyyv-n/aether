# Third-Party Notices and Licences

Aether incorporates or interfaces with several open-source and third-party software components. This document provides attribution and notices in compliance with their respective licences.

> [!NOTE]
> **libmpv and FFmpeg are GPL builds.** The `libmpv-2.dll` shipped with Aether (`v0.41.0-1122-gaccb7b282`) and `ffmpeg.exe` (`9.0.2-full_build-www.gyan.dev`, `--enable-gpl --enable-version3`) are GPL builds. Aether itself is licensed under GPL-3.0-only (see [LICENSE](LICENSE), [NOTICE.md](NOTICE.md) and [ADR-0025](docs/DECISIONS.md)), which is compatible with both, so the whole bundle can be distributed together as long as the written source offer below is kept.

---

## Component Overview

| Component | Version | Licence | Project URL | Usage Mode |
|:---|:---|:---|:---|:---|
| **libmpv** (`libmpv-2.dll`) | `v0.41.0-1122-gaccb7b282` | GPL-2.0-or-later (GPL build; `gpl` feature enabled) | https://mpv.io | Dynamic linking (unmanaged C-ABI P/Invoke); bundled in installer and portable archive |
| **ANGLE** (`libEGL.dll`, `libGLESv2.dll`) | `2.1.0.0` (Chromium ANGLE 2.1) | BSD-3-Clause | https://chromium.googlesource.com/angle/angle | Dynamic loading; bundled in installer and portable archive |
| **FFmpeg** (`ffmpeg.exe`) | `9.0.2-full_build-www.gyan.dev` | GPL-3.0-or-later (`--enable-gpl --enable-version3`) | https://ffmpeg.org | External process execution (`Process.Start` for stream-copy clipping); loaded from `PATH` or bundled with written source offer |
| **yt-dlp** (`yt-dlp.exe`) | `2026.07.04` | The Unlicense (Public Domain) | https://github.com/yt-dlp/yt-dlp | External process execution (web stream resolution); bundled beside the app in the installer and portable archive; a newer copy downloaded into `%LOCALAPPDATA%\Aether	ools` (checked against the release SHA-256) or one on `PATH` is used instead when newer |
| **Windows App SDK** | `2.5.1` | MIT | https://github.com/microsoft/WindowsAppSDK | Managed framework runtime (WinUI 3 desktop shell and composition) |
| **Microsoft.Web.WebView2** (SDK files: `WebView2Loader.dll`, `Microsoft.Web.WebView2.Core*.dll`) | `1.0.3719.77` | BSD-3-Clause (confirmed from the package's own `LICENSE.txt`; the Edge WebView2 runtime itself is not bundled and comes from Windows) | https://developer.microsoft.com/microsoft-edge/webview2/ | Transitive runtime dependency of Windows App SDK |
| **Microsoft.Windows.SDK.BuildTools** | `10.0.28000.2705` | Microsoft Software License | https://aka.ms/WinSDK | Build-time MSBuild targets and PRI resource tooling |
| **QRCoder** | `1.6.0` | MIT | https://github.com/codebude/QRCoder | Managed library dependency in `Aether.Server.LanHost` (LAN streaming QR code generation) |
| **ASP.NET Core (Kestrel)** | `9.0` (.NET 9 BCL) | MIT | https://github.com/dotnet/aspnetcore | Managed framework reference in `Aether.Server.LanHost` (LAN HTTP 206 streaming server) |
| **Cascadia Mono** | System / Windows 11 | SIL Open Font License 1.1 | https://github.com/microsoft/cascadia-code | Referenced typography (timecodes and configuration editor) |
| **Segoe UI Variable & Fluent Icons** | System / Windows 11 | Microsoft Software License | https://learn.microsoft.com/windows/apps/design/style/segoe-ui-symbol-font | System-provided typography and iconography (`SymbolThemeFontFamily`) |

### Development, Test, and Build-Time Components (Not Redistributed in Release Packages)

| Component | Version | Licence | Project URL | Usage Mode |
|:---|:---|:---|:---|:---|
| **xUnit** (`xunit`) | `2.9.3` | Apache-2.0 | https://xunit.net | Unit and integration test framework |
| **xunit.runner.visualstudio** | `3.1.4` | Apache-2.0 | https://xunit.net | Test execution adapter for Visual Studio and `dotnet test` |
| **Microsoft.NET.Test.Sdk** | `17.14.1` | Microsoft Software License | https://aka.ms/mstest | MSBuild targets and discovery engine for unit tests |
| **coverlet.collector** | `6.0.4` | MIT | https://github.com/coverlet-coverage/coverlet | Cross-platform test code coverage collector |

---

## Written Offer for Source Code (GPL Components)

For components distributed in binary form under the GNU General Public License (GPL)—specifically `ffmpeg.exe` and `libmpv-2.dll`:

> **Written Offer for Source Code:**  
> The Aether Project hereby offers to provide, upon request by any third party, a complete machine-readable copy of the corresponding source code for any GPL-licensed binary component distributed in or with Aether (including any modifications and build configuration scripts), on a medium customarily used for software interchange, for a charge no more than the cost of physically performing source distribution.  
> This offer is valid for a period of at least three (3) years from the date of distribution of the software, or for as long as customer support or updates are offered for that software release.
>
> **Upstream Source Repositories & Build Configurations:**
> - **FFmpeg Source Code:** https://ffmpeg.org/download.html  
> - **FFmpeg Gyan.dev Build Scripts & Patches:** https://www.gyan.dev/ffmpeg/builds/  
> - **mpv Source Code:** https://github.com/mpv-player/mpv  
> - **mpv-winbuild-cmake Toolchain:** https://github.com/shinchiro/mpv-winbuild-cmake  
> - **Aether Project Source Inquiries:** https://github.com/rvyyv-n/aether/issues

---

## Licences and Attribution Notices

### 1. GNU General Public License, Version 3.0 (GPL-3.0)
*Applies to: **Aether itself** (GPL-3.0-only), **FFmpeg** (`ffmpeg.exe`, `--enable-gpl --enable-version3`) and **libmpv** (`libmpv-2.dll`, GPL-2.0-or-later build, used under GPL-3.0 terms)*

The full licence text is in the [LICENSE](LICENSE) file at the root of the repository and is included in the installer and portable archive. Additional terms that apply to Aether's own code are in [NOTICE.md](NOTICE.md).

Aether loads `libmpv-2.dll` as a separate library file. You may replace it with an interface-compatible build by replacing the file in the application directory (`%LOCALAPPDATA%\Programs\Aether` or the extracted portable directory). Source code for mpv is available at https://github.com/mpv-player/mpv.

---

### 2. BSD 3-Clause License
*Applies to: **ANGLE** (`libEGL.dll`, `libGLESv2.dll`); the WebView2 SDK files use the same BSD-3-Clause terms, copyright Microsoft Corporation*

```text
Copyright (C) 2018 The ANGLE Project Authors.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:

    Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.

    Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in the
    documentation and/or other materials provided with the distribution.

    Neither the name of Google Inc. nor the names of its contributors
    may be used to endorse or promote products derived from this software
    without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

---

### 3. MIT License
*Applies to: **Windows App SDK**, **QRCoder**, **ASP.NET Core (Kestrel)**, and **coverlet.collector***

#### Windows App SDK
```text
Copyright (c) Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

#### QRCoder
```text
The MIT License (MIT)

Copyright (c) 2013-2024 Raffael Herrmann

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

#### ASP.NET Core (.NET Foundation)
```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors
All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

### 4. The Unlicense / Public Domain Dedication
*Applies to: **yt-dlp***

```text
This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or
distribute this software, either in source code form or as a compiled
binary, for any purpose, commercial or non-commercial, and by any
means.

In jurisdictions that recognize copyright laws, the author or authors
of this software dedicate any and all copyright interest in the
software to the public domain. We make this dedication for the benefit
of the public at large and to the detriment of our heirs and
successors. We intend this dedication to be an overt act of
relinquishment in perpetuity of all present and future rights to this
software under copyright law.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR
OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
OTHER DEALINGS IN THE SOFTWARE.

For more information, please refer to <http://unlicense.org/>
```

---

### 5. Apache License, Version 2.0
*Applies to: **xUnit** and **xunit.runner.visualstudio***

```text
                              Apache License
                        Version 2.0, January 2004
                     http://www.apache.org/licenses/

TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

1. Definitions.
   "License" shall mean the terms and conditions for use, reproduction,
   and distribution as defined by Sections 1 through 9 of this document.

   "Licensor" shall mean the copyright owner or entity authorized by
   the copyright owner that is granting the License.

2. Grant of Copyright License. Subject to the terms and conditions of
   this License, each Contributor hereby grants to You a perpetual,
   worldwide, non-exclusive, no-charge, royalty-free, irrevocable
   copyright license to reproduce, prepare Derivative Works of,
   publicly display, publicly perform, sublicense, and distribute the
   Work and such Derivative Works in Source or Object form.

3. Grant of Patent License. Subject to the terms and conditions of
   this License, each Contributor hereby grants to You a perpetual,
   worldwide, non-exclusive, no-charge, royalty-free, irrevocable
   (except as stated in this section) patent license to make, have made,
   use, offer to sell, sell, import, and otherwise transfer the Work.

4. Redistribution. You may reproduce and distribute copies of the
   Work or Derivative Works thereof in any medium, with or without
   modifications, and in Source or Object form, provided that You
   meet the following conditions:

   (a) You must give any other recipients of the Work or
       Derivative Works a copy of this License; and
   (b) You must cause any modified files to carry prominent notices
       stating that You changed the files; and
   (c) You must retain, in the Source form of any Derivative Works
       that You distribute, all copyright, patent, trademark, and
       attribution notices from the Source form of the Work; and
   (d) If the Work includes a "NOTICE" text file as part of its
       distribution, then any Derivative Works that You distribute must
       include a readable copy of the attribution notices contained
       within such NOTICE file.

[For the full terms and conditions of the Apache 2.0 License,
 please refer to http://www.apache.org/licenses/LICENSE-2.0]
```

---

### 6. SIL Open Font License 1.1 (OFL-1.1)
*Applies to: **Cascadia Mono / Cascadia Code***

```text
Copyright (c) Microsoft Corporation.

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
https://openfontlicense.org

-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded, 
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless prominent written permission is granted by the
corresponding Copyright Holder. This restriction only applies to the
primary font name as presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created
using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, OR CONSEQUENTIAL DAMAGES,
ARISING FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR
FROM OTHER DEALINGS IN THE FONT SOFTWARE.
```

---

### 7. Microsoft Software License Notices
*Applies to: **Microsoft.Windows.SDK.BuildTools**, **Microsoft.NET.Test.Sdk**, **Segoe UI Variable**, and **Segoe Fluent Icons***

Microsoft software components, build tools, runtimes, test adapters, and system fonts are licensed under Microsoft Software License Terms. Use of Windows SDK tools and WebView2 is governed by their respective Microsoft Developer and Redistribution licenses. System fonts (Segoe UI Variable, Segoe Fluent Icons) are referenced through Windows system typography ramps and are licensed as part of the Windows operating system.
