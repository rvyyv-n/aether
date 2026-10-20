<#
.SYNOPSIS
    Measures how fast Aether opens and how many frames it drops while playing a video.

.DESCRIPTION
    Starts Aether several times with the AETHER_PERF_LOG environment variable set, so the app writes its own timings to a
    file, then closes it the normal way. Nothing in your settings changes. Close every Aether window before running it.

    Cold start: the time from the process starting until the window has drawn its first frame. The target is under 300 ms.
    The first run is usually slower (Windows still has to read the files from disk), so the median of the other runs is
    the number to judge.

    Playback (only with -Video): plays the video for -Seconds, then reports the frames mpv dropped, the frames Aether drew
    and the garbage collector's pauses. Dropped frames should stay at 0 on a smooth run; a few at the very start (while
    the video loads) are normal.

.EXAMPLE
    .\tools\measure-performance.ps1 -Video "D:\Videos\clip-120fps.mp4"
#>
param(
    # Aether.App.exe to measure. Defaults to the installed copy, then the portable build in dist.
    [string]$Exe,
    # A video to play for the dropped-frame check. Leave out to measure start-up only.
    [string]$Video,
    [int]$Runs = 5,
    [int]$Seconds = 30
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if (-not $Exe) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'AetherPlayer\current\Aether.App.exe'),
        (Join-Path $repo 'dist\Aether-portable\Aether.App.exe')
    )
    $Exe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $Exe) {
        throw 'No Aether build found. Install Aether, run tools\package-standalone.ps1, or pass -Exe <path to Aether.App.exe>.'
    }
}
if (-not (Test-Path $Exe)) { throw "Not found: $Exe" }
if ($Video -and -not (Test-Path $Video)) { throw "Video not found: $Video" }
if (Get-Process Aether.App -ErrorAction SilentlyContinue) {
    throw 'Aether is already running. Close it first: a second launch would hand over to the open window and measure nothing.'
}

function Read-Log([string]$path) {
    $values = @{}
    if (Test-Path $path) {
        foreach ($line in Get-Content $path) {
            $key, $value = $line -split '=', 2
            if ($key -and -not $values.ContainsKey($key)) { $values[$key] = $value }
        }
    }
    return $values
}

function Invoke-Aether([string[]]$arguments, [string]$waitFor, [int]$holdSeconds) {
    $log = Join-Path ([IO.Path]::GetTempPath()) ("aether-perf-" + [guid]::NewGuid().ToString('N') + '.log')
    $env:AETHER_PERF_LOG = $log
    try {
        $process = if ($arguments) { Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru } else { Start-Process -FilePath $Exe -PassThru }
    }
    finally {
        Remove-Item Env:AETHER_PERF_LOG
    }

    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Read-Log $log).ContainsKey($waitFor)) {
        if ((Get-Date) -gt $deadline -or $process.HasExited) { break }
        Start-Sleep -Milliseconds 50
    }
    if ($holdSeconds -gt 0) { Start-Sleep -Seconds $holdSeconds }

    # A normal close, so the app writes its session totals on the way out.
    $process.Refresh()
    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(10000)) {
        Stop-Process -Id $process.Id -Force
    }
    Start-Sleep -Milliseconds 300
    $values = Read-Log $log
    Remove-Item $log -ErrorAction SilentlyContinue
    return $values
}

Write-Host "Measuring $Exe"
Write-Host ''
Write-Host "Cold start ($Runs runs, no video)"
$shown = @()
for ($i = 1; $i -le $Runs; $i++) {
    $run = Invoke-Aether @() 'window_shown_ms' 1
    if (-not $run.ContainsKey('window_shown_ms')) {
        Write-Host "  run $i : no timing written (the window did not appear within 30 s)"
        continue
    }
    $ms = [int]$run['window_shown_ms']
    $shown += $ms
    $note = if ($i -eq 1) { '  (first run: files may still be coming off the disk)' } else { '' }
    Write-Host ("  run {0}: window shown after {1} ms (Main reached at {2} ms){3}" -f $i, $ms, $run['main_ms'], $note)
    if ($i -eq 1) {
        Write-Host ("         collector: server={0}, latency mode={1}" -f $run['gc_server'], $run['gc_latency_mode'])
    }
}
$warm = if ($shown.Count -gt 1) { $shown | Select-Object -Skip 1 } else { $shown }
if ($warm.Count -gt 0) {
    $sorted = $warm | Sort-Object
    $median = $sorted[[int][Math]::Floor(($sorted.Count - 1) / 2)]
    $verdict = if ($median -lt 300) { 'meets the 300 ms target' } else { 'SLOWER than the 300 ms target' }
    Write-Host ("  median after the first run: {0} ms, {1}" -f $median, $verdict)
}

if ($Video) {
    Write-Host ''
    Write-Host "Playback ($Seconds s of $(Split-Path -Leaf $Video))"
    $run = Invoke-Aether @("`"$Video`"") 'engine_ready_ms' $Seconds
    if (-not $run.ContainsKey('dropped_frames')) {
        Write-Host '  no session totals written (Aether did not close normally)'
    }
    else {
        Write-Host ("  dropped by the video output: {0}" -f $run['dropped_frames'])
        Write-Host ("  dropped by the decoder:      {0}" -f $run['decoder_dropped_frames'])
        Write-Host ("  frames drawn by Aether:      {0} (display {1} Hz)" -f $run['frames_presented'], $run['display_fps'])
        Write-Host ("  garbage collections:         {0} quick, {1} medium, {2} full; {3} ms paused in total" -f `
            $run['gc_gen0'], $run['gc_gen1'], $run['gc_gen2'], $run['gc_pause_total_ms'])
    }
}
