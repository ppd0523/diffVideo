param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\src\DiffVideo.App\bin\Release\net10.0-windows\win-x64\DiffVideo.exe'),
    [ValidateSet('ui', 'checks', 'features', 'roi')][string]$Mode = 'ui'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$inputs = @('motion-one-80s.mp4', 'motion-two-80s.mp4', 'music-80s.mp3') | ForEach-Object {
    $mediaPath = Join-Path $projectRoot "artifacts\preview-validation\$_"
    if (-not (Test-Path -LiteralPath $mediaPath)) { throw 'Generate test media first with build/Test-PlayerPreview.ps1 -GenerateOnly.' }
    $mediaPath
}
$reportPath = Join-Path $projectRoot "artifacts\ui-validation\design-$Mode.json"
$arguments = @('--preview-diagnostics', $Mode) + @($inputs | ForEach-Object { '"' + $_ + '"' }) + @(('"' + $reportPath + '"'), '5')
$process = Start-Process -FilePath ([IO.Path]::GetFullPath($ExecutablePath)) -ArgumentList $arguments -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit(55000)) { throw "UI diagnostics timed out: $Mode" }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "UI diagnostics exited with code $($process.ExitCode). Check $reportPath and the DiffVideo crash log." }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (-not $report.Success) { throw ($report | ConvertTo-Json -Depth 5) }
    $report | ConvertTo-Json -Depth 5
}
finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $process.Dispose()
}
