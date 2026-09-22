param([string]$DotnetPath = 'dotnet')

$ErrorActionPreference = 'Stop'
$previousSources = $env:RestoreSources
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('DiffVideo-offline-' + [guid]::NewGuid())
function Invoke-WebRequest { throw 'Simulated offline download failure' }
try {
    # Unreachable package source: the real build must work from its existing cache.
    $env:RestoreSources = 'https://127.0.0.1:1/nuget'
    & (Join-Path $PSScriptRoot 'Publish-Portable.ps1') -DotnetPath $DotnetPath

    # No installed FFmpeg or cached ZIP: download must be attempted before failing.
    $testBuild = New-Item -ItemType Directory -Path (Join-Path $testRoot 'build')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Get-FFmpeg.ps1') -Destination $testBuild.FullName
    $failedAsExpected = $false
    try { & (Join-Path $testBuild.FullName 'Get-FFmpeg.ps1') }
    catch {
        if ($_.Exception.Message -notlike '*unavailable locally and download failed: Simulated offline download failure*') { throw }
        $failedAsExpected = $true
    }
    if (-not $failedAsExpected) { throw 'Missing FFmpeg did not fail after attempting download.' }
    if (Get-ChildItem -LiteralPath $testRoot -Filter '*.partial' -Recurse) { throw 'Partial download was left behind.' }
    Write-Output 'PASS: cached offline build; missing FFmpeg attempts download and then fails.'
}
finally {
    $env:RestoreSources = $previousSources
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTestRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTestRoot)) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}
