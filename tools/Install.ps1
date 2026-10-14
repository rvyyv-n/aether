<#
.SYNOPSIS
    Per-user installer for Aether.

.DESCRIPTION
    Installs Aether to the user's profile (%LOCALAPPDATA%\Programs\Aether).
    Requires zero Administrator rights, no test certificates, and no Developer Mode.
    Creates Start Menu and Desktop shortcuts, registers in Windows Installed Apps,
    and adds 'Play with Aether' shell context menu.

.PARAMETER TargetDir
    Installation directory. Defaults to %LOCALAPPDATA%\Programs\Aether.

.PARAMETER NoDesktopShortcut
    Skip creating the Desktop shortcut.

.PARAMETER NoLaunch
    Do not launch Aether after installation.

.PARAMETER Uninstall
    Uninstall Aether and clean up per-user shortcuts and registry entries.

.PARAMETER Quiet
    Run silently without console banners.
#>
[CmdletBinding()]
param(
    [string]$TargetDir = (Join-Path $env:LOCALAPPDATA "Programs\Aether"),
    [switch]$NoDesktopShortcut,
    [switch]$NoLaunch,
    [switch]$Uninstall,
    [switch]$Quiet
)

$AppName = "Aether"
$AppVersion = "1.0.0"
$MainExe = "Aether.App.exe"

function Write-Info([string]$msg) {
    if (-not $Quiet) { Write-Host $msg -ForegroundColor Cyan }
}
function Write-Success([string]$msg) {
    if (-not $Quiet) { Write-Host $msg -ForegroundColor Green }
}
function Write-Err([string]$msg) {
    Write-Host $msg -ForegroundColor Red
}

# --- UNINSTALL MODE ---
if ($Uninstall) {
    # Uninstall deletes the whole folder, so only touch one that holds Aether.
    if ((Test-Path $TargetDir) -and -not (Test-Path (Join-Path $TargetDir $MainExe))) {
        Write-Err "[Error] $TargetDir is not an $AppName install, so nothing was removed."
        exit 1
    }
    Write-Info "Uninstalling $AppName from $TargetDir..."

    # Kill running instances
    Get-Process -Name "Aether.App" -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -like "$TargetDir*" } catch { $false }
    } | Stop-Process -Force -ErrorAction SilentlyContinue

    # Remove Start Menu shortcut
    $startMenuLnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$AppName.lnk"
    if (Test-Path $startMenuLnk) { Remove-Item $startMenuLnk -Force -ErrorAction SilentlyContinue }

    # Remove Desktop shortcut
    $desktopLnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "$AppName.lnk"
    if (Test-Path $desktopLnk) { Remove-Item $desktopLnk -Force -ErrorAction SilentlyContinue }

    # Remove HKCU Registry keys
    Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\aether.exe" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "HKCU:\Software\Classes\SystemFileAssociations\video\shell\Aether" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "HKCU:\Software\Classes\Aether.VideoPlayer" -Recurse -Force -ErrorAction SilentlyContinue

    # Remove target directory
    if (Test-Path $TargetDir) {
        Remove-Item $TargetDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Success "[SUCCESS] $AppName has been completely uninstalled."
    return
}

# --- INSTALL MODE ---
if (-not $Quiet) {
    Write-Host "====================================================================" -ForegroundColor Cyan
    Write-Host "           $($AppName.ToUpper()) - STANDALONE PER-USER INSTALLER v$AppVersion" -ForegroundColor Cyan
    Write-Host "      Zero Administrator Privileges Required | %LOCALAPPDATA%       " -ForegroundColor Cyan
    Write-Host "====================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Target Installation Directory:"
    Write-Host "  $TargetDir" -ForegroundColor White
    Write-Host ""
}

# Locate source files (either beside this script, in dist/Aether-portable, or in build output)
$scriptDir = $PSScriptRoot
$sourceDir = $null

if (Test-Path (Join-Path $scriptDir $MainExe)) {
    $sourceDir = $scriptDir
} elseif (Test-Path (Join-Path $scriptDir "..\dist\Aether-portable\$MainExe")) {
    $sourceDir = Resolve-Path (Join-Path $scriptDir "..\dist\Aether-portable")
} elseif (Test-Path (Join-Path $scriptDir "..\src\Aether.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\$MainExe")) {
    $sourceDir = Resolve-Path (Join-Path $scriptDir "..\src\Aether.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64")
} elseif (Test-Path (Join-Path $scriptDir "..\src\Aether.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\$MainExe")) {
    $sourceDir = Resolve-Path (Join-Path $scriptDir "..\src\Aether.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64")
}

