using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using DiffVideo.Core;

namespace DiffVideo.Infrastructure;

/// <summary>A bounded decoder for one original video. No crop, overlay, canvas or encoding.</summary>
public sealed class SourceVideoDecoder : IAsyncDisposable
{
    private readonly Channel<PreviewFrame> _frames = Channel.CreateBounded<PreviewFrame>(new BoundedChannelOptions(4)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly CancellationTokenSource _cancellation;
    private readonly Task _worker;

    public SourceVideoDecoder(FfmpegPaths paths, MediaInfo media, double fromSeconds, int fps, CancellationToken cancellationToken)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = Task.Run(() => DecodeAsync(paths, media, fromSeconds, fps, _cancellation.Token));
    }

    public ValueTask<PreviewFrame> ReadAsync(CancellationToken token) => _frames.Reader.ReadAsync(token);
    public bool TryRead(out PreviewFrame? frame) => _frames.Reader.TryRead(out frame);
    public bool IsCompleted => _frames.Reader.Completion.IsCompleted;

    public static FfmpegInvocation BuildInvocation(MediaInfo media, double fromSeconds, int fps)
    {
        var ratio = Math.Min(1, Math.Min(960d / media.DisplayWidth, 540d / media.DisplayHeight));
        var width = Math.Max(2, (int)(media.DisplayWidth * ratio) / 2 * 2);
        var height = Math.Max(2, (int)(media.DisplayHeight * ratio) / 2 * 2);
        return new([
            "-hide_banner", "-loglevel", "error", "-nostdin", "-threads", "2",
            "-ss", Math.Max(0, fromSeconds).ToString("0.#########", CultureInfo.InvariantCulture),
            "-i", media.Path, "-map", "0:v:0", "-an", "-sn", "-dn",
            "-vf", $"fps={fps},scale={width}:{height}:flags=fast_bilinear",
            "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"
        ], width, height);
    }

    private async Task DecodeAsync(FfmpegPaths paths, MediaInfo media, double from, int fps, CancellationToken token)
    {
        var invocation = BuildInvocation(media, from, fps);
        using var process = new Process { StartInfo = ProcessRunner.CreateStartInfo(paths.Ffmpeg, invocation.Arguments) };
        try
        {
            process.Start();
            using var registration = token.Register(() => ProcessRunner.TryKill(process));
            var errors = process.StandardError.ReadToEndAsync();
            long index = 0;
            while (true)
            {
                var pixels = GC.AllocateUninitializedArray<byte>(invocation.PreviewWidth * invocation.PreviewHeight * 4);
                var read = await process.StandardOutput.BaseStream.ReadAtLeastAsync(pixels, pixels.Length, throwOnEndOfStream: false, token).ConfigureAwait(false);
                if (read != pixels.Length)
                {
                    break;
                }

                await _frames.Writer.WriteAsync(new(pixels, invocation.PreviewWidth, invocation.PreviewHeight, from + index++ / (double)fps), token).ConfigureAwait(false);
            }

            await process.WaitForExitAsync(token).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("원본 영상 재생 실패: " + await errors.ConfigureAwait(false));
            }

            _frames.Writer.TryComplete();
        }
        catch (OperationCanceledException)
        {
            _frames.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            _frames.Writer.TryComplete(exception);
        }
        finally
        {
            ProcessRunner.TryKill(process);
            try { await process.WaitForExitAsync().ConfigureAwait(false); }
            catch (InvalidOperationException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        await _worker.ConfigureAwait(false);
        _cancellation.Dispose();
    }
}
