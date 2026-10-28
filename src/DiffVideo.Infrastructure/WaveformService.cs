using System.Diagnostics;
using System.Runtime.InteropServices;
using DiffVideo.Core;

namespace DiffVideo.Infrastructure;

public sealed class WaveformService(FfmpegPaths paths)
{
    private readonly FfmpegPaths _paths = paths;

    /// <summary>음원을 저해상도 모노 PCM으로 풀어 구간 피크(0~1)를 돌려준다.</summary>
    public async Task<double[]> LoadPeaksAsync(string path, int bucketCount, CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = ProcessRunner.CreateStartInfo(
                _paths.Ffmpeg,
                ["-v", "error", "-nostdin", "-i", path, "-map", "0:a:0", "-ac", "1", "-ar", "8000", "-f", "s16le", "-"])
        };
        process.Start();
        await using var registration = cancellationToken.Register(() => ProcessRunner.TryKill(process));
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var pcm = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(pcm, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException($"파형을 읽을 수 없습니다: {error.Trim()}");
        }

        var bytes = pcm.GetBuffer().AsSpan(0, (int)pcm.Length & ~1);
        return Waveform.Peaks(MemoryMarshal.Cast<byte, short>(bytes), bucketCount);
    }
}
