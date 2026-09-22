#requires -Version 7.0
param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\src\DiffVideo.App\bin\Release\net10.0-windows\win-x64\DiffVideo.exe'),
    [int]$Seconds = 60,
    [switch]$GenerateOnly,
    [switch]$ChecksOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$validationRoot = Join-Path $projectRoot 'artifacts\preview-validation'
$ffmpeg = Join-Path $projectRoot 'tools\ffmpeg\bin\ffmpeg.exe'
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null
$videoOne = Join-Path $validationRoot 'motion-one-80s.mp4'
$videoTwo = Join-Path $validationRoot 'motion-two-80s.mp4'
$music = Join-Path $validationRoot 'music-80s.mp3'

if (-not (Test-Path -LiteralPath $videoOne)) {
    & $ffmpeg -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=1920x1080:rate=30:duration=80' -f lavfi -i 'sine=frequency=440:sample_rate=48000:duration=80' -c:v h264_mf -rate_control pc_vbr -b:v 4M -c:a aac -shortest $videoOne
    if ($LASTEXITCODE -ne 0) { throw 'First benchmark video generation failed.' }
}
if (-not (Test-Path -LiteralPath $videoTwo)) {
    & $ffmpeg -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=1920x1080:rate=30:duration=80' -f lavfi -i 'sine=frequency=660:sample_rate=48000:duration=80' -vf 'hue=h=90' -c:v h264_mf -rate_control pc_vbr -b:v 4M -c:a aac -shortest $videoTwo
    if ($LASTEXITCODE -ne 0) { throw 'Second benchmark video generation failed.' }
}
if (-not (Test-Path -LiteralPath $music)) {
    & $ffmpeg -hide_banner -loglevel error -f lavfi -i 'sine=frequency=220:sample_rate=48000:duration=80' -c:a libmp3lame $music
    if ($LASTEXITCODE -ne 0) { throw 'MP3 generation failed.' }
}
if ($GenerateOnly) { Write-Output $validationRoot; exit 0 }

$ExecutablePath = [IO.Path]::GetFullPath($ExecutablePath)
$cpuCount = [Environment]::ProcessorCount
function Invoke-Preview([string]$Mode, [int]$Duration, [bool]$Measure) {
    $report = Join-Path $validationRoot "$Mode.json"
    # Only these generated result files are replaced; media and unrelated files are untouched.
    foreach ($generated in @($report, "$report.ready")) {
        if (Test-Path -LiteralPath $generated) { Remove-Item -LiteralPath $generated -Force }
    }
    $arguments = @('--preview-diagnostics', $Mode,
        '--videos', ('"' + $videoOne + ',' + $videoTwo + '"'),
        '--audio', ('"' + $music + '"'),
        '--report', ('"' + $report + '"'),
        '--seconds', $Duration)
    $process = Start-Process -FilePath $ExecutablePath -ArgumentList $arguments -WorkingDirectory $projectRoot -PassThru
    $samples = [Collections.Generic.List[object]]::new()
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(100)
        while (-not $process.HasExited -and -not (Test-Path -LiteralPath "$report.ready") -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 100
            $process.Refresh()
        }
        if ($Measure -and (Test-Path -LiteralPath "$report.ready")) {
            $previous = @{}
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $previousTime = 0.0
            $gpuAvailable = $true
            while ($timer.Elapsed.TotalSeconds -lt $Duration -and -not $process.HasExited) {
                $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($process.Id)" -ErrorAction SilentlyContinue)
                $ids = @($process.Id) + @($children | ForEach-Object { [int]$_.ProcessId })
                $cpu = 0.0
                $working = 0L
                $private = 0L
                foreach ($id in $ids) {
                    $item = Get-Process -Id $id -ErrorAction SilentlyContinue
                    if ($null -eq $item) { continue }
                    try {
                        $total = $item.TotalProcessorTime.TotalSeconds
                        if ($previous.ContainsKey($id)) { $cpu += [Math]::Max(0, $total - $previous[$id]) }
                        $previous[$id] = $total
                        $working += $item.WorkingSet64
                        $private += $item.PrivateMemorySize64
                    } catch { }
                }
                $gpu = $null
                if ($gpuAvailable) {
                    try {
                        $counters = (Get-Counter '\GPU Engine(*)\Utilization Percentage' -ErrorAction Stop).CounterSamples
                        $engines = @{}
                        foreach ($counter in $counters) {
                            if ($counter.InstanceName -match '^pid_(\d+)_(.+)$' -and $ids -contains [int]$Matches[1]) {
                                $engine = $Matches[2]
                                $engines[$engine] = [double]$engines[$engine] + [double]$counter.CookedValue
                            }
                        }
                        $gpu = if ($engines.Count -eq 0) { 0.0 } else { ($engines.Values | Measure-Object -Maximum).Maximum }
                    } catch { $gpuAvailable = $false }
                }
                $now = $timer.Elapsed.TotalSeconds
                if ($previousTime -gt 0) {
                    $samples.Add([pscustomobject]@{
                        Seconds=$now; CpuPercent=100 * $cpu / (($now - $previousTime) * $cpuCount)
                        WorkingSetMiB=$working / 1MB; PrivateMiB=$private / 1MB; BusiestGpuEnginePercent=$gpu
                    })
                }
                $previousTime = $now
                Start-Sleep -Milliseconds 750
                $process.Refresh()
            }
            [IO.File]::WriteAllText((Join-Path $validationRoot "$Mode-resources.json"), ($samples | ConvertTo-Json -Depth 4))
        }
        if (-not $process.WaitForExit(30000)) { throw "Preview diagnostics timeout: $Mode" }
        $process.Refresh()
        if (-not (Test-Path -LiteralPath $report)) { throw "No diagnostic report: $Mode" }
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($process.ExitCode -ne 0 -or -not $result.Success) { throw ($result | ConvertTo-Json -Depth 5) }
        Write-Output ($result | ConvertTo-Json -Depth 5)
        if ($Measure) {
            [pscustomobject]@{
                Mode=$Mode; Samples=$samples.Count
                AverageCpuPercent=($samples.CpuPercent | Measure-Object -Average).Average
                AverageWorkingSetMiB=($samples.WorkingSetMiB | Measure-Object -Average).Average
                PeakWorkingSetMiB=($samples.WorkingSetMiB | Measure-Object -Maximum).Maximum
                AveragePrivateMiB=($samples.PrivateMiB | Measure-Object -Average).Average
                AverageBusiestGpuEnginePercent=($samples.BusiestGpuEnginePercent | Measure-Object -Average).Average
            } | ConvertTo-Json | Write-Output
        }
    } finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}

Invoke-Preview 'checks' 5 $false
Invoke-Preview 'fallback' 5 $false
if (-not $ChecksOnly) {
    Invoke-Preview 'legacy' $Seconds $true
    Invoke-Preview 'players' $Seconds $true
}
exit 0
