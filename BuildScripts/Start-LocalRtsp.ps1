[CmdletBinding()]
param(
    [ValidateRange(160,3840)][int]$Width = 1280,
    [ValidateRange(90,2160)][int]$Height = 720,
    [ValidateRange(1,60)][int]$FrameRate = 25,
    [ValidateRange(1,250)][int]$Gop = 25
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root 'ThirdParty~'
$sdk = & (Join-Path $PSScriptRoot 'Get-FFmpeg.ps1')
$ffmpeg = Join-Path $sdk 'bin/ffmpeg.exe'
if ($Width % 2 -ne 0 -or $Height % 2 -ne 0) { throw 'H.264 4:2:0 fixture dimensions must be even.' }
$archive = Join-Path $cache 'mediamtx.zip'
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest 'https://github.com/bluenviron/mediamtx/releases/download/v1.21.2/mediamtx_v1.21.2_windows_amd64.zip' -OutFile $archive
}
if ((Get-FileHash $archive).Hash -ne 'a84224f5a490855684e62ba6fb71e80ea2a5e16aed61883bb58380290e93e467') { throw 'MediaMTX checksum mismatch.' }
$serverRoot = Join-Path $cache 'mediamtx'
if (-not (Test-Path (Join-Path $serverRoot 'mediamtx.exe'))) { Expand-Archive -LiteralPath $archive -DestinationPath $serverRoot }
$config = Join-Path $cache 'mediamtx-local.yml'
@'
logLevel: warn
rtspAddress: 127.0.0.1:18554
rtspTransports: [tcp]
rtmp: false
hls: false
webrtc: false
srt: false
moq: false
paths:
  test:
'@ | Set-Content $config -Encoding utf8
$fixture = Join-Path $cache "test-h264-${Width}x${Height}-${FrameRate}fps-gop${Gop}.mp4"
if (-not (Test-Path $fixture)) {
    & $ffmpeg -hide_banner -loglevel error -f lavfi -i "testsrc2=size=${Width}x${Height}:rate=$FrameRate" -t 16 -c:v libopenh264 -b:v 6000k -g $Gop -bf 0 -colorspace bt709 -color_primaries bt709 -color_trc bt709 -y $fixture
    if ($LASTEXITCODE -ne 0) { throw 'Could not generate the H.264 test fixture.' }
}
if (Get-NetTCPConnection -LocalPort 18554 -State Listen -ErrorAction SilentlyContinue) { throw 'Port 18554 is occupied. Stop the existing fixture before starting another.' }
$server = Start-Process (Join-Path $serverRoot 'mediamtx.exe') -ArgumentList ('"' + $config + '"') -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $cache 'mediamtx-error.log') -RedirectStandardOutput (Join-Path $cache 'mediamtx.log')
Start-Sleep -Milliseconds 600
if ($server.HasExited) { throw 'Local RTSP server failed. See ThirdParty~/mediamtx-error.log.' }
$publisher = Start-Process $ffmpeg -ArgumentList @('-hide_banner','-loglevel','error','-re','-stream_loop','-1','-i',('"' + $fixture + '"'),'-c:v','copy','-an','-f','rtsp','-rtsp_transport','tcp','rtsp://127.0.0.1:18554/test') -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $cache 'publisher-error.log')
$record = @(
    @{id=$server.Id; path=$server.Path; startTicks=$server.StartTime.ToUniversalTime().Ticks},
    @{id=$publisher.Id; path=$publisher.Path; startTicks=$publisher.StartTime.ToUniversalTime().Ticks}
)
$record | ConvertTo-Json | Set-Content (Join-Path $cache 'rtsp-processes.local.json') -Encoding utf8
Write-Output "Local fixture: rtsp://127.0.0.1:18554/test (H.264 ${Width}x${Height} ${FrameRate}fps, GOP $Gop). Stop with BuildScripts/Stop-LocalRtsp.ps1."
