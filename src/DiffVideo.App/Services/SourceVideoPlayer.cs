using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.App.Services;

/// <summary>How far a player trails the shared clock, and whether it has nothing to show yet.</summary>
public readonly record struct PresentResult(double LagSeconds, bool IsBuffering);

/// <summary>One muted video player. Its mutable Drawing is transformed/clipped by the canvas.</summary>
public sealed class SourceVideoPlayer(FfmpegPaths paths, bool forceDecoder = false) : IDisposable
{
    private readonly FfmpegPreviewService _stills = new(paths);
    private readonly Dictionary<double, BitmapSource> _cache = [];
    private MediaInfo? _media;
    private MediaPlayer? _native;
    private bool _nativeFailed;
    private bool _active;
    private SourceVideoDecoder? _decoder;
    private PreviewFrame? _nextFrame;
    private double _displayedTime;
    private VideoDrawing? _nativeDrawing;

    public DrawingGroup Drawing { get; } = new();
    public bool UsesNativePlayer => _native is not null && !_nativeFailed;
    public bool IsBuffering => UsesNativePlayer && _native!.IsBuffering;
    public double PositionSeconds => UsesNativePlayer ? _native!.Position.TotalSeconds : _displayedTime;
    public string Backend => UsesNativePlayer ? "Windows MediaPlayer" : "FFmpeg 원본 디코더";

    public async Task OpenAsync(MediaInfo media, CancellationToken token)
    {
        if (_media == media)
        {
            return;
        }

        await StopDecoderAsync();
        _native?.Close();
        _native = null;
        _media = media;
        _cache.Clear();
        // FFmpeg's source decoder consistently honors rotation metadata, including 180°.
        _nativeFailed = forceDecoder || media.Rotation != 0;
        _active = false;
        if (_nativeFailed)
        {
            return;
        }

        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new MediaPlayer { Volume = 0, IsMuted = true, ScrubbingEnabled = true };
        player.MediaOpened += (_, _) => opened.TrySetResult();
        player.MediaFailed += (_, e) =>
        {
            if (ReferenceEquals(_native, player)) { _nativeFailed = true; }
            opened.TrySetException(e.ErrorException);
        };
        _native = player;
        _nativeDrawing = new() { Player = player, Rect = SourceRect };
        try
        {
            player.Open(new Uri(Path.GetFullPath(media.Path)));
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            player.Pause();
            if (!player.HasVideo)
            {
                throw new InvalidOperationException("Windows 플레이어가 영상 스트림을 열지 못했습니다.");
            }
        }
        catch (OperationCanceledException)
        {
            player.Close();
            _media = null;
            throw;
        }
        catch (Exception)
        {
            player.Close();
            _nativeFailed = true;
        }
    }

    public async Task ShowStillAsync(double seconds, CancellationToken token, Func<bool>? isCurrent = null)
    {
        Pause();
        await StopDecoderAsync();
        var bitmap = await ReadStillAsync(seconds, token);
        token.ThrowIfCancellationRequested();
        if (isCurrent?.Invoke() == false) { return; }
        ShowBitmap(bitmap);
        _displayedTime = seconds;
    }

    public async Task PrepareAsync(VideoTrack track, double timelineSeconds, int fps, CancellationToken token)
    {
        Pause();
        await StopDecoderAsync();
        var position = PreviewTiming.Map(timelineSeconds, track);
        var last = PreviewTiming.Map(double.MaxValue, track).Seconds;
        await ReadStillAsync(0, token);
        await ReadStillAsync(last, token);
        token.ThrowIfCancellationRequested();
        if (!position.IsActive && timelineSeconds >= track.Start.TotalSeconds)
        {
            ShowBitmap(_cache[Key(last)]);
            return;
        }

        if (UsesNativePlayer)
        {
            _native!.Position = TimeSpan.FromSeconds(position.Seconds);
            // Allow a render tick for the muted native player to prepare its seek frame.
            await Task.Delay(35, token);
        }
        else
        {
            _decoder = new(paths, track.Media, position.Seconds, fps, token);
            var first = await _decoder.ReadAsync(token);
            ShowFrame(first);
            _displayedTime = first.TimeSeconds;
        }

        if (!position.IsActive)
        {
            ShowBitmap(_cache[Key(0)]);
        }
    }

