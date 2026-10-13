<#
.SYNOPSIS
    Locally hosts a video using Aether's Kestrel streaming server.

.DESCRIPTION
    Serves video on http://localhost:8443/watch with HTTP 206 range processing
    and generates instant mobile LAN streaming URLs.

.PARAMETER FilePath
    Path to video file (.mp4, .mkv, .mov, .webm) to stream.

.EXAMPLE
    .\tools\host.ps1 "D:\Videos\sample.mp4"
#>
param(
    [Parameter(Position = 0)]
    [string]$FilePath
)

$runScript = Join-Path $PSScriptRoot "run.ps1"
if ($FilePath) {
    & $runScript -Host -FilePath $FilePath
} else {
    & $runScript -Host
}
