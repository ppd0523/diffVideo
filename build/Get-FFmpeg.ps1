param(
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $projectRoot 'tools\ffmpeg'
}
$Destination = [System.IO.Path]::GetFullPath($Destination)

$version = 'n9.0.1-11-ge47273f4d9'
$archiveName = 'ffmpeg-n9.0.1-11-ge47273f4d9-win64-lgpl-9.0.zip'
$expectedHash = '14CE996102BCACCDC8DE62E404DD96C9E6EB4C7AE28A25EB3537817F1E4D60FD'
$url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-02-13-13/' + $archiveName
$downloadDirectory = Join-Path $projectRoot 'tools\downloads'
$archivePath = Join-Path $downloadDirectory $archiveName
$extractDirectory = Join-Path $projectRoot 'tools\ffmpeg-extract'

$ffmpegPath = Join-Path $Destination 'bin\ffmpeg.exe'
$ffprobePath = Join-Path $Destination 'bin\ffprobe.exe'
if ((Test-Path -LiteralPath $ffmpegPath) -and (Test-Path -LiteralPath $ffprobePath)) {
    $reportedVersion = & $ffmpegPath -version | Select-Object -First 1
    if ($reportedVersion -match [regex]::Escape($version)) {
        Write-Output "FFmpeg $version is already available at $Destination"
        exit 0
    }
}

New-Item -ItemType Directory -Force -Path $downloadDirectory | Out-Null
if (-not (Test-Path -LiteralPath $archivePath)) {
    Invoke-WebRequest -Uri $url -OutFile $archivePath
}

$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) {
    throw "FFmpeg archive checksum mismatch. Expected $expectedHash, got $actualHash."
}

$resolvedTools = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'tools'))
$resolvedExtract = [System.IO.Path]::GetFullPath($extractDirectory)
if (-not $resolvedExtract.StartsWith($resolvedTools, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe extraction path: $resolvedExtract"
}
if (Test-Path -LiteralPath $resolvedExtract) {
    Remove-Item -LiteralPath $resolvedExtract -Recurse -Force
}
Expand-Archive -LiteralPath $archivePath -DestinationPath $resolvedExtract
$packageRoot = Get-ChildItem -LiteralPath $resolvedExtract -Directory | Select-Object -First 1
if ($null -eq $packageRoot) {
    throw 'FFmpeg archive did not contain the expected package directory.'
}

New-Item -ItemType Directory -Force -Path (Join-Path $Destination 'bin') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Destination 'licenses') | Out-Null
Copy-Item -LiteralPath (Join-Path $packageRoot.FullName 'bin\ffmpeg.exe') -Destination $ffmpegPath -Force
Copy-Item -LiteralPath (Join-Path $packageRoot.FullName 'bin\ffprobe.exe') -Destination $ffprobePath -Force
Copy-Item -LiteralPath (Join-Path $packageRoot.FullName 'LICENSE.txt') -Destination (Join-Path $Destination 'licenses\FFmpeg-LICENSE.txt') -Force
Remove-Item -LiteralPath $resolvedExtract -Recurse -Force
Write-Output "Installed FFmpeg $version at $Destination"
