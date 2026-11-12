<#
.SYNOPSIS
    Downloads libmpv and ANGLE into src/Aether.App/Native/x64, so playback and the real-engine tests run.

.DESCRIPTION
    Fetches native-deps.zip from the native-deps-1 release, checks the same pinned SHA-256 as
    .github/workflows/release.yml, and unpacks it. If the engine files change, update the tag and the
    hash here and in release.yml together.

.EXAMPLE
    .\tools\fetch-native-deps.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$url = "https://github.com/rvyyv-n/aether/releases/download/native-deps-1/native-deps.zip"
$expected = "6572A522CFE8FC39E7420C4AF9FCAF6990C44D17DD03EEBC05E97021ED2DAA00"
$dest = Join-Path $PSScriptRoot "..\src\Aether.App\Native\x64"

$tmp = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
New-Item -ItemType Directory $tmp | Out-Null
try {
    $zip = Join-Path $tmp "native-deps.zip"
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $expected) { throw "native-deps.zip does not match the pinned SHA-256." }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Expand-Archive $zip -DestinationPath $dest -Force
} finally {
    Remove-Item -Recurse -Force $tmp
}
Get-ChildItem $dest -File | ForEach-Object { "$($_.Name) $($_.Length)" }
