<#
.SYNOPSIS
    Runs the FlaUI (UIA3) end-to-end UI automation suite against the unpackaged Aether.App.exe.

.DESCRIPTION
    Builds Aether.App and Aether.App.UiTests (unless -NoBuild is set), runs sequential FlaUI UI tests
    with TRX result logging, and unconditionally terminates any stray Aether.App or Aether.DevHost
    processes before exiting.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [switch]$NoBuild,
    [string]$ResultsDir = "TestResults"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

$rid = if ($Platform -eq "x86") { "win-x86" } else { "win-x64" }
$exePath = Join-Path $repoRoot "src\Aether.App\bin\$Platform\$Configuration\net10.0-windows10.0.26100.0\$rid\Aether.App.exe"
$resultsPath = if ([IO.Path]::IsPathRooted($ResultsDir)) { $ResultsDir } else { Join-Path $repoRoot $ResultsDir }

function Stop-AetherProcesses {
    Get-Process Aether.App, Aether.DevHost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

try {
    Stop-AetherProcesses

    if (-not $NoBuild) {
        Write-Host "Building AetherPlayer.sln ($Configuration|$Platform)..."
        dotnet build AetherPlayer.sln -c $Configuration -p:Platform=$Platform -warnaserror -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    if (-not (Test-Path $exePath)) {
        throw "Compiled Aether.App.exe not found at '$exePath'."
    }

    New-Item -ItemType Directory -Force $resultsPath | Out-Null
    $env:AETHER_UI_TEST_EXE = $exePath

    Write-Host "Running FlaUI UI automation suite against $exePath..."
    dotnet test tests\Aether.App.UiTests\Aether.App.UiTests.csproj `
        -c $Configuration `
        --no-build `
        --results-directory $resultsPath `
        --logger "trx;LogFileName=ui-tests.trx" `
        --logger "console;verbosity=normal"
    exit $LASTEXITCODE
}
finally {
    Stop-AetherProcesses
    Pop-Location
}
