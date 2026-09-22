using System.Diagnostics;
using System.Globalization;
using System.Text;
using DiffVideo.Core;

namespace DiffVideo.Infrastructure;

public sealed record ExportProgress(double Fraction, TimeSpan Processed, string Encoder, string Message);

public sealed record ExportResult(string OutputPath, H264Encoder Encoder, TimeSpan Elapsed);

public sealed class FfmpegExportService(FfmpegPaths paths)
{
    private readonly FfmpegPaths _paths = paths;

    public async Task<ExportResult> ExportAsync(
        Composition composition,
        string outputPath,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool overwrite = false,
        IReadOnlyList<LabelOverlayAsset>? labels = null)
    {
        var validation = CompositionValidator.Validate(composition);
        if (validation.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Select(issue => issue.Message)));
        }

        outputPath = Path.GetFullPath(outputPath);
        var sources = composition.Videos.Select(track => track.Media.Path)
            .Concat(composition.Audios.Select(track => track.Media.Path));
        if (sources.Any(source => source is not null && string.Equals(Path.GetFullPath(source), outputPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new IOException("원본 영상 또는 음원과 같은 경로로 저장할 수 없습니다.");
        }
        if (File.Exists(outputPath) && !overwrite)
        {
            throw new IOException("동일한 이름의 출력 파일이 이미 존재합니다.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(directory);
        var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".part";

        var errors = new List<string>();
        try
        {
            foreach (var encoder in new[] { H264Encoder.AmdAmf, H264Encoder.MediaFoundation })
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteIfExists(temporaryPath);
                var invocation = FfmpegCommandBuilder.BuildExport(composition, temporaryPath, encoder, labels);
                var stopwatch = Stopwatch.StartNew();
                var result = await RunExportProcessAsync(invocation, composition.Output.ExportDuration, encoder, progress, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                if (result.ExitCode == 0 && File.Exists(temporaryPath) && new FileInfo(temporaryPath).Length > 0)
                {
                    File.Move(temporaryPath, outputPath, overwrite);
                    progress?.Report(new(1, composition.Output.ExportDuration, EncoderName(encoder), "내보내기 완료"));
                    return new(outputPath, encoder, stopwatch.Elapsed);
                }

                errors.Add($"{EncoderName(encoder)}: {LastMeaningfulLine(result.Error)}");
            }
        }
        finally
        {
            await DeleteIfExistsWithRetryAsync(temporaryPath).ConfigureAwait(false);
        }

        throw new InvalidOperationException("사용 가능한 H.264 인코더가 없습니다." + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    private async Task<(int ExitCode, string Error)> RunExportProcessAsync(
        FfmpegInvocation invocation,
        TimeSpan duration,
        H264Encoder encoder,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = ProcessRunner.CreateStartInfo(_paths.Ffmpeg, invocation.Arguments) };
        process.Start();
        using var registration = cancellationToken.Register(() => ProcessRunner.TryKill(process));
        var error = new StringBuilder();
        var outputDrain = process.StandardOutput.ReadToEndAsync(cancellationToken);

        try
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                error.AppendLine(line);
                if (!line.StartsWith("out_time=", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TimeSpan.TryParse(line[9..], CultureInfo.InvariantCulture, out var processed))
                {
                    var fraction = duration.TotalSeconds <= 0 ? 0 : Math.Clamp(processed.TotalSeconds / duration.TotalSeconds, 0, 1);
                    progress?.Report(new(fraction, processed, EncoderName(encoder), "인코딩 중"));
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await outputDrain.ConfigureAwait(false);
            return (process.ExitCode, error.ToString());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ProcessRunner.TryKill(process);
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            throw;
        }
    }

    private static string EncoderName(H264Encoder encoder) => encoder == H264Encoder.AmdAmf ? "AMD AMF" : "Media Foundation";

    private static string LastMeaningfulLine(string error) =>
        error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "알 수 없는 오류";

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private static async Task DeleteIfExistsWithRetryAsync(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }
}
