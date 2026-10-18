<#
.SYNOPSIS
    Builds and packages Aether as a standalone unpackaged portable bundle and single-file installer.

.DESCRIPTION
    1. Publishes Aether.App unpackaged self-contained for Release x64.
    2. Verifies that resources.pri, native DLLs (mpv, ANGLE), and assets are correctly deployed.
    3. Assembles dist/Aether-portable with portable launchers (run-portable.cmd, Aether.cmd, Install.cmd).
    4. Packs dist/Aether-portable.zip.
    5. Builds tools/installer/Aether.Installer embedding the zip payload into dist/AetherInstaller.exe.

.PARAMETER SkipZip
    Skip zip compression.

.PARAMETER NativeAot
    Publish as Native AOT (needs the Visual Studio C++ tools). Off by default until the AOT build is verified.

.PARAMETER Version
    SemVer to stamp into the build (the release workflow passes the tag without its leading v). Defaults to the version in Directory.Build.props.
#>
[CmdletBinding()]
param(
    [switch]$SkipZip,
    [switch]$NativeAot,
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$AppProject = Join-Path $RepoRoot "src\Aether.App\Aether.App.csproj"
$InstallerProject = Join-Path $RepoRoot "tools\installer\Aether.Installer\Aether.Installer.csproj"
$DistDir = Join-Path $RepoRoot "dist"
$PortableDir = Join-Path $DistDir "Aether-portable"
$ZipFile = Join-Path $DistDir "Aether-portable.zip"
$FinalInstallerExe = Join-Path $DistDir "AetherInstaller.exe"

Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host "             AETHER STANDALONE PACKAGING & INSTALLER BUILD          " -ForegroundColor Cyan
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host ""

# Clean staging directories
if (Test-Path $DistDir) {
    Remove-Item $DistDir -Recurse -Force
}
New-Item -ItemType Directory -Path $PortableDir -Force | Out-Null

# Step 1: Publish Aether.App
Write-Host "[1/5] Publishing Aether.App ($Platform $Configuration unpackaged self-contained)..." -ForegroundColor Cyan
$publishArgs = @(
    "publish", $AppProject,
    "-c", $Configuration,
    "-r", "win-$Platform",
    "-p:Platform=$Platform",
    "--self-contained", "true",
    "-p:WindowsPackageType=None",
    "-p:WindowsAppSDKSelfContained=true",
    "-p:PublishSingleFile=false",
    "-warnaserror",
    "-p:NuGetAudit=false",
    "-o", $PortableDir
)
if ($Version) {
    $publishArgs += "-p:Version=$Version"
}
# Native AOT needs the C++ build tools and has not been verified end to end yet, so the default stays a plain self-contained publish.
if ($NativeAot) {
    $publishArgs += "-p:PublishAot=true"
} else {
    $publishArgs += "-p:PublishAot=false", "-p:PublishTrimmed=false", "-p:PublishReadyToRun=true"
}
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "[Error] Publishing Aether.App failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

# Step 1b: Bundle yt-dlp so the first web link plays with no setup. The pinned release is checked against its published SHA-256.
$ytDlpVersion = "2026.07.04"
$ytDlpBase = "https://github.com/yt-dlp/yt-dlp/releases/download/$ytDlpVersion"
$ytDlpExe = Join-Path $PortableDir "yt-dlp.exe"
Write-Host "       Bundling yt-dlp $ytDlpVersion..." -ForegroundColor Cyan
Invoke-WebRequest -Uri "$ytDlpBase/yt-dlp.exe" -OutFile $ytDlpExe -UseBasicParsing
$ytDlpSums = [Text.Encoding]::UTF8.GetString((Invoke-WebRequest -Uri "$ytDlpBase/SHA2-256SUMS" -UseBasicParsing).Content)
$ytDlpExpected = ($ytDlpSums -split "`n" | Where-Object { $_ -match "\s\*?yt-dlp\.exe\s*$" } | Select-Object -First 1) -split "\s+" | Select-Object -First 1
$ytDlpActual = (Get-FileHash $ytDlpExe -Algorithm SHA256).Hash
if (-not $ytDlpExpected -or $ytDlpActual -ne $ytDlpExpected.ToUpperInvariant()) {
    Remove-Item $ytDlpExe -Force
    Write-Error "[Error] yt-dlp.exe did not match its published SHA-256."
    exit 1
}

# Step 2: Verify essential unpackaged assets
Write-Host "[2/5] Verifying unpackaged runtime assets in portable directory..." -ForegroundColor Cyan
$requiredFiles = @(
    "Aether.App.exe",
    "resources.pri",
    "libmpv-2.dll",
    "yt-dlp.exe",
    "libEGL.dll",
    "libGLESv2.dll",
    "Microsoft.UI.Xaml.dll"
)
foreach ($req in $requiredFiles) {
    $p = Join-Path $PortableDir $req
    if (-not (Test-Path $p)) {
        Write-Error "[Error] Missing required file in portable package: $req"
        exit 1
    }
}
Write-Host "       [OK] All required runtime binaries and resources.pri verified." -ForegroundColor Green

# Step 3: Add portable launcher helpers to dist/Aether-portable
Write-Host "[3/5] Adding portable runners and installer scripts to portable folder..." -ForegroundColor Cyan

# Aether.cmd (command launcher)
$aetherCmd = "@echo off`r`nstart `"`" `"%~dp0Aether.App.exe`" %*`r`n"
Set-Content -Path (Join-Path $PortableDir "Aether.cmd") -Value $aetherCmd

# run-portable.cmd
$runPortableCmd = "@echo off`r`ncd /d `"%~dp0`"`r`nstart `"`" `"%~dp0Aether.App.exe`" %*`r`n"
Set-Content -Path (Join-Path $PortableDir "run-portable.cmd") -Value $runPortableCmd

# Install.cmd
$installCmd = "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"%~dp0Install.ps1`" %*`r`n"
Set-Content -Path (Join-Path $PortableDir "Install.cmd") -Value $installCmd

# Copy Install.ps1
Copy-Item (Join-Path $RepoRoot "tools\Install.ps1") -Destination (Join-Path $PortableDir "Install.ps1") -Force

Write-Host "       [OK] Portable launchers created." -ForegroundColor Green

# Step 4: Create zip archive
Write-Host "[4/5] Creating portable distribution archive (Aether-portable.zip)..." -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path $ZipFile) { Remove-Item $ZipFile -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($PortableDir, $ZipFile, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$zipSizeMb = [math]::Round((Get-Item $ZipFile).Length / 1MB, 2)
Write-Host "       [OK] Created $ZipFile ($zipSizeMb MB)." -ForegroundColor Green

# Step 5: Build single-file AetherInstaller.exe with embedded payload
Write-Host "[5/5] Building standalone AetherInstaller.exe..." -ForegroundColor Cyan
$installerPayloadPath = Join-Path $RepoRoot "tools\installer\Aether.Installer\payload.zip"
Copy-Item $ZipFile -Destination $installerPayloadPath -Force

$installerPublishDir = Join-Path $DistDir "installer-build"
$installerArgs = @(
    "publish", $InstallerProject,
    "-c", "Release",
    "-r", "win-$Platform",
    "-p:Platform=$Platform",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:NuGetAudit=false",
    "-o", $installerPublishDir
)
& dotnet @installerArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "[Error] Building AetherInstaller.exe failed."
    exit $LASTEXITCODE
}

# Move final installer to dist/AetherInstaller.exe
$builtInstaller = Join-Path $installerPublishDir "Aether.Installer.exe"
if (-not (Test-Path $builtInstaller)) {
    $builtInstaller = Join-Path $installerPublishDir "AetherInstaller.exe"
}
Copy-Item $builtInstaller -Destination $FinalInstallerExe -Force
Remove-Item $installerPublishDir -Recurse -Force
Remove-Item $installerPayloadPath -Force -ErrorAction SilentlyContinue

# Also place AetherInstaller.exe into dist/Aether-portable so the portable folder can be self-installing
Copy-Item $FinalInstallerExe -Destination (Join-Path $PortableDir "AetherInstaller.exe") -Force

$installerSizeMb = [math]::Round((Get-Item $FinalInstallerExe).Length / 1MB, 2)

Write-Host ""
Write-Host "====================================================================" -ForegroundColor Green
Write-Host "                   PACKAGING COMPLETED SUCCESSFULLY!                " -ForegroundColor Green
Write-Host "====================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "Generated Artifacts in dist/:" -ForegroundColor White
Write-Host "  1. Standalone Installer: $FinalInstallerExe ($installerSizeMb MB)" -ForegroundColor Cyan
Write-Host "  2. Portable Zip:        $ZipFile ($zipSizeMb MB)" -ForegroundColor Cyan
Write-Host "  3. Portable Directory:  $PortableDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "Usage:" -ForegroundColor White
Write-Host "  - Double-click AetherInstaller.exe to install to %LOCALAPPDATA% without Admin rights."
Write-Host "  - Run 'AetherInstaller.exe --silent' for unattended scripted installation."
Write-Host "  - Run 'AetherInstaller.exe --uninstall' for clean per-user removal."
Write-Host "  - Run 'dist\Aether-portable\run-portable.cmd' to run portably anywhere."
Write-Host ""
