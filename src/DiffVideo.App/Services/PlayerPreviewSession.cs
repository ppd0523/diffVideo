using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.App.Services;

/// <summary>Coordinates source players; never generates a merged video frame or file.</summary>
public sealed class PlayerPreviewSession : IDisposable
{
    private readonly SourceVideoPlayer _one;
    private readonly SourceVideoPlayer _two;
    private readonly BufferedTimelineAudio _audio;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly DrawingGroup _canvas = new();
    private Composition? _layoutComposition;
    private int? _roiFocus;
    private bool _disposed;

    public PlayerPreviewSession(FfmpegPaths paths, bool forceDecoder = false)
    {
        _one = new(paths, forceDecoder);
        _two = new(paths, forceDecoder);
        _audio = new(paths);
        Image = new DrawingImage(_canvas);
    }

    public ImageSource Image { get; }
    public string Backends => $"{_one.Backend} / {_two.Backend}";
    public int BufferingCount { get; private set; }
    public double MaximumObservedSkewSeconds { get; private set; }

    public async Task ShowStillAsync(Composition composition, double atSeconds, CancellationToken token, Func<bool>? isCurrent = null)
    {
        await _operations.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await OpenAsync(composition, token);
            ApplyLayout(composition);
            await Task.WhenAll(
                _one.ShowStillAsync(PreviewTiming.Map(atSeconds, composition.Video1).Seconds, token, isCurrent),
                _two.ShowStillAsync(PreviewTiming.Map(atSeconds, composition.Video2).Seconds, token, isCurrent));
        }
        finally { _operations.Release(); }
    }

    public async Task RunAsync(Composition composition, double from, Action<double> onTime, Action<bool> onBuffering, CancellationToken token)
    {
        await _operations.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            onBuffering(true);
            await OpenAsync(composition, token);
            ApplyLayout(composition);
            await Task.WhenAll(
                _one.PrepareAsync(composition.Video1, from, composition.Output.FramesPerSecond, token),
                _two.PrepareAsync(composition.Video2, from, composition.Output.FramesPerSecond, token),
                _audio.PrepareAsync(composition, from, token));
            token.ThrowIfCancellationRequested();
            _one.Present(composition.Video1, from);
            _two.Present(composition.Video2, from);
            _audio.Play();
            onBuffering(false);
            var lastTime = from;
            var stalled = Stopwatch.StartNew();
            while (true)
            {
                await Task.Delay(10, token);
                var time = from + _audio.PositionSeconds;
                if (time >= composition.Output.Duration.TotalSeconds - 0.002) { break; }
                if (time > lastTime + 0.001) { stalled.Restart(); lastTime = time; }
                if (stalled.Elapsed > TimeSpan.FromSeconds(20)) { throw new TimeoutException("재생 시계가 진행되지 않습니다."); }
                var readyOne = _one.Present(composition.Video1, time);
                var readyTwo = _two.Present(composition.Video2, time);
                ObserveSkew(_one, composition.Video1, time);
                ObserveSkew(_two, composition.Video2, time);
                if (!readyOne || !readyTwo || _audio.NeedsBuffer)
                {
                    // The audio device clock stops here; no independent wall clock keeps running.
                    _audio.Pause();
                    _one.Pause();
                    _two.Pause();
                    time = from + _audio.PositionSeconds;
                    onTime(time);
                    onBuffering(true);
                    BufferingCount++;
                    await Task.WhenAll(
                        _audio.WaitForBufferAsync(token),
                        _one.PrepareAsync(composition.Video1, time, composition.Output.FramesPerSecond, token),
                        _two.PrepareAsync(composition.Video2, time, composition.Output.FramesPerSecond, token));
                    var timeout = Stopwatch.StartNew();
                    while (_one.IsBuffering || _two.IsBuffering)
                    {
                        if (timeout.Elapsed > TimeSpan.FromSeconds(20)) { throw new TimeoutException("영상 재생 준비 시간이 초과되었습니다."); }
                        await Task.Delay(20, token);
                    }
                    token.ThrowIfCancellationRequested();
                    _one.Present(composition.Video1, time);
                    _two.Present(composition.Video2, time);
                    _audio.Play();
                    onBuffering(false);
                    stalled.Restart();
                }
                onTime(time);
            }
        }
        finally
        {
            _audio.Stop();
            await Task.WhenAll(_one.StopAsync(), _two.StopAsync());
            onBuffering(false);
            _operations.Release();
        }
    }

    public void PauseImmediately()
    {
        _audio.Pause();
        _one.Pause();
        _two.Pause();
    }

    private void ObserveSkew(SourceVideoPlayer player, VideoTrack track, double time)
    {
        var position = PreviewTiming.Map(time, track);
        if (position.IsActive)
        {
            MaximumObservedSkewSeconds = Math.Max(MaximumObservedSkewSeconds, Math.Abs(player.PositionSeconds - position.Seconds));
        }
    }

    private async Task OpenAsync(Composition composition, CancellationToken token)
    {
        await Task.WhenAll(_one.OpenAsync(composition.Video1.Media, token), _two.OpenAsync(composition.Video2.Media, token));
    }

    public void SetRoiFocus(int? videoIndex)
    {
        if (_disposed) { return; }
        _roiFocus = videoIndex;
        if (_layoutComposition is { } composition) { ApplyLayout(composition); }
    }

    public void ApplyLayout(Composition composition)
    {
        _layoutComposition = composition;
        _canvas.Children.Clear();
        var bounds = new RectangleGeometry(new Rect(0, 0, composition.Output.Width, composition.Output.Height));
        _canvas.ClipGeometry = bounds;
        _canvas.Children.Add(new GeometryDrawing(Brushes.Black, null, bounds));
        // This is only a display override; the export snapshot and model ZIndex are untouched.
        foreach (var item in new[] { (Index: 1, Track: composition.Video1, Player: _one), (Index: 2, Track: composition.Video2, Player: _two) }
            .OrderBy(item => item.Index == _roiFocus ? int.MaxValue : item.Track.ZIndex))
        {
            var layout = PreviewLayout.From(item.Track);
            var dst = item.Track.Destination;
            _canvas.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(dst.X, dst.Y, dst.Width, dst.Height))));
            var transformed = new DrawingGroup { Transform = new MatrixTransform(layout.ScaleX, 0, 0, layout.ScaleY, layout.TranslateX, layout.TranslateY) };
            transformed.Children.Add(item.Player.Drawing);
            var clip = layout.Clip;
            var clipped = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(clip.X, clip.Y, clip.Width, clip.Height)) };
            clipped.Children.Add(transformed);
            _canvas.Children.Add(clipped);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _audio.Dispose();
        _one.Dispose();
        _two.Dispose();
    }
}
