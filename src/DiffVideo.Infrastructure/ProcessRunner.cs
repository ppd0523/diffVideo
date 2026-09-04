using System.Diagnostics;
using System.Text;

namespace DiffVideo.Infrastructure;

internal static class ProcessRunner
{
    public static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    public static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunTextAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process { StartInfo = CreateStartInfo(executable, arguments) };
        process.Start();

        await using var registration = cancellationToken.Register(() => TryKill(process));
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await outputTask, await errorTask);
    }

    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                // Direct ffmpeg/ffprobe sidecars do not launch a shell. Target only
                // our owned process, avoiding tree-enumeration races during exit.
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Exit may win the HasExited/Kill race. The owner still waits for exit
            // and validates cleanup; cancellation callbacks must not throw.
        }
    }
}
