[CmdletBinding()]
param(
    [string]$UnityEditorPath,
    [ValidateSet('Debug', 'Release', 'RelWithDebInfo')][string]$Configuration = 'RelWithDebInfo',
    [switch]$SkipInstall,
    [switch]$WithVideo,
    [string]$CMakePath
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent

if (-not $UnityEditorPath) {
    $versionLine = Get-Content (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1
    $version = ($versionLine -split ':', 2)[1].Trim()
    $UnityEditorPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw 'Unity Editor not found. Pass -UnityEditorPath with the full Unity.exe path.'
}
$pluginApi = Join-Path (Split-Path $UnityEditorPath -Parent) 'Data/PluginAPI'

if (-not $CMakePath) {
    $cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
    if ($cmakeCommand) { $CMakePath = $cmakeCommand.Source }
    else {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
        if (Test-Path -LiteralPath $vswhere) {
            $vsPath = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
            if ($vsPath) { $CMakePath = Join-Path $vsPath 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' }
        }
    }
}
if (-not $CMakePath -or -not (Test-Path -LiteralPath $CMakePath)) {
    throw 'CMake not found. Install Visual Studio Desktop development with C++ including CMake, or pass -CMakePath.'
}
$buildDir = Join-Path $projectRoot 'NativeSource/build-windows-x64'
$videoArguments = @('-DSVP_BUILD_VIDEO=OFF')
if ($WithVideo) {
    $ffmpegRoot = & (Join-Path $PSScriptRoot 'Get-FFmpeg.ps1')
    $videoArguments = @('-DSVP_BUILD_VIDEO=ON', "-DFFMPEG_ROOT=$ffmpegRoot")
}
& $CMakePath -S (Join-Path $projectRoot 'NativeSource') -B $buildDir -G 'Visual Studio 17 2022' -A x64 "-DUNITY_PLUGIN_API_DIR=$pluginApi" -DBUILD_TESTING=ON @videoArguments
if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed.' }
& $CMakePath --build $buildDir --config $Configuration --parallel
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
$ctest = Join-Path (Split-Path $CMakePath -Parent) 'ctest.exe'
& $ctest --test-dir $buildDir -C $Configuration --output-on-failure --timeout 30
if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }

if (-not $SkipInstall) {
    $sources = @(Join-Path $buildDir "$Configuration/StreamVideoPlayerNative.dll")
    if ($WithVideo) {
        $sources += Join-Path $buildDir "$Configuration/StreamVideoPlayerFFmpeg.dll"
        $sources += (Get-ChildItem (Join-Path $ffmpegRoot 'bin') -Filter '*.dll').FullName
    }
    $pluginFolder = Join-Path $projectRoot 'Packages/com.xmh.streamvideoplayer/Runtime/Plugins/Windows/x86_64'
    $template = Get-Content (Join-Path $PSScriptRoot 'StreamVideoPlayerNative.dll.meta.template') -Raw
    try {
        foreach ($source in $sources) {
            $name = Split-Path $source -Leaf
            $destination = Join-Path $pluginFolder $name
            if (-not (Test-Path -LiteralPath ($destination + '.meta'))) {
                $sha = [System.Security.Cryptography.SHA256]::Create()
                try { $guid = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes('com.xmh.streamvideoplayer/' + $name)))).Replace('-', '').Substring(0,32).ToLowerInvariant() }
                finally { $sha.Dispose() }
                if ($name -eq 'StreamVideoPlayerNative.dll') { $guid = '067f437014524f6a905b2c3bb9b8668b' }
                ($template -replace '067f437014524f6a905b2c3bb9b8668b', $guid) | Set-Content ($destination + '.meta') -Encoding utf8
            }
            if ((Test-Path -LiteralPath $destination) -and
                (Get-FileHash $source).Hash -eq (Get-FileHash $destination).Hash) { continue }
            Copy-Item -LiteralPath $source -Destination $destination -Force
            Write-Output "Installed: $name"
        }
    }
    catch { throw "Cannot install DLL. Close the Unity Editor/Player using this plugin, then rerun. $($_.Exception.Message)" }
}
