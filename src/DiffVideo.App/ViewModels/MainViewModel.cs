using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiffVideo.App.Services;
using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly MediaProbeService _probeService;
    private readonly FfmpegPreviewService _previewService;
    private readonly FfmpegExportService _exportService;
    private readonly PlayerPreviewSession _playerPreview;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _stillCancellation;
    private CancellationTokenSource? _exportCancellation;
    private CancellationTokenSource? _propertyPreviewDebounceCancellation;
    private CancellationTokenSource? _navigationPreviewCancellation;
    private CancellationTokenSource? _interactivePreviewCancellation;
    private Task? _activeExportTask;
    private int _canvasWidth = 1920;
    private int _canvasHeight = 1080;
    private int _outputFps = 30;
    private double _outputDurationSeconds = 15;
    private OutputQuality _quality = OutputQuality.Balanced;
    private double _playheadSeconds;
    private double _playbackStartSeconds;
    private ImageSource? _previewImage;
    private string _status = "MP4 두 개를 끌어 놓으세요.";
    private string _errorMessage = "";
    private double _exportProgress;
    private bool _isExporting;
    private PlaybackState _playbackState = PlaybackState.Stopped;
    private VideoTrackViewModel _selectedVideo;
    private bool _suppressTrackPreview;
    private bool _hasInteractivePreviewPending;
    private bool _interactivePreviewScheduled;
    private bool _interactivePreviewInFlight;
    private bool _isPreviewLoading;
    private bool _disposed;
    private object? _pendingInteractiveTrack;
    private double _pendingInteractiveStart;
    private long _interactivePreviewRevision;
    private DateTimeOffset _lastInteractivePreviewStarted = DateTimeOffset.MinValue;

    public MainViewModel(FfmpegPaths paths)
    {
        _probeService = new(paths);
        _previewService = new(paths);
        _exportService = new(paths);
        _playerPreview = new(paths);
        PreviewImage = _playerPreview.Image;
        Video1 = new("영상 1", 0);
        Video2 = new("영상 2", 1);
        Audio = new();
        _selectedVideo = Video1;
        Video1.IsSelected = true;
        SubscribeTrack(Video1);
        SubscribeTrack(Video2);
        Audio.PropertyChanged += TrackPropertyChanged;
    }

    public VideoTrackViewModel Video1 { get; }
    public VideoTrackViewModel Video2 { get; }
    public AudioTrackViewModel Audio { get; }

    public IReadOnlyList<Choice<int>> FpsChoices { get; } =
    [
        new("24 fps", 24), new("25 fps", 25), new("30 fps", 30), new("50 fps", 50), new("60 fps", 60)
    ];

    public IReadOnlyList<Choice<OutputQuality>> QualityChoices { get; } =
    [
        new("고화질", OutputQuality.High), new("균형", OutputQuality.Balanced), new("작은 파일", OutputQuality.Small)
    ];

    public IReadOnlyList<Choice<VideoFitMode>> FitChoices { get; } =
    [
        new("맞춤", VideoFitMode.Fit), new("채우기", VideoFitMode.Fill), new("늘이기", VideoFitMode.Stretch)
    ];

    public int CanvasWidth
    {
        get => _canvasWidth;
        set
        {
            var normalized = CompositionValidator.NormalizeEvenDimension(value, 16, 3840);
            if (SetProperty(ref _canvasWidth, normalized))
            {
                ClampDestinations();
                OnPropertyChanged(nameof(CanvasSizeText));
                QueuePropertyPreview();
            }
        }
    }

    public int CanvasHeight
    {
        get => _canvasHeight;
        set
        {
            var normalized = CompositionValidator.NormalizeEvenDimension(value, 16, 2160);
            if (SetProperty(ref _canvasHeight, normalized))
            {
                ClampDestinations();
                OnPropertyChanged(nameof(CanvasSizeText));
                QueuePropertyPreview();
            }
        }
    }

    public string CanvasSizeText => $"{CanvasWidth}×{CanvasHeight}";

    public int OutputFps
    {
        get => _outputFps;
        set
        {
            var normalized = Math.Clamp(value, 1, 60);
            if (!SetProperty(ref _outputFps, normalized))
            {
                return;
            }

            ResnapTimelineForOutputChange();
            Status = "출력 FPS에 맞춰 영상과 재생 위치를 재정렬했습니다.";
            QueuePropertyPreview();
        }
    }
    public double OutputDurationSeconds
    {
        get => _outputDurationSeconds;
        set
        {
            if (SetProperty(ref _outputDurationSeconds, Math.Max(0.1, value)))
            {
                ResnapTimelineForOutputChange();
                OnPropertyChanged(nameof(OutputDurationText));
                Status = "새 종료 시각에 맞춰 타임라인 위치를 제한했습니다.";
                QueuePropertyPreview();
            }
        }
    }

    public string OutputDurationText => FormatTime(OutputDurationSeconds);
    public OutputQuality Quality
    {
        get => _quality;
        set
        {
            if (SetProperty(ref _quality, value))
            {
                QueuePropertyPreview();
            }
        }
    }

    public double PlayheadSeconds
    {
        get => _playheadSeconds;
        set => SetPlayhead(value, transitionFromStopped: true);
    }

    public string PlayheadText => $"{FormatTime(PlayheadSeconds)} / {FormatTime(OutputDurationSeconds)}";
    public double PlaybackStartSeconds => _playbackStartSeconds;
    public string PlaybackStartText => $"시작점 {FormatTime(PlaybackStartSeconds)}";
    public ImageSource? PreviewImage { get => _previewImage; private set => SetProperty(ref _previewImage, value); }
    public bool IsPreviewLoading { get => _isPreviewLoading; private set => SetProperty(ref _isPreviewLoading, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    internal void ReportInteraction(string status, string? error = null)
    {
        Status = status;
        if (error is not null) { ErrorMessage = error; }
    }
    public double ExportProgress { get => _exportProgress; private set => SetProperty(ref _exportProgress, value); }
    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(CanStartExport));
            }
        }
    }

    public PlaybackState CurrentPlaybackState => _playbackState;
    public bool IsPlaying => CurrentPlaybackState == PlaybackState.Playing;
    public bool IsEditingEnabled => !IsPlaying;
    public bool CanPlay => HasComposition && !IsPlaying;
    public bool CanPause => IsPlaying;
    public bool CanStop => HasComposition;
    public bool CanNavigate => HasComposition && !IsPlaying;
    public bool CanSetPlaybackStart => HasComposition && !IsPlaying;
    public bool CanStartExport => HasComposition && !IsExporting;
    public string PlaybackStateText => CurrentPlaybackState switch
    {
        PlaybackState.Playing => "재생 중",
        PlaybackState.Paused => "일시정지",
        _ => "정지"
    };

    public bool HasComposition => Video1.HasMedia && Video2.HasMedia;

    public VideoTrackViewModel SelectedVideo
    {
        get => _selectedVideo;
        private set
        {
            if (SetProperty(ref _selectedVideo, value))
            {
                OnPropertyChanged(nameof(SelectedVideoTitle));
            }
        }
    }

    public string SelectedVideoTitle => $"{SelectedVideo.Name} 속성";

    public async Task LoadFilesAsync(IEnumerable<string> paths)
    {
        if (IsPlaying)
        {
            return;
        }

        var hadComposition = HasComposition;
        ErrorMessage = "";
        foreach (var path in paths)
        {
            try
            {
                Status = $"분석 중: {Path.GetFileName(path)}";
                var media = await _probeService.ProbeAsync(path);
                if (media.Kind == MediaKind.Audio)
                {
                    Audio.SetMedia(media);
                    continue;
                }

                var target = !Video1.HasMedia ? Video1 : !Video2.HasMedia ? Video2 : SelectedVideo;
                var halfWidth = CanvasWidth / 2;
                var destination = ReferenceEquals(target, Video1)
                    ? new PixelRect(0, 0, halfWidth, CanvasHeight)
                    : new PixelRect(halfWidth, 0, CanvasWidth - halfWidth, CanvasHeight);
                target.SetMedia(media, destination);
                SelectVideo(target);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ErrorMessage = exception.Message;
            }
        }

        OnPropertyChanged(nameof(HasComposition));
        NotifyControlState();
        if (HasComposition)
        {
            var end = Math.Max(Video1.StartSeconds + Video1.DurationSeconds, Video2.StartSeconds + Video2.DurationSeconds);
            if (Audio.HasMedia)
            {
                end = Math.Max(end, Audio.StartSeconds + Audio.DurationSeconds);
            }

            OutputDurationSeconds = Math.Max(0.1, end);
            OutputFps = ChooseAutomaticFps(Math.Max(Video1.Media!.FramesPerSecond, Video2.Media!.FramesPerSecond));
            if (!hadComposition)
            {
                SetPlaybackStart(0);
                SetPlayhead(0, transitionFromStopped: false);
                SetPlaybackState(PlaybackState.Stopped);
            }

            Status = "미디어를 불러왔습니다.";
            await RefreshStillPreviewAsync();
        }
    }

    public void SelectVideo(VideoTrackViewModel track)
    {
        Video1.IsSelected = ReferenceEquals(track, Video1);
        Video2.IsSelected = ReferenceEquals(track, Video2);
        SelectedVideo = track;
    }

    public void MoveSelectedLayer(bool forward)
    {
        if (IsPlaying)
        {
            return;
        }

        if (forward)
        {
            SelectedVideo.ZIndex = 1;
            OtherVideo(SelectedVideo).ZIndex = 0;
        }
        else
        {
            SelectedVideo.ZIndex = 0;
            OtherVideo(SelectedVideo).ZIndex = 1;
        }
    }

    public void ApplyCanvasPreset(int width, int height)
    {
        if (IsPlaying)
        {
            return;
        }

        CanvasWidth = width;
        CanvasHeight = height;
    }

    public Composition BuildComposition()
    {
        if (!HasComposition)
        {
            throw new InvalidOperationException("MP4 영상 두 개가 필요합니다.");
        }

        var composition = new Composition(
            Video1.ToModel(),
            Video2.ToModel(),
            Audio.ToModel(),
            new(CanvasWidth, CanvasHeight, OutputFps, TimeSpan.FromSeconds(OutputDurationSeconds), Quality));
        var validation = CompositionValidator.Validate(composition);
        if (validation.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Select(issue => issue.Message)));
        }

        return composition;
    }

    public double SnapTrackStart(object? track, double seconds) => track switch
    {
        VideoTrackViewModel => PlaybackTimeline.SnapVideoStart(seconds, OutputFps, OutputDurationSeconds),
        AudioTrackViewModel => PlaybackTimeline.SnapAudioStart(seconds, OutputDurationSeconds),
        _ => 0
    };

    public void RequestTimelineDragPreview(object? track, double pendingStartSeconds)
    {
        if (IsPlaying || !HasComposition || track is not VideoTrackViewModel)
        {
            return;
        }

        _pendingInteractiveTrack = track;
        _pendingInteractiveStart = SnapTrackStart(track, pendingStartSeconds);
        _hasInteractivePreviewPending = true;
        _interactivePreviewRevision++;
        ScheduleInteractivePreview();
    }

    public void RequestPlayheadPreview()
    {
        if (IsPlaying || !HasComposition)
        {
            return;
        }

        _pendingInteractiveTrack = null;
        _pendingInteractiveStart = 0;
        _hasInteractivePreviewPending = true;
        _interactivePreviewRevision++;
        ScheduleInteractivePreview();
    }

    public async Task CommitTimelineTrackStartAsync(object? track, double seconds)
    {
        if (IsPlaying)
        {
            return;
        }

        CancelInteractivePreview();
        var snapped = SnapTrackStart(track, seconds);
        _suppressTrackPreview = true;
        try
        {
            SetTrackStart(track, snapped);
        }
        finally
        {
            _suppressTrackPreview = false;
        }

        Status = $"시작 시각을 {snapped:F3}초로 확정했습니다.";
        await RefreshStillPreviewAsync();
    }

    public void CancelTimelineDragPreview() => CancelInteractivePreview();

    public void BeginTimelineInteraction()
    {
        _propertyPreviewDebounceCancellation?.Cancel();
        _navigationPreviewCancellation?.Cancel();
        _stillCancellation?.Cancel();
    }

    public async Task RefreshStillPreviewAsync()
    {
        if (!HasComposition || IsPlaying || _disposed)
        {
            return;
        }

        CancelInteractivePreview();
        _stillCancellation?.Cancel();
        _stillCancellation?.Dispose();
        _stillCancellation = new();
        try
        {
            await _playerPreview.ShowStillAsync(BuildComposition(), PlayheadSeconds, _stillCancellation.Token);
            Status = "원본 정지 프레임 · 화면 배치 미리보기";
            ErrorMessage = "";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public Task<byte[]> GetSourceFramePngAsync(VideoTrackViewModel track, CancellationToken cancellationToken = default)
    {
        if (track.Media is null)
        {
            throw new InvalidOperationException("ROI를 편집할 영상이 없습니다.");
        }

        return _previewService.GetSourceFramePngAsync(track.Media, 0, cancellationToken);
    }

    public async Task ApplyRoiAsync(VideoTrackViewModel track, PixelRect roi)
    {
        if (IsPlaying || track.Media is null || _disposed) { return; }
        _propertyPreviewDebounceCancellation?.Cancel();
        _suppressTrackPreview = true;
        try { track.SetRoi(roi); }
        finally { _suppressTrackPreview = false; }
        await RefreshStillPreviewAsync();
    }

    public void SetRoiPreviewFocus(VideoTrackViewModel? track)
    {
        if (!_disposed && (track is null || !IsPlaying))
        {
            _playerPreview.SetRoiFocus(track is null ? null : ReferenceEquals(track, Video1) ? 1 : 2);
        }
    }

    private bool TryBuildComposition(out Composition composition)
    {
        try
        {
            composition = BuildComposition();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OverflowException)
        {
            composition = null!;
            ErrorMessage = exception.Message;
            Status = "설정값을 확인해 주세요.";
            return false;
        }
    }

    public Task StartPlaybackAsync()
    {
        if (IsPlaying)
        {
            return Task.CompletedTask;
        }

        if (!HasComposition)
        {
            ErrorMessage = "MP4 영상 두 개가 필요합니다.";
            return Task.CompletedTask;
        }

        if (!TryBuildComposition(out var composition)) { return Task.CompletedTask; }

        CancelInteractivePreview();
        _stillCancellation?.Cancel();
        _propertyPreviewDebounceCancellation?.Cancel();
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new();
        if (IsAtPlaybackEnd())
        {
            SetPlayhead(PlaybackStartSeconds, transitionFromStopped: false);
        }

        var fromSeconds = PlayheadSeconds;
        var previewCancellation = _previewCancellation;
        SetPlaybackState(PlaybackState.Playing);
        IsPreviewLoading = true;
        Status = "개별 플레이어와 공통 오디오 믹스 재생 준비 중";
        ErrorMessage = "";
        _ = RunPlaybackAsync(composition, fromSeconds, previewCancellation);
        return Task.CompletedTask;
    }

    public async Task PausePlaybackAsync()
    {
        if (!IsPlaying)
        {
            return;
        }

        _previewCancellation?.Cancel();
        _playerPreview.PauseImmediately();
        IsPreviewLoading = false;
        SetPlaybackState(PlaybackState.Paused);
        Status = "일시정지";
        await RefreshStillPreviewAsync();
    }

    public async Task StopPlaybackAsync()
    {
        _previewCancellation?.Cancel();
        _playerPreview.PauseImmediately();
        IsPreviewLoading = false;
        SetPlayhead(PlaybackStartSeconds, transitionFromStopped: false);
        SetPlaybackState(PlaybackState.Stopped);
        Status = $"지정 시작점 {FormatTime(PlaybackStartSeconds)}에서 정지";
        await RefreshStillPreviewAsync();
    }

    public async Task SetPlaybackStartAsync()
    {
        if (!CanSetPlaybackStart)
        {
            return;
        }

        SetPlaybackStart(PlaybackTimeline.SnapPlayhead(PlayheadSeconds, OutputFps, OutputDurationSeconds));
        SetPlayhead(PlaybackStartSeconds, transitionFromStopped: false);
        SetPlaybackState(PlaybackState.Stopped);
        Status = $"재생 시작점을 {FormatTime(PlaybackStartSeconds)}로 지정했습니다.";
        await RefreshStillPreviewAsync();
    }

    public async Task StepFrameAsync(int direction)
    {
        if (!CanNavigate || direction == 0)
        {
            return;
        }

        var target = PlayheadSeconds + Math.Sign(direction) / (double)OutputFps;
        SetPlayhead(target, transitionFromStopped: true);
        Status = $"프레임 이동 · {FormatTime(PlayheadSeconds)}";
        await RefreshNavigationPreviewAfterIdleAsync();
    }

    public Task ExportAsync(string outputPath, bool overwrite = false)
    {
        return TryBuildComposition(out var snapshot) ? ExportAsync(snapshot, outputPath, overwrite) : Task.CompletedTask;
    }

    public Task ExportAsync(Composition snapshot, string outputPath, bool overwrite = false)
    {
        if (IsExporting)
        {
            return _activeExportTask ?? Task.CompletedTask;
        }

        ErrorMessage = "";
        ExportProgress = 0;
        IsExporting = true;
        _exportCancellation = new();
        _activeExportTask = RunExportAsync(snapshot, outputPath, overwrite, _exportCancellation);
        return _activeExportTask;
    }

    public void CancelExport() => _exportCancellation?.Cancel();

    public async Task CancelExportAndWaitAsync()
    {
        CancelExport();
        if (_activeExportTask is { } activeExport)
        {
            await activeExport;
        }
    }

    private async Task RunPlaybackAsync(Composition composition, double fromSeconds, CancellationTokenSource previewCancellation)
    {
        var completedNaturally = false;
        var token = previewCancellation.Token;
        try
        {
            await _playerPreview.RunAsync(composition, fromSeconds,
                time =>
                {
                    if (!token.IsCancellationRequested && ReferenceEquals(_previewCancellation, previewCancellation))
                    {
                        SetPlayhead(time, transitionFromStopped: false);
                    }
                },
                loading =>
                {
                    if (!token.IsCancellationRequested && ReferenceEquals(_previewCancellation, previewCancellation))
                    {
                        IsPreviewLoading = loading;
                        Status = loading ? "재생 동기화 중…" : "개별 플레이어 재생 중 · " + _playerPreview.Backends;
                    }
                },
                token);
            completedNaturally = !previewCancellation.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested && ReferenceEquals(_previewCancellation, previewCancellation))
            {
                ErrorMessage = exception.Message;
            }
        }
        finally
        {
            if (!_disposed && ReferenceEquals(_previewCancellation, previewCancellation))
            {
                IsPreviewLoading = false;
                if (completedNaturally)
                {
                    SetPlayhead(PlaybackTimeline.LastFrameSeconds(OutputDurationSeconds, OutputFps), transitionFromStopped: false);
                    SetPlaybackState(PlaybackState.Paused);
                    Status = "출력 종료 프레임에서 일시정지";
                    _ = RefreshStillPreviewAsync();
                }
                else if (!token.IsCancellationRequested)
                {
                    SetPlaybackState(PlaybackState.Paused);
                }
            }
        }
    }

    private async Task RunExportAsync(Composition snapshot, string outputPath, bool overwrite, CancellationTokenSource exportCancellation)
    {
        try
        {
            var progress = new Progress<ExportProgress>(item =>
            {
                ExportProgress = item.Fraction * 100;
                Status = $"{item.Message} · {item.Encoder} · {item.Processed:mm\\:ss}";
            });
            using var labels = Services.ExportLabelRenderer.Create(snapshot);
            var result = await _exportService.ExportAsync(snapshot, outputPath, progress, exportCancellation.Token, overwrite, labels.Assets);
            Status = $"완료: {Path.GetFileName(result.OutputPath)} ({result.Encoder})";
        }
        catch (OperationCanceledException)
        {
            Status = "내보내기를 취소했습니다.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            Status = "내보내기에 실패했습니다.";
        }
        finally
        {
            exportCancellation.Dispose();
            if (ReferenceEquals(_exportCancellation, exportCancellation))
            {
                _exportCancellation = null;
                _activeExportTask = null;
                IsExporting = false;
            }
        }
    }

    private async Task RefreshNavigationPreviewAfterIdleAsync()
    {
        _navigationPreviewCancellation?.Cancel();
        _navigationPreviewCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _navigationPreviewCancellation = cancellation;
        try
        {
            await Task.Delay(150, cancellation.Token);
            await RefreshStillPreviewAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ScheduleInteractivePreview()
    {
        if (_interactivePreviewScheduled || _interactivePreviewInFlight || _disposed)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - _lastInteractivePreviewStarted;
        var delay = elapsed >= TimeSpan.FromMilliseconds(100)
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(100) - elapsed;
        _interactivePreviewScheduled = true;
        _ = RunInteractivePreviewAsync(delay);
    }

    private async Task RunInteractivePreviewAsync(TimeSpan delay)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay);
        }

        _interactivePreviewScheduled = false;
        if (!_hasInteractivePreviewPending || IsPlaying || !HasComposition || _disposed)
        {
            return;
        }

        _hasInteractivePreviewPending = false;
        _interactivePreviewInFlight = true;
        var revision = _interactivePreviewRevision;
        var track = _pendingInteractiveTrack;
        var pendingStart = _pendingInteractiveStart;
        _interactivePreviewCancellation?.Cancel();
        _interactivePreviewCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _interactivePreviewCancellation = cancellation;
        _lastInteractivePreviewStarted = DateTimeOffset.UtcNow;

        try
        {
            var composition = BuildCompositionWithTemporaryStart(track, pendingStart);
            await _playerPreview.ShowStillAsync(composition, PlayheadSeconds, cancellation.Token,
                () => revision == _interactivePreviewRevision);
            if (revision == _interactivePreviewRevision && !cancellation.IsCancellationRequested && !IsPlaying)
            {
                Status = "개별 프레임 탐색 · 드롭 시 확정";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (revision == _interactivePreviewRevision)
            {
                ErrorMessage = exception.Message;
            }
        }
        finally
        {
            _interactivePreviewInFlight = false;
            if (_hasInteractivePreviewPending)
            {
                ScheduleInteractivePreview();
            }
        }
    }

    private Composition BuildCompositionWithTemporaryStart(object? track, double pendingStart)
    {
        var composition = BuildComposition();
        return track switch
        {
            VideoTrackViewModel video when ReferenceEquals(video, Video1) => composition with
            {
                Video1 = composition.Video1 with { Start = TimeSpan.FromSeconds(pendingStart) }
            },
            VideoTrackViewModel video when ReferenceEquals(video, Video2) => composition with
            {
                Video2 = composition.Video2 with { Start = TimeSpan.FromSeconds(pendingStart) }
            },
            _ => composition
        };
    }

    private void CancelInteractivePreview()
    {
        _hasInteractivePreviewPending = false;
        _pendingInteractiveTrack = null;
        _interactivePreviewRevision++;
        _interactivePreviewCancellation?.Cancel();
    }

    private void SubscribeTrack(VideoTrackViewModel track) => track.PropertyChanged += TrackPropertyChanged;

    private void TrackPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_suppressTrackPreview || eventArgs.PropertyName is nameof(VideoTrackViewModel.IsSelected) or nameof(VideoTrackViewModel.DisplayName) or nameof(VideoTrackViewModel.Details))
        {
            return;
        }

        if (eventArgs.PropertyName == nameof(VideoTrackViewModel.StartSeconds))
        {
            var snapped = SnapTrackStart(sender, GetTrackStart(sender));
            _suppressTrackPreview = true;
            try
            {
                SetTrackStart(sender, snapped);
            }
            finally
            {
                _suppressTrackPreview = false;
            }
        }

        if (sender is AudioTrackViewModel ||
            eventArgs.PropertyName is nameof(VideoTrackViewModel.IncludeAudio) or nameof(VideoTrackViewModel.VolumePercent))
        {
            return;
        }

        var roiChanged = eventArgs.PropertyName is nameof(VideoTrackViewModel.RoiX) or nameof(VideoTrackViewModel.RoiY)
            or nameof(VideoTrackViewModel.RoiWidth) or nameof(VideoTrackViewModel.RoiHeight);
        QueuePropertyPreview(roiChanged ? 1 : 180);
    }

    private void QueuePropertyPreview(int delayMilliseconds = 180)
    {
        if (IsPlaying || _disposed)
        {
            return;
        }

        _propertyPreviewDebounceCancellation?.Cancel();
        _propertyPreviewDebounceCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _propertyPreviewDebounceCancellation = cancellation;
        _ = DebouncedPreviewAsync(cancellation, delayMilliseconds);
    }

    private async Task DebouncedPreviewAsync(CancellationTokenSource cancellation, int delayMilliseconds)
    {
        try
        {
            await Task.Delay(delayMilliseconds, cancellation.Token);
            if (!IsPlaying && ReferenceEquals(_propertyPreviewDebounceCancellation, cancellation))
            {
                await RefreshStillPreviewAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ResnapTimelineForOutputChange()
    {
        _suppressTrackPreview = true;
        try
        {
            Video1.StartSeconds = PlaybackTimeline.SnapVideoStart(Video1.StartSeconds, OutputFps, OutputDurationSeconds);
            Video2.StartSeconds = PlaybackTimeline.SnapVideoStart(Video2.StartSeconds, OutputFps, OutputDurationSeconds);
            Audio.StartSeconds = PlaybackTimeline.SnapAudioStart(Audio.StartSeconds, OutputDurationSeconds);
        }
        finally
        {
            _suppressTrackPreview = false;
        }

        SetPlaybackStart(PlaybackTimeline.SnapPlayhead(PlaybackStartSeconds, OutputFps, OutputDurationSeconds));
        if (CurrentPlaybackState == PlaybackState.Stopped)
        {
            SetPlayhead(PlaybackStartSeconds, transitionFromStopped: false);
        }
        else
        {
            SetPlayhead(PlayheadSeconds, transitionFromStopped: false);
        }
    }

    private void SetPlaybackState(PlaybackState state)
    {
        if (_playbackState == state)
        {
            return;
        }

        _playbackState = state;
        OnPropertyChanged(nameof(CurrentPlaybackState));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlaybackStateText));
        NotifyControlState();
    }

    private void NotifyControlState()
    {
        OnPropertyChanged(nameof(IsEditingEnabled));
        OnPropertyChanged(nameof(CanPlay));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(CanSetPlaybackStart));
        OnPropertyChanged(nameof(CanStartExport));
    }

    private void SetPlaybackStart(double seconds)
    {
        var snapped = PlaybackTimeline.SnapPlayhead(seconds, OutputFps, OutputDurationSeconds);
        if (SetProperty(ref _playbackStartSeconds, snapped, nameof(PlaybackStartSeconds)))
        {
            OnPropertyChanged(nameof(PlaybackStartText));
        }
    }

    private void SetPlayhead(double seconds, bool transitionFromStopped)
    {
        var snapped = PlaybackTimeline.SnapPlayhead(seconds, OutputFps, OutputDurationSeconds);
        if (!SetProperty(ref _playheadSeconds, snapped, nameof(PlayheadSeconds)))
        {
            return;
        }

        OnPropertyChanged(nameof(PlayheadText));
        if (transitionFromStopped && CurrentPlaybackState == PlaybackState.Stopped)
        {
            SetPlaybackState(PlaybackState.Paused);
        }
    }

    private bool IsAtPlaybackEnd()
    {
        var lastFrame = PlaybackTimeline.LastFrameSeconds(OutputDurationSeconds, OutputFps);
        return PlayheadSeconds >= lastFrame - 0.5 / OutputFps;
    }

    private static void SetTrackStart(object? track, double seconds)
    {
        switch (track)
        {
            case VideoTrackViewModel video:
                video.StartSeconds = seconds;
                break;
            case AudioTrackViewModel audio:
                audio.StartSeconds = seconds;
                break;
        }
    }

    private static double GetTrackStart(object? track) => track switch
    {
        VideoTrackViewModel video => video.StartSeconds,
        AudioTrackViewModel audio => audio.StartSeconds,
        _ => 0
    };

    private void ClampDestinations()
    {
        foreach (var track in new[] { Video1, Video2 })
        {
            track.DestinationWidth = Math.Min(track.DestinationWidth, CanvasWidth);
            track.DestinationHeight = Math.Min(track.DestinationHeight, CanvasHeight);
            track.DestinationX = Math.Min(track.DestinationX, Math.Max(0, CanvasWidth - track.DestinationWidth));
            track.DestinationY = Math.Min(track.DestinationY, Math.Max(0, CanvasHeight - track.DestinationHeight));
        }
    }

    private VideoTrackViewModel OtherVideo(VideoTrackViewModel track) => ReferenceEquals(track, Video1) ? Video2 : Video1;

    private static int ChooseAutomaticFps(double sourceFps)
    {
        foreach (var candidate in new[] { 24, 25, 30, 50, 60 })
        {
            if (sourceFps <= candidate + 0.1)
            {
                return candidate;
            }
        }

        return 60;
    }

    private static string FormatTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"mm\:ss\.fff");

    public void Dispose()
    {
        _disposed = true;
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _stillCancellation?.Cancel();
        _stillCancellation?.Dispose();
        _propertyPreviewDebounceCancellation?.Cancel();
        _propertyPreviewDebounceCancellation?.Dispose();
        _navigationPreviewCancellation?.Cancel();
        _navigationPreviewCancellation?.Dispose();
        _interactivePreviewCancellation?.Cancel();
        _interactivePreviewCancellation?.Dispose();
        _exportCancellation?.Cancel();
        _exportCancellation?.Dispose();
        _playerPreview.Dispose();
    }
}
