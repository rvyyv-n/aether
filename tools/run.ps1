<#
.SYNOPSIS
    Fast developer launcher for Aether.

.DESCRIPTION
    Builds (incrementally in ~2-3s) and launches the Aether app without needing
    full MSIX packaging, installer scripts, or Release AOT compilation.
    Also supports locally hosting video streams via Kestrel with -Host.

.PARAMETER FilePath
    Optional path to a video file (.mp4, .mkv, .mov, .webm) to play immediately.

.PARAMETER NoBuild
    Skip building and launch the existing build immediately.

.PARAMETER HostServer
    Run the local Kestrel LAN streaming server instead of the desktop player.

.PARAMETER Rebuild
    Clean and perform a full rebuild before launching.

.PARAMETER Configuration
    Build configuration: Debug (default) or Release.

.PARAMETER Platform
    Target architecture: x64 (default).

.EXAMPLE
    .\tools\run.ps1
    # Incrementally builds and launches the desktop app.

.EXAMPLE
    .\tools\run.ps1 -FilePath "C:\Videos\clip.mp4"
    # Builds and launches Aether directly opening clip.mp4.

.EXAMPLE
    .\tools\run.ps1 -NoBuild
    # Instantly launches without rebuilding.

.EXAMPLE
    .\tools\run.ps1 -Host "C:\Videos\clip.mp4"
    # Locally hosts clip.mp4 via Kestrel on http://localhost:8443/watch
#>
[CmdletBinding(DefaultParameterSetName = "Desktop")]
param(
    [Parameter(Position = 0)]
    [string]$FilePath,

    [Parameter(ParameterSetName = "Desktop")]
    [switch]$NoBuild,

    [Parameter(ParameterSetName = "Desktop")]
    [switch]$Rebuild,

    [Parameter(ParameterSetName = "Host")]
    [Alias("Host", "Serve")]
    [switch]$HostServer,

    [string]$Configuration = "Debug",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$AppProject = Join-Path $RepoRoot "src\Aether.App\Aether.App.csproj"
$DevHostProject = Join-Path $RepoRoot "tools\dev-host\Aether.DevHost.csproj"

# --- MODE 1: Locally Host Stream Server ---
if ($HostServer) {
    Write-Host "[Aether Dev] Starting local Kestrel streaming host..." -ForegroundColor Cyan
    $hostArgs = @("run", "--project", $DevHostProject, "-c", $Configuration, "--no-restore", "-p:NuGetAudit=false")
    if ($FilePath) {
        $resolvedVideo = Resolve-Path $FilePath
        $hostArgs += "--"
        $hostArgs += $resolvedVideo.Path
    }
    & dotnet @hostArgs
    exit $LASTEXITCODE
}

# --- MODE 2: Run Desktop Player ---
$OutputDir = Join-Path $RepoRoot "src\Aether.App\bin\$Platform\$Configuration\net10.0-windows10.0.26100.0\win-$Platform"
$ExePath = Join-Path $OutputDir "Aether.App.exe"

if ($Rebuild) {
    Write-Host "[Aether Dev] Cleaning obj and bin..." -ForegroundColor Yellow
    Remove-Item (Join-Path $RepoRoot "src\Aether.App\obj") -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $NoBuild) {
    Write-Host "[Aether Dev] Building Aether.App ($Platform $Configuration)..." -ForegroundColor Cyan
    $buildStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    
    $buildArgs = @(
        "build", $AppProject,
        "-p:Platform=$Platform",
        "-c", $Configuration,
        "--no-restore",
        "-warnaserror",
        "-p:NuGetAudit=false"
    )

    $buildOutput = & dotnet @buildArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        # If restore is required, attempt restore from local nuget cache
        Write-Host "[Aether Dev] Fast build failed, attempting local restore..." -ForegroundColor Yellow
        $nugetPath = Join-Path $env:USERPROFILE ".nuget\packages"
        & dotnet restore $AppProject -p:Platform=$Platform --source $nugetPath -p:NuGetAudit=false | Out-Null
        $buildOutput = & dotnet @buildArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            $buildOutput | ForEach-Object { Write-Host $_ }
            Write-Error "[Aether Dev] Build failed."
            exit $LASTEXITCODE
        }
    }
    $buildStopwatch.Stop()
    Write-Host "[Aether Dev] Build succeeded in $($buildStopwatch.Elapsed.TotalSeconds.ToString('0.00'))s" -ForegroundColor Green
}

# Ensure the executable exists
if (-not (Test-Path $ExePath)) {
    Write-Error "[Aether Dev] Aether.App.exe not found at $ExePath. Build first."
    exit 1
}

# Launch the app directly as an unpackaged desktop application (no Developer Mode or certificates required)
Write-Host "[Aether Dev] Launching Aether (unpackaged standalone)..." -ForegroundColor Cyan

if ($FilePath) {
    $resolvedPath = (Resolve-Path $FilePath).Path
    Write-Host "[Aether Dev] Opening video: $resolvedPath" -ForegroundColor Cyan
    Start-Process -FilePath $ExePath -ArgumentList "`"$resolvedPath`""
} else {
    Start-Process -FilePath $ExePath
}

Start-Sleep -Milliseconds 800
$proc = Get-Process -Name "Aether.App" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($proc) {
    Write-Host "[Aether Dev] Aether is running (PID: $($proc.Id))." -ForegroundColor Green
} else {
    Write-Host "[Aether Dev] Aether launched." -ForegroundColor Green
}
