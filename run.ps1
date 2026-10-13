<#
.SYNOPSIS
    Convenience shortcut to .\tools\run.ps1
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

$toolScript = Join-Path $PSScriptRoot "tools\run.ps1"
$forwardArgs = @{}
if ($FilePath) { $forwardArgs["FilePath"] = $FilePath }
if ($NoBuild) { $forwardArgs["NoBuild"] = $true }
if ($Rebuild) { $forwardArgs["Rebuild"] = $true }
if ($HostServer) { $forwardArgs["HostServer"] = $true }
if ($Configuration) { $forwardArgs["Configuration"] = $Configuration }
if ($Platform) { $forwardArgs["Platform"] = $Platform }

& $toolScript @forwardArgs
