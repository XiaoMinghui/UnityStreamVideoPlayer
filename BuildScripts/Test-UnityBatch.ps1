[CmdletBinding()]
param([string]$UnityEditorPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $UnityEditorPath) {
    $version = ((Get-Content (Join-Path $root 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1) -split ':',2)[1].Trim()
    $UnityEditorPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
}
if (Test-Path (Join-Path $root 'Temp/UnityLockfile')) { throw 'Close this project in Unity before running batch tests.' }
$artifacts = Join-Path $root 'Artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$run = [guid]::NewGuid().ToString('N')
$results = Join-Path $artifacts "unity-tests-$run.xml"
$log = Join-Path $artifacts "unity-tests-$run.log"
$oldSource = $env:SVP_TEST_RTSP_URL
try {
    if (-not $env:SVP_TEST_RTSP_URL) { $env:SVP_TEST_RTSP_URL = 'rtsp://127.0.0.1:18554/test' }
    $process = Start-Process $UnityEditorPath -ArgumentList @('-batchmode','-force-d3d11','-projectPath',('"'+$root+'"'),'-runTests','-testPlatform','PlayMode','-assemblyNames','StreamVideoPlayer.Tests.PlayMode','-testResults',('"'+$results+'"'),'-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
}
finally { $env:SVP_TEST_RTSP_URL = $oldSource }
if (-not $process.WaitForExit(180000)) { throw "Unity test timeout; process $($process.Id) is still running. Inspect $log" }
if (-not (Test-Path $results)) { throw "Unity produced no results. Inspect $log" }
[xml]$xml = Get-Content $results -Raw
$summary = $xml.'test-run'
[pscustomobject]@{ Result=$summary.result; Passed=$summary.passed; Failed=$summary.failed; Skipped=$summary.skipped; Results=$results } | Format-List
if ($process.ExitCode -ne 0 -or [int]$summary.failed -gt 0 -or [int]$summary.passed -lt 7 -or [int]$summary.skipped -gt 0) { throw "Unity tests did not fully pass. Inspect $results and $log" }
