[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PlayerPath,
    [int[]]$Counts = @(1,4,9,16),
    [ValidateRange(2,300)][int]$Seconds = 15,
    [string]$OutputDirectory,
    [string]$NativePluginPath,
    [switch]$IndividualRendering
)
$ErrorActionPreference = 'Stop'
$player = (Resolve-Path -LiteralPath $PlayerPath).Path
$directory = Split-Path $player -Parent
if ($NativePluginPath) {
    # Allows validating a newly built native library without replacing the DLL locked by the Editor.
    $plugin = (Resolve-Path -LiteralPath $NativePluginPath).Path
    $targetDirectory = Join-Path $directory (([IO.Path]::GetFileNameWithoutExtension($player)) + '_Data/Plugins/x86_64')
    if (-not (Test-Path -LiteralPath $targetDirectory -PathType Container)) { throw 'Expected Windows Unity Player plugin directory is missing.' }
    Copy-Item -LiteralPath $plugin -Destination (Join-Path $targetDirectory 'StreamVideoPlayerFFmpeg.dll') -Force
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $directory ('wall-' + [guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$rows = @()
$renderArguments = if ($IndividualRendering) { @('-svp-individual-render') } else { @() }
foreach ($count in $Counts) {
    if ($count -lt 1 -or $count -gt 16) { throw 'Session count must be 1..16.' }
    $runId = [guid]::NewGuid().ToString('N')
    $resultPath = Join-Path $OutputDirectory "wall-$count-$runId.json"
    $logPath = Join-Path $OutputDirectory "wall-$count-$runId.log"
    $process = Start-Process -FilePath $player -ArgumentList (@('-force-d3d11','-screen-fullscreen','0','-screen-width','1920','-screen-height','1080','-svp-wall-benchmark',('"'+$resultPath+'"'),'-svp-count',$count,'-svp-seconds',$Seconds,'-logFile',('"'+$logPath+'"')) + $renderArguments) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(($Seconds + 60) * 1000)) { throw "Player verification timed out. Process $($process.Id) is still running; inspect $logPath" }
    if (-not (Test-Path $resultPath)) { throw "Player wrote no report; inspect $logPath" }
    $report = Get-Content $resultPath -Raw | ConvertFrom-Json
    if (-not $report.success -or $process.ExitCode -ne 0) { throw "Player benchmark failed: $($report.error). Inspect $logPath" }
    $rows += [pscustomobject]@{
        Sessions=$count; UnityFps=[math]::Round($report.unityFps,2); FrameP95Ms=[math]::Round($report.frameP95Ms,2)
        MinVideoFps=[math]::Round(($report.streams.presentedFps | Measure-Object -Minimum).Minimum,2)
        MaxVideoFps=[math]::Round(($report.streams.presentedFps | Measure-Object -Maximum).Maximum,2)
        CpuPercentOfMachine=[math]::Round($report.processCpuPercentOfMachine,2)
        WorkingSetMB=[math]::Round($report.peakWorkingSetMB,1); StopAllMs=[math]::Round($report.stopAllMs,1)
        RenderSubmitMeanUs=[math]::Round($report.renderSubmissionMeanUs,2)
    }
    Write-Output "Completed Unity video wall: $count sessions."
}
$rows | Export-Csv (Join-Path $OutputDirectory 'summary.csv') -NoTypeInformation -Encoding utf8
$rows | Format-Table -AutoSize
Write-Output "Reports: $OutputDirectory. Test is local RTSP; no glass-to-glass latency measurement."