    /// <summary>
    /// Advances this player to the shared clock. Lag is reported, never acted on here; the only
    /// correction applied is re-seeking a self-clocked native player that drifted past its budget.
    /// </summary>
    public PresentResult Present(VideoTrack track, double timelineSeconds, double reseekSeconds)
    {
        var position = PreviewTiming.Map(timelineSeconds, track);
        if (!position.IsActive)
        {
            Pause();
            if (_cache.TryGetValue(Key(position.Seconds), out var still))
            {
                ShowBitmap(still);
            }
            return default;
        }

        if (UsesNativePlayer)
        {
            if (!_active)
            {
                _native!.Position = TimeSpan.FromSeconds(position.Seconds);
                Drawing.Children.Clear();
                Drawing.Children.Add(_nativeDrawing!);
                _native.Play();
                _active = true;
                return default;
            }

            var drift = Math.Abs(_native!.Position.TotalSeconds - position.Seconds);
            if (drift > reseekSeconds)
            {
                // A native player owns its clock and cannot be told to hurry; seeking is the only lever.
                _native.Position = TimeSpan.FromSeconds(position.Seconds);
            }

            return new(drift, _native.IsBuffering);
        }

        if (_decoder is null)
        {
            return new(0, true);
        }

        _active = true;
        while (true)
        {
            if (_nextFrame is null && !_decoder.TryRead(out _nextFrame))
            {
                break;
            }
            if (_nextFrame!.TimeSeconds > position.Seconds + 0.001)
            {
                break;
            }
            ShowFrame(_nextFrame);
            _displayedTime = _nextFrame.TimeSeconds;
            _nextFrame = null;
        }

        // The drain above already skipped whatever it could; what remains is honest lag.
        return new(Math.Max(0, position.Seconds - _displayedTime), false);
    }

    public void Pause()
    {
        if (_active && UsesNativePlayer)
        {
            _native!.Pause();
        }
        _active = false;
    }

    public async Task StopAsync()
    {
        Pause();
        await StopDecoderAsync();
    }

    private async Task<BitmapSource> ReadStillAsync(double seconds, CancellationToken token)
    {
        var key = Key(seconds);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var png = await _stills.GetSourceFramePngAsync(_media!, seconds, token);
        token.ThrowIfCancellationRequested();
        using var memory = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = memory;
        image.EndInit();
        image.Freeze();
        var last = Math.Max(0, (_media!.VideoDuration ?? _media.Duration).TotalSeconds - 1 / Math.Max(1, _media.FramesPerSecond));
        foreach (var old in _cache.Keys.Where(item => item != Key(0) && item != Key(last)).ToArray())
        {
            _cache.Remove(old);
        }
        _cache[key] = image;
        return image;
    }

    private Rect SourceRect => new(0, 0, _media!.DisplayWidth, _media.DisplayHeight);
    private static double Key(double seconds) => Math.Round(seconds, 7);

    private void ShowFrame(PreviewFrame frame)
    {
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels, frame.Width * 4);
        bitmap.Freeze();
        ShowBitmap(bitmap);
    }

    private void ShowBitmap(BitmapSource bitmap)
    {
        if (Drawing.Children.Count == 1 && Drawing.Children[0] is ImageDrawing image && ReferenceEquals(image.ImageSource, bitmap))
        {
            return;
        }
        Drawing.Children.Clear();
        Drawing.Children.Add(new ImageDrawing(bitmap, SourceRect));
    }

    private async Task StopDecoderAsync()
    {
        var decoder = _decoder;
        _decoder = null;
        _nextFrame = null;
        if (decoder is not null)
        {
            await decoder.DisposeAsync();
        }
    }

    public void Dispose()
    {
        Pause();
        _native?.Close();
        _native = null;
        _ = StopDecoderAsync();
    }
}
