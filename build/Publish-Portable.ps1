param(
    [string]$DotnetPath = 'dotnet',
    [ValidatePattern('^DiffVideo(?:-[0-9]+\.[0-9]+\.[0-9]+)?-win-x64$')]
    [string]$PackageName = 'DiffVideo-win-x64',
    [switch]$SkipTests,
    [switch]$RunGuiSmokeTest
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$publishDirectory = Join-Path $artifactsRoot $PackageName
$zipPath = Join-Path $artifactsRoot "$PackageName-portable.zip"

& (Join-Path $PSScriptRoot 'Get-FFmpeg.ps1')
$env:DIFFVIDEO_FFMPEG_BIN = Join-Path $projectRoot 'tools\ffmpeg\bin'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

# Restore uses cached packages first; unavailable feeds are tolerated, missing packages still fail.
# Skip the online vulnerability audit for this portable build so cached builds work offline.
if (-not $SkipTests) {
    & $DotnetPath test (Join-Path $projectRoot 'DiffVideo.slnx') -c Release -p:RestoreIgnoreFailedSources=true -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

$resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactsRoot)
if (-not $resolvedArtifacts.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe artifacts path: $resolvedArtifacts"
}
# Check locks before deleting any package content. A running app must retain its FFmpeg and docs.
foreach ($candidate in @('DiffVideo.exe', 'ffmpeg.exe', 'ffprobe.exe')) {
    $candidatePath = Join-Path $publishDirectory $candidate
    if (Test-Path -LiteralPath $candidatePath) {
        try {
            $lockCheck = [IO.File]::Open($candidatePath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $lockCheck.Dispose()
        }
        catch { throw "Package file is in use; close the application or choose a new -PackageName. No package files were removed: $candidatePath" }
    }
}
if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

& $DotnetPath publish (Join-Path $projectRoot 'src\DiffVideo.App\DiffVideo.App.csproj') -c Release -r win-x64 --self-contained true -o $publishDirectory -p:RestoreIgnoreFailedSources=true -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

if ($RunGuiSmokeTest) {
    & (Join-Path $PSScriptRoot 'Test-GuiStartup.ps1') -ExecutablePath (Join-Path $publishDirectory 'DiffVideo.exe')
}

$archiveCreated = $false
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    try {
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }

        Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
        $archiveCreated = $true
        break
    }
    catch {
        if ($attempt -eq 19) {
            throw
        }

        Start-Sleep -Milliseconds 500
    }
}

if (-not $archiveCreated) { throw 'Portable archive was not created.' }
Write-Output $zipPath