if (-not $sourceDir) {
    Write-Err "[Error] Could not locate application binaries. Build or publish Aether first."
    exit 1
}

if ((Test-Path $TargetDir) -and (Get-ChildItem $TargetDir -Force | Select-Object -First 1) -and -not (Test-Path (Join-Path $TargetDir $MainExe))) {
    Write-Err "[Error] $TargetDir already has other files in it. Choose an empty folder."
    exit 1
}

# Kill running instances before overwriting
Get-Process -Name "Aether.App" -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -like "$TargetDir*" } catch { $false }
} | Stop-Process -Force -ErrorAction SilentlyContinue

# 1. Copy files
Write-Info "[1/5] Deploying application files from $sourceDir..."
if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
}
Copy-Item "$sourceDir\*" -Destination $TargetDir -Recurse -Force

$installedExe = Join-Path $TargetDir $MainExe
$iconFile = Join-Path $TargetDir "Assets\AppIcon.ico"
if (-not (Test-Path $iconFile)) { $iconFile = $installedExe }

# 2. Start Menu shortcut
Write-Info "[2/5] Creating Start Menu shortcut..."
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$startMenuLnk = Join-Path $startMenuDir "$AppName.lnk"
$wshell = New-Object -ComObject WScript.Shell
$shortcut = $wshell.CreateShortcut($startMenuLnk)
$shortcut.TargetPath = $installedExe
$shortcut.WorkingDirectory = $TargetDir
$shortcut.Description = "$AppName Video Player"
$shortcut.IconLocation = "$iconFile,0"
$shortcut.Save()

# 3. Desktop shortcut
if (-not $NoDesktopShortcut) {
    Write-Info "[3/5] Creating Desktop shortcut..."
    $desktopDir = [Environment]::GetFolderPath("Desktop")
    $desktopLnk = Join-Path $desktopDir "$AppName.lnk"
    $dShortcut = $wshell.CreateShortcut($desktopLnk)
    $dShortcut.TargetPath = $installedExe
    $dShortcut.WorkingDirectory = $TargetDir
    $dShortcut.Description = "$AppName Video Player"
    $dShortcut.IconLocation = "$iconFile,0"
    $dShortcut.Save()
} else {
    Write-Info "[3/5] Skipping Desktop shortcut."
}

# 4. Windows Registry HKCU (Add/Remove Programs & App Paths)
Write-Info "[4/5] Registering in Windows (Installed Apps & Run)..."
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
if (-not (Test-Path $uninstallKey)) { New-Item -Path $uninstallKey -Force | Out-Null }
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value $AppName
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value $AppVersion
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "Aether Studio"
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value "$installedExe,0"
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $TargetDir
Set-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$TargetDir\Install.ps1`" -Uninstall -TargetDir `"$TargetDir`""
Set-ItemProperty -Path $uninstallKey -Name "QuietUninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$TargetDir\Install.ps1`" -Uninstall -TargetDir `"$TargetDir`" -Quiet"
Set-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -Type DWord

$appPathsKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\aether.exe"
if (-not (Test-Path $appPathsKey)) { New-Item -Path $appPathsKey -Force | Out-Null }
Set-ItemProperty -Path $appPathsKey -Name "(Default)" -Value $installedExe
Set-ItemProperty -Path $appPathsKey -Name "Path" -Value $TargetDir

# 5. Context menu for video files
Write-Info "[5/5] Registering shell context menus ('Play with Aether')..."
$contextMenuKey = "HKCU:\Software\Classes\SystemFileAssociations\video\shell\Aether"
if (-not (Test-Path $contextMenuKey)) { New-Item -Path $contextMenuKey -Force | Out-Null }
Set-ItemProperty -Path $contextMenuKey -Name "(Default)" -Value "Play with Aether"
Set-ItemProperty -Path $contextMenuKey -Name "Icon" -Value "`"$installedExe`",0"

$cmdKey = Join-Path $contextMenuKey "command"
if (-not (Test-Path $cmdKey)) { New-Item -Path $cmdKey -Force | Out-Null }
Set-ItemProperty -Path $cmdKey -Name "(Default)" -Value "`"$installedExe`" `"%1`""

# Copy installer script into target directory for uninstall support
Copy-Item $PSCommandPath -Destination (Join-Path $TargetDir "Install.ps1") -Force

Write-Success "`n[SUCCESS] $AppName has been installed successfully!"

if (-not $NoLaunch) {
    Write-Info "Launching $AppName..."
    Start-Process -FilePath $installedExe -WorkingDirectory $TargetDir
}
