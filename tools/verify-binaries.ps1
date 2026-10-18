<#
.SYNOPSIS
    Verifies that libmpv and ANGLE native binaries are present where the app and tests expect them,
    tests that they can be loaded, and reports their versions.

.DESCRIPTION
    Checks:
      1. src/Aether.App/Native/x64 (source staging folder copied by csproj)
      2. App build output directories (win-x64)
      3. tests/Aether.Core.Tests/bin output directories
      4. Dynamic loading and version reporting for libmpv-2.dll, libEGL.dll, and libGLESv2.dll
#>
[CmdletBinding()]
param(
    [string]$Architecture = "x64"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host "             AETHER NATIVE BINARY & INTEGRATION VERIFIER            " -ForegroundColor Cyan
Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host "Repository: $RepoRoot" -ForegroundColor Gray
Write-Host "Target Architecture: $Architecture" -ForegroundColor Gray
Write-Host ""

$stagingDir = Join-Path $RepoRoot "src\Aether.App\Native\$Architecture"
$testBinDir = Join-Path $RepoRoot "tests\Aether.Core.Tests\bin\Debug\net10.0"
$appBinDir = Join-Path $RepoRoot "src\Aether.App\bin\$Architecture\Debug\net10.0-windows10.0.26100.0\win-$Architecture"

$requiredBinaries = @(
    @{ Name = "libmpv-2.dll"; Role = "Core Playback Engine (libmpv C-ABI)" },
    @{ Name = "libEGL.dll"; Role = "ANGLE EGL 1.4 Display & Client Buffer" },
    @{ Name = "libGLESv2.dll"; Role = "ANGLE OpenGL ES 2.0 / 3.0 Implementation" }
)

$allFound = $true
$loadFailed = $false

Write-Host "[1/2] Checking binary existence across expected locations..." -ForegroundColor Cyan

foreach ($bin in $requiredBinaries) {
    $name = $bin.Name
    $role = $bin.Role
    Write-Host "  * Checking $name ($role):" -ForegroundColor White

    $foundPath = $null
    $locations = @(
        @{ Label = "App Staging (src/Aether.App/Native/$Architecture)"; Path = (Join-Path $stagingDir $name) },
        @{ Label = "Test Output (tests/Aether.Core.Tests/bin)"; Path = (Join-Path $testBinDir $name) },
        @{ Label = "App Build Output (src/Aether.App/bin)"; Path = (Join-Path $appBinDir $name) }
    )

    foreach ($loc in $locations) {
        $p = $loc.Path
        $lbl = $loc.Label
        if (Test-Path $p) {
            $lenMb = [math]::Round((Get-Item $p).Length / 1MB, 2)
            Write-Host "    [FOUND] $lbl ($lenMb MB)" -ForegroundColor Green
            if (-not $foundPath) { $foundPath = $p }
        } else {
            Write-Host "    [MISSING] $lbl" -ForegroundColor Yellow
        }
    }

    if (-not $foundPath) {
        Write-Host "    [ERROR] $name was not found in any expected location!" -ForegroundColor Red
        $allFound = $false
    }
}

Write-Host ""
Write-Host "[2/2] Loading native libraries and inspecting versions..." -ForegroundColor Cyan

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class NativeLoader
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
}
"@ -ErrorAction SilentlyContinue

foreach ($bin in $requiredBinaries) {
    $name = $bin.Name
    $targetPath = $null

    # Search in staging, test bin, or app bin
    foreach ($cand in @((Join-Path $stagingDir $name), (Join-Path $testBinDir $name), (Join-Path $appBinDir $name))) {
        if (Test-Path $cand) {
            $targetPath = $cand
            break
        }
    }

    if ($targetPath) {
        $item = Get-Item $targetPath
        $versionInfo = $item.VersionInfo
        $fileVersion = $versionInfo.FileVersion
        $prodVersion = $versionInfo.ProductVersion

        Write-Host "  * Inspecting $name from: $targetPath" -ForegroundColor White
        Write-Host "    File Size:       $($item.Length) bytes ($([math]::Round($item.Length / 1MB, 2)) MB)" -ForegroundColor Gray
        Write-Host "    File Version:    $($fileVersion)" -ForegroundColor Gray
        Write-Host "    Product Version: $($prodVersion)" -ForegroundColor Gray

        # Test load
        $handle = [NativeLoader]::LoadLibrary($targetPath)
        if ($handle -ne [IntPtr]::Zero) {
            Write-Host "    Load Status:     LOAD SUCCESSFUL (Handle: 0x$($handle.ToString('X')))" -ForegroundColor Green

            if ($name -eq "libmpv-2.dll") {
                $export = [NativeLoader]::GetProcAddress($handle, "mpv_client_api_version")
                if ($export -ne [IntPtr]::Zero) {
                    Write-Host "    Export Verified: mpv_client_api_version found (0x$($export.ToString('X')))" -ForegroundColor Green
                } else {
                    Write-Host "    Export Warning:  mpv_client_api_version not found in module" -ForegroundColor Yellow
                }
            } elseif ($name -eq "libEGL.dll") {
                $export = [NativeLoader]::GetProcAddress($handle, "eglGetProcAddress")
                if ($export -ne [IntPtr]::Zero) {
                    Write-Host "    Export Verified: eglGetProcAddress found (0x$($export.ToString('X')))" -ForegroundColor Green
                }
            }

            [NativeLoader]::FreeLibrary($handle) | Out-Null
        } else {
            $lastErr = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
            Write-Host "    Load Status:     FAILED (Win32 Error: $lastErr)" -ForegroundColor Red
            $loadFailed = $true
        }
    }
}

Write-Host ""
if ($allFound -and -not $loadFailed) {
    Write-Host "[SUCCESS] All native binaries are present, loadable, and verified." -ForegroundColor Green
    exit 0
} else {
    Write-Host "[WARNING] One or more native binaries are missing or failed to load." -ForegroundColor Yellow
    exit 1
}
