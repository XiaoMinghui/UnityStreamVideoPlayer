[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PlayerPath, [switch]$Video)
$ErrorActionPreference = 'Stop'
$player = (Resolve-Path -LiteralPath $PlayerPath).Path
$directory = Split-Path $player -Parent
$runId = [guid]::NewGuid().ToString('N')
$resultPath = Join-Path $directory "smoke-$runId.json"
$logPath = Join-Path $directory "smoke-$runId.log"
$testFlag = if ($Video) { '-svp-video-test' } else { '-svp-smoke-test' }
$process = Start-Process -FilePath $player -ArgumentList @('-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '360', $testFlag, ('"' + $resultPath + '"'), '-logFile', ('"' + $logPath + '"')) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) {
    throw "Player verification timed out. Process $($process.Id) is still running. Inspect $logPath"
}
if (-not (Test-Path -LiteralPath $resultPath)) { throw "No verification report. Inspect $logPath" }
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$result | ConvertTo-Json
if (-not $result.success) { throw "Player GPU verification failed. Inspect $logPath" }
Write-Output "Player verification passed: $resultPath"
