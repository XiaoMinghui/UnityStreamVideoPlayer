[CmdletBinding()]
param(
    [string]$Source = 'rtsp://127.0.0.1:18554/test',
    [int[]]$Counts = @(1,4,9,16),
    [ValidateRange(2,300)][int]$Seconds = 15,
    [switch]$Probe,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'NativeSource/build-windows-x64/RelWithDebInfo/VideoBenchmark.exe'
if (-not (Test-Path $exe)) { throw 'Build first: BuildScripts/Build-Native.ps1 -WithVideo -SkipInstall' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root ('Artifacts/benchmark-' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$mode = if ($Probe) { @('probe') } else { @() }
$rows = @()
foreach ($count in $Counts) {
    if ($count -lt 1 -or $count -gt 32) { throw 'Session count must be 1..32.' }
    $path = Join-Path $OutputDirectory "native-$count.json"
    & $exe $Source $count $Seconds $path @mode
    if ($LASTEXITCODE -ne 0) { throw "Native benchmark failed for $count sessions; inspect the diagnostic above." }
    $report = Get-Content $path -Raw | ConvertFrom-Json
    $rows += [pscustomobject]@{
        Sessions = $count
        MinDisplayedFps = ($report.streams.presentedFps | Measure-Object -Minimum).Minimum
        MaxDisplayedFps = ($report.streams.presentedFps | Measure-Object -Maximum).Maximum
        CpuPercentOfMachine = $report.processCpuPercentOfMachine
        WorkingSetMB = $report.peakWorkingSetMB
        ProcessLocalVideoMemoryMB = $report.peakProcessLocalVideoMemoryMB
        PresentLoopP95Ms = $report.presentLoopCpuP95Ms
        StopAllMs = $report.stopAllMs
    }
}
$rows | Export-Csv (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation -Encoding utf8
$rows | Format-Table -AutoSize
Write-Output "Reports: $OutputDirectory (native pipeline only; not Unity frame rate or glass-to-glass latency)."
