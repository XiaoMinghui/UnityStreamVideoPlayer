$ErrorActionPreference = 'Stop'
$recordPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'ThirdParty~/rtsp-processes.local.json'
if (-not (Test-Path $recordPath)) { return }
$records = Get-Content $recordPath -Raw | ConvertFrom-Json
$unmatched = @()
foreach ($record in $records) {
    $process = Get-Process -Id $record.id -ErrorAction SilentlyContinue
    if ($process -and $process.Path -eq $record.path -and
        $process.StartTime.ToUniversalTime().Ticks -eq [long]$record.startTicks) {
        Stop-Process -Id $process.Id -PassThru | Wait-Process -Timeout 10
    }
    elseif ($process) { $unmatched += $record }
}
if ($unmatched.Count -gt 0) {
    $unmatched | ConvertTo-Json | Set-Content $recordPath -Encoding utf8
    throw 'Some process identities did not match. They were not stopped; the record was retained for inspection.'
}
Remove-Item -LiteralPath $recordPath
