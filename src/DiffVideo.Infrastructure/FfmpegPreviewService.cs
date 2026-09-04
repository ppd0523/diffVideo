using System.Diagnostics;
using DiffVideo.Core;

namespace DiffVideo.Infrastructure;

public sealed record PreviewFrame(byte[] Pixels, int Width, int Height, double TimeSeconds);

public sealed class FfmpegPreviewService(FfmpegPaths paths)
{
    private readonly FfmpegPaths _paths = paths;

    public async Task<byte[]> GetStillPngAsync(Composition composition, double atSeconds, CancellationToken cancellationToken = default)
    {
        var invocation = FfmpegCommandBuilder.BuildStillPreview(composition, atSeconds);
        using var process = new Process { StartInfo = ProcessRunner.CreateStartInfo(_paths.Ffmpeg, invocation.Arguments) };
        process.Start();
        using var registration = cancellationToken.Register(() => ProcessRunner.TryKill(process));
        await using var memory = new MemoryStream();
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardOutput.BaseStream.CopyToAsync(memory, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0 || memory.Length == 0)
        {
            throw new InvalidOperationException("미리보기 프레임을 만들 수 없습니다: " + await errorTask);
        }

        return memory.ToArray();
    }

    public async Task<byte[]> GetSourceFramePngAsync(MediaInfo media, double atSeconds, CancellationToken cancellationToken = default)
    {
        var arguments = new[]
        {
            "-hide_banner", "-nostdin", "-ss", Math.Max(0, atSeconds).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture),
            "-i", media.Path, "-frames:v", "1", "-an", "-vf", "scale='min(1280,iw)':-2", "-c:v", "png", "-f", "image2pipe", "pipe:1"
        };
        using var process = new Process { StartInfo = ProcessRunner.CreateStartInfo(_paths.Ffmpeg, arguments) };
        process.Start();
        using var registration = cancellationToken.Register(() => ProcessRunner.TryKill(process));
        await using var memory = new MemoryStream();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardOutput.BaseStream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0 || memory.Length == 0)
            {
                throw new InvalidOperationException("원본 프레임을 만들 수 없습니다: " + await errorTask.ConfigureAwait(false));
            }
            return memory.ToArray();
        }
        finally
        {
            ProcessRunner.TryKill(process);
            await process.WaitForExitAsync().ConfigureAwait(false);
            await errorTask.ConfigureAwait(false);
        }
    }

    public async Task StreamAsync(
        Composition composition,
        double fromSeconds,
        Func<PreviewFrame, Task> onFrame,
        Func<Task>? onReady,
        CancellationToken cancellationToken)
    {
        var invocation = FfmpegCommandBuilder.BuildPreviewStream(composition, fromSeconds);
        using var process = new Process { StartInfo = ProcessRunner.CreateStartInfo(_paths.Ffmpeg, invocation.Arguments) };
        process.Start();
        using var registration = cancellationToken.Register(() => ProcessRunner.TryKill(process));
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var frameSize = checked(invocation.PreviewWidth * invocation.PreviewHeight * 4);
        var frameDuration = TimeSpan.FromSeconds(1d / composition.Output.FramesPerSecond);
        Stopwatch? clock = null;
        var frameIndex = 0L;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var buffer = GC.AllocateUninitializedArray<byte>(frameSize);
                var bytesRead = await ReadExactlyOrEndAsync(process.StandardOutput.BaseStream, buffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead < frameSize)
                {
                    break;
                }

                if (frameIndex == 0)
                {
                    if (onReady is not null)
                    {
                        await onReady().ConfigureAwait(false);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    clock = Stopwatch.StartNew();
                }

                var time = fromSeconds + frameIndex / (double)composition.Output.FramesPerSecond;
                await onFrame(new(buffer, invocation.PreviewWidth, invocation.PreviewHeight, time)).ConfigureAwait(false);
                frameIndex++;
                var desired = TimeSpan.FromTicks(frameDuration.Ticks * frameIndex);
                var delay = desired - clock!.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            ProcessRunner.TryKill(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await errorTask.ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static async Task<int> ReadExactlyOrEndAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
