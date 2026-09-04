param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\artifacts\DiffVideo-win-x64\DiffVideo.exe'),
    [int]$TimeoutSeconds = 12,
    [int]$StableSeconds = 3
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = [System.IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "Executable not found: $resolvedExecutable"
}

$process = Start-Process -FilePath $resolvedExecutable -WorkingDirectory (Split-Path -Parent $resolvedExecutable) -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited) {
            throw "DiffVideo exited before creating a window. Exit code: $($process.ExitCode)"
        }
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)

    if ($process.MainWindowHandle -eq 0) {
        throw "DiffVideo did not create a main window within $TimeoutSeconds seconds."
    }

    if ($process.MainWindowTitle -ne 'DiffVideo') {
        throw "Unexpected main window title: $($process.MainWindowTitle)"
    }

    $stableDeadline = [DateTime]::UtcNow.AddSeconds($StableSeconds)
    while ([DateTime]::UtcNow -lt $stableDeadline) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited -or $process.MainWindowHandle -eq 0) {
            throw 'DiffVideo window did not remain open during the stability check.'
        }
    }

    if (-not $process.Responding) {
        throw 'DiffVideo window is not responding.'
    }

    Write-Output "GUI startup passed and remained responsive for $StableSeconds seconds. Window handle: $($process.MainWindowHandle)"
}
finally {
    if (-not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(3000)) {
            Stop-Process -Id $process.Id -Force
            if (-not $process.WaitForExit(5000)) {
                throw 'DiffVideo test process did not terminate.'
            }
        }
    }

    $process.Dispose()
}

$unlockDeadline = [DateTime]::UtcNow.AddSeconds(10)
do {
    try {
        $stream = [System.IO.File]::Open($resolvedExecutable, 'Open', 'Read', 'None')
        $stream.Dispose()
        break
    }
    catch [System.IO.IOException] {
        if ([DateTime]::UtcNow -ge $unlockDeadline) {
            throw "DiffVideo executable remained locked after the startup test: $resolvedExecutable"
        }

        Start-Sleep -Milliseconds 200
    }
} while ($true)
