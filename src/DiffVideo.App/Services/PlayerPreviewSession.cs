using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.App.Services;

/// <summary>Coordinates source players; never generates a merged video frame or file.</summary>
public sealed class PlayerPreviewSession : IDisposable
{
    private readonly FfmpegPaths _paths;
    private readonly bool _forceDecoder;
    private readonly List<SourceVideoPlayer> _players = [];
    private readonly List<bool> _lagging = [];
    private readonly List<bool> _everLagged = [];
    private readonly BufferedTimelineAudio _audio;
    private readonly PlaybackSkewPolicy _skew = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly DrawingGroup _canvas = new();
    private Composition? _layoutComposition;
    private int? _roiFocus;
    private bool _disposed;

    public PlayerPreviewSession(FfmpegPaths paths, bool forceDecoder = false)
    {
        _paths = paths;
        _forceDecoder = forceDecoder;
        _audio = new(paths);
        Image = new DrawingImage(_canvas);
    }

    public ImageSource Image { get; }
    public string Backends => string.Join(" / ", _players.Select(player => player.Backend));
    public int BufferingCount { get; private set; }
    public double MaximumObservedSkewSeconds { get; private set; }

    /// <summary>Per-track lag flags, indexed like the composition videos; read after each time callback.</summary>
    public IReadOnlyList<bool> TrackLagging => _lagging;

    /// <summary>Tracks that lagged at any point in the run, for the message shown once it ends.</summary>
    public IReadOnlyList<bool> TrackEverLagged => _everLagged;

    public double BadgeThresholdSeconds => _skew.BadgeSeconds;

    public async Task ShowStillAsync(Composition composition, double atSeconds, CancellationToken token, Func<bool>? isCurrent = null)
    {
        await _operations.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await OpenAsync(composition, token);
            ApplyLayout(composition);
            await Task.WhenAll(composition.Videos.Select((track, index) =>
                _players[index].ShowStillAsync(PreviewTiming.Map(atSeconds, track).Seconds, token, isCurrent)));
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
            for (var index = 0; index < _everLagged.Count; index++) { _everLagged[index] = false; }
            await Task.WhenAll(composition.Videos
                .Select((track, index) => _players[index].PrepareAsync(track, from, composition.Output.FramesPerSecond, token))
                .Append(_audio.PrepareAsync(composition, from, token)));
            token.ThrowIfCancellationRequested();
            PresentAll(composition, from);
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

                // A lagging video keeps its last frame and raises its own badge; it no longer stops
                // the shared clock. Only a starved audio mix can, because the audio device IS the
                // clock here and no independent wall clock exists to take over.
                PresentAll(composition, time);
                if (_audio.NeedsBuffer)
                {
                    _audio.Pause();
                    foreach (var player in _players) { player.Pause(); }
                    time = from + _audio.PositionSeconds;
                    onTime(time);
                    onBuffering(true);
                    BufferingCount++;
                    await _audio.WaitForBufferAsync(token);
                    token.ThrowIfCancellationRequested();
                    PresentAll(composition, time);
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
            await Task.WhenAll(_players.Select(player => player.StopAsync()));
            onBuffering(false);
            _operations.Release();
        }
    }

    public void PauseImmediately()
    {
        _audio.Pause();
        foreach (var player in _players) { player.Pause(); }
    }

    /// <summary>Index of the track lifted above its layer order while its ROI is being edited.</summary>
    public void SetRoiFocus(int? trackIndex)
    {
        if (_disposed) { return; }
        _roiFocus = trackIndex;
        if (_layoutComposition is { } composition) { ApplyLayout(composition); }
    }

    public void ApplyLayout(Composition composition)
    {
        _layoutComposition = composition;
        EnsurePlayers(composition.Videos.Count);
        _canvas.Children.Clear();
        var bounds = new RectangleGeometry(new Rect(0, 0, composition.Output.Width, composition.Output.Height));
        _canvas.ClipGeometry = bounds;
        _canvas.Children.Add(new GeometryDrawing(Brushes.Black, null, bounds));
        // This is only a display override; the export snapshot and model ZIndex are untouched.
        var ordered = composition.Videos
            .Select((track, index) => (Index: index, Track: track))
            .OrderBy(item => item.Index == _roiFocus ? int.MaxValue : item.Track.ZIndex);
        foreach (var item in ordered)
        {
            var layout = PreviewLayout.From(item.Track);
            var dst = item.Track.Destination;
            _canvas.Children.Add(new GeometryDrawing(Brushes.Black, null, new RectangleGeometry(new Rect(dst.X, dst.Y, dst.Width, dst.Height))));
            var transformed = new DrawingGroup { Transform = new MatrixTransform(layout.ScaleX, 0, 0, layout.ScaleY, layout.TranslateX, layout.TranslateY) };
            transformed.Children.Add(_players[item.Index].Drawing);
            var clip = layout.Clip;
            var clipped = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(clip.X, clip.Y, clip.Width, clip.Height)) };
            clipped.Children.Add(transformed);
            _canvas.Children.Add(clipped);
        }
    }

    private void PresentAll(Composition composition, double time)
    {
        var reseek = _skew.ReseekSeconds;
        var badge = _skew.BadgeSeconds;
        for (var index = 0; index < composition.Videos.Count && index < _players.Count; index++)
        {
            var result = _players[index].Present(composition.Videos[index], time, reseek);
            _skew.Observe(result.LagSeconds);
            MaximumObservedSkewSeconds = Math.Max(MaximumObservedSkewSeconds, result.LagSeconds);
            var lagging = result.IsBuffering || result.LagSeconds > badge;
            _lagging[index] = lagging;
            _everLagged[index] |= lagging;
        }
    }

    // ponytail: players are bound to row positions, so reordering rows makes two players
    // reopen each other's file. Fine for four tracks; key players by media path if it drags.
    private async Task OpenAsync(Composition composition, CancellationToken token)
    {
        EnsurePlayers(composition.Videos.Count);
        await Task.WhenAll(composition.Videos.Select((track, index) => _players[index].OpenAsync(track.Media, token)));
    }

    private void EnsurePlayers(int count)
    {
        while (_players.Count < count)
        {
            _players.Add(new(_paths, _forceDecoder));
            _lagging.Add(false);
            _everLagged.Add(false);
        }

        while (_players.Count > count)
        {
            _players[^1].Dispose();
            _players.RemoveAt(_players.Count - 1);
            _lagging.RemoveAt(_lagging.Count - 1);
            _everLagged.RemoveAt(_everLagged.Count - 1);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _audio.Dispose();
        foreach (var player in _players) { player.Dispose(); }
        _players.Clear();
    }
}
