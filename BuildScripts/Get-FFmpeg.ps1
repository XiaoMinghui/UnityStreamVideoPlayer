[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$lock = Get-Content (Join-Path $PSScriptRoot 'ffmpeg.lock.json') -Raw | ConvertFrom-Json
$cache = Join-Path $root 'ThirdParty~'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache 'ffmpeg-win64-lgpl-shared.zip'
if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest $lock.url -OutFile $archive }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $lock.sha256) {
    throw 'FFmpeg archive checksum mismatch. Remove the invalid archive and rerun.'
}
$destination = Join-Path $cache 'ffmpeg'
$sdk = Join-Path $destination $lock.directory
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'include/libavcodec/avcodec.h'))) {
    Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
}
$configuration = & (Join-Path $sdk 'bin/ffmpeg.exe') -version
if ($LASTEXITCODE -ne 0 -or ($configuration -match '--enable-gpl|--enable-nonfree')) {
    throw 'FFmpeg version/license configuration verification failed.'
}
$configuration | Set-Content (Join-Path $cache 'ffmpeg-build-configuration.txt') -Encoding utf8
Write-Output $sdk
