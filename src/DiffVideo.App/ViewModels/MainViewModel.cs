using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiffVideo.App.Services;
using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.App.ViewModels;

public enum PlaybackScope { Segment, Full }

public sealed partial class MainViewModel : ObservableObject, IDisposable
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
    private double _fullPlayheadSeconds;
    private PlaybackScope _activePlaybackScope = PlaybackScope.Segment;
    private PlaybackScope? _playingScope;
    private ImageSource? _previewImage;
    private string _status = "MP4 두 개를 끌어 놓으세요.";
    private string _errorMessage = "";
    private double _exportProgress;
    private bool _isExporting;
    private PlaybackState _playbackState = PlaybackState.Stopped;
    private VideoTrackViewModel? _selectedVideo;
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
        Videos.CollectionChanged += (_, _) => RebuildTimelineTracks();
        Audios.CollectionChanged += (_, _) => RebuildTimelineTracks();
    }

    // ponytail: UI-only ceilings. Core counts no tracks at all, so raising these when hardware
    // allows is a one-line change with no engine work.
    public const int MaximumVideoTracks = 4;
    public const int MaximumAudioTracks = 4;

    /// <summary>Layer order: the first entry draws in front of the rest.</summary>
    public ObservableCollection<VideoTrackViewModel> Videos { get; } = [];

    /// <summary>Audio-only sources. They carry no placement, so they have no layer order.</summary>
    public ObservableCollection<AudioTrackViewModel> Audios { get; } = [];

    public bool CanAddAudio => Audios.Count < MaximumAudioTracks;

    /// <summary>
    /// Every timeline row in display order: videos first, then audio. Both the fixed name column
    /// and the scrolling clip column bind to this one list, so their rows line up by construction.
    /// Media kind decides the block, which is why the two never interleave.
    /// </summary>
    public ObservableCollection<object> TimelineTracks { get; } = [];

    private void RebuildTimelineTracks()
    {
        TimelineTracks.Clear();
        foreach (var video in Videos)
        {
            TimelineTracks.Add(video);
        }

        foreach (var audio in Audios)
        {
            TimelineTracks.Add(audio);
        }
    }

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
                OnPropertyChanged(nameof(CanvasSizeText));
                RelayoutGrid();
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
                OnPropertyChanged(nameof(CanvasSizeText));
                RelayoutGrid();
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
            if (SetProperty(ref _outputDurationSeconds, Math.Max(0.01, value)))
            {
                ResnapTimelineForOutputChange();
                OnPropertyChanged(nameof(OutputDurationText));
                OnPropertyChanged(nameof(PlayheadText));
                OnPropertyChanged(nameof(SegmentTimeText));
                OnPropertyChanged(nameof(FullTimeText));
                OnPropertyChanged(nameof(ExportEndText));
                Status = "새 종료 시각에 맞춰 타임라인 위치를 제한했습니다.";
                QueuePropertyPreview();
            }
        }
    }

    public string OutputDurationText => FormatTime(OutputDurationSeconds);
    public bool TryApplyOutputDuration(string text)
    {
        if (IsPlaying) { return false; }
        if (!TryParseDurationSeconds(text, out var seconds))
        {
            ErrorMessage = "전체 길이는 0.01초 이상의 초 또는 mm:ss.fff 형식으로 입력하세요.";
            return false;
        }

        ErrorMessage = "";
        OutputDurationSeconds = seconds;
        Status = $"전체 길이를 {OutputDurationText}(으)로 설정했습니다.";
        return true;
    }

    public bool TryApplyExportBoundary(bool start, string text)
    {
        if (!CanEditExportRange || !TryParseDurationSeconds(text, out var seconds, 0))
        {
            ErrorMessage = "구간 경계는 0초 이상의 초 또는 mm:ss.fff 형식으로 입력하세요.";
            return false;
        }

        ErrorMessage = "";
        SetExportBoundary(start, seconds);
        Status = $"내보내기 {(start ? "시작" : "종료")} 경계를 {FormatTime(start ? ExportStartSeconds : ExportEndSeconds)}(으)로 설정했습니다.";
        return true;
    }

    internal static bool TryParseDurationSeconds(string text, out double seconds, double minimum = 0.01)
    {
        text = text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            return double.IsFinite(seconds) && seconds >= minimum;
        }

        var parts = text.Split(':');
        var secondsCulture = double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.CurrentCulture, out var finalSeconds) ||
            double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out finalSeconds);
        var valid = parts.Length switch
        {
            2 => int.TryParse(parts[0], out var minutes) && minutes >= 0 && secondsCulture && finalSeconds is >= 0 and < 60,
            3 => int.TryParse(parts[0], out var hours) && hours >= 0 && int.TryParse(parts[1], out var minutes) && minutes is >= 0 and < 60 && secondsCulture && finalSeconds is >= 0 and < 60,
            _ => false
        };
        seconds = valid
            ? parts.Length == 2
                ? int.Parse(parts[0], CultureInfo.InvariantCulture) * 60d + finalSeconds
                : int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600d + int.Parse(parts[1], CultureInfo.InvariantCulture) * 60d + finalSeconds
            : 0;
        return valid && double.IsFinite(seconds) && seconds >= minimum;
    }

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

    public double FullPlayheadSeconds
    {
        get => _fullPlayheadSeconds;
        set => SetPlayhead(PlaybackScope.Full, value, transitionFromStopped: true);
    }

    public double ActivePlayheadSeconds => _activePlaybackScope == PlaybackScope.Segment ? PlayheadSeconds : FullPlayheadSeconds;
    public string SegmentHeadText => FormatTime(PlayheadSeconds);
    public string FullHeadText => FormatTime(FullPlayheadSeconds);
    public string ExportStartText => FormatTime(ExportStartSeconds);
    public string ExportEndText => FormatTime(ExportEndSeconds);
    public string SegmentTimeText => $"{FormatTime(PlayheadSeconds)} / {FormatTime(ExportStartSeconds)} ~ {FormatTime(ExportEndSeconds)}";
    public string FullTimeText => $"{FormatTime(FullPlayheadSeconds)} / {OutputDurationText}";
    public string CurrentTimeText => FormatTime(ActivePlayheadSeconds);
    public string PlayheadText => SegmentTimeText;
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
    public bool CanPlay => CanPlaySegment;
    public bool CanPause => CanPauseSegment;
    public bool CanStop => HasComposition;
    public bool CanNavigate => HasComposition && !IsPlaying;
    public bool CanPlaySegment => HasComposition && !IsPlaying;
    public bool CanPauseSegment => IsPlaying && _playingScope == PlaybackScope.Segment;
    public bool CanUseSegmentControls => HasComposition && (!IsPlaying || _playingScope == PlaybackScope.Segment);
    public bool CanPlayFull => HasComposition && !IsPlaying;
    public bool CanPauseFull => IsPlaying && _playingScope == PlaybackScope.Full;
    public bool CanUseFullControls => HasComposition && (!IsPlaying || _playingScope == PlaybackScope.Full);
    public bool CanStartExport => HasComposition && !IsExporting;
    public string PlaybackStateText => CurrentPlaybackState switch
    {
        PlaybackState.Playing => "재생 중",
        PlaybackState.Paused => "일시정지",
        _ => "정지"
    };

    public bool HasComposition => Videos.Count > 0 || Audios.Count > 0;

    public bool CanAddVideo => Videos.Count < MaximumVideoTracks;

    public VideoTrackViewModel? SelectedVideo
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

    public string SelectedVideoTitle => SelectedVideo is null ? "영상 속성" : $"{SelectedVideo.Name} 속성";

    public Task LoadFilesAsync(IEnumerable<string> paths) => LoadFilesCoreAsync(paths, null);

    public Task LoadVideoAsync(VideoTrackViewModel target, string path) => LoadFilesCoreAsync([path], target);

    private async Task LoadFilesCoreAsync(IEnumerable<string> paths, VideoTrackViewModel? requestedVideo)
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
                    if (requestedVideo is not null) { throw new InvalidOperationException("영상 트랙에는 MP4 영상만 불러올 수 있습니다."); }
                    if (!CanAddAudio)
                    {
                        throw new InvalidOperationException($"음원은 최대 {MaximumAudioTracks}개까지 추가할 수 있습니다.");
                    }

                    var track = new AudioTrackViewModel();
                    track.PropertyChanged += TrackPropertyChanged;
                    track.SetMedia(media);
                    Audios.Add(track);
                    OnPropertyChanged(nameof(CanAddAudio));
                    continue;
                }

                var target = requestedVideo;
                if (target is null)
                {
                    if (!CanAddVideo)
                    {
                        throw new InvalidOperationException($"영상은 최대 {MaximumVideoTracks}개까지 추가할 수 있습니다.");
                    }

                    target = new VideoTrackViewModel(TrackName(Videos.Count));
                    target.PropertyChanged += TrackPropertyChanged;
                    Videos.Add(target);
                    RenumberTracks();
                    OnPropertyChanged(nameof(CanAddVideo));
                }

                var destination = target.HasMedia
                    ? new PixelRect(target.DestinationX, target.DestinationY, target.DestinationWidth, target.DestinationHeight)
                    : new PixelRect(0, 0, CanvasWidth, CanvasHeight);
                target.SetMedia(media, destination);
                RelayoutGrid();
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
            var end = Videos.Count == 0 ? 0 : Videos.Max(video => video.StartSeconds + video.DurationSeconds);
            foreach (var audio in Audios)
            {
                end = Math.Max(end, audio.StartSeconds + audio.DurationSeconds);
            }

            OutputDurationSeconds = Math.Max(0.01, end);
            if (Videos.Count > 0)
            {
                OutputFps = ChooseAutomaticFps(Videos.Max(video => video.Media!.FramesPerSecond));
            }
            if (!hadComposition)
            {
                SetPlayhead(PlaybackScope.Segment, ExportStartSeconds, transitionFromStopped: false);
                SetPlayhead(PlaybackScope.Full, 0, transitionFromStopped: false);
                SetPlaybackState(PlaybackState.Stopped);
            }

            Status = "미디어를 불러왔습니다.";
            await RefreshStillPreviewAsync();
        }
    }

    public void SelectVideo(VideoTrackViewModel? track)
    {
        foreach (var video in Videos)
        {
            video.IsSelected = ReferenceEquals(video, track);
        }

        SelectedVideo = track;
    }

    /// <summary>Row order is layer order, so moving a layer is moving its row.</summary>
    public void MoveSelectedLayer(bool forward)
    {
        if (SelectedVideo is not { } selected) { return; }
        var from = Videos.IndexOf(selected);
        MoveVideo(from, from + (forward ? -1 : 1));
    }

    public void MoveVideo(int from, int to)
    {
        if (!IsEditingEnabled || from == to || from < 0 || to < 0 || from >= Videos.Count || to >= Videos.Count)
        {
            return;
        }

        Videos.Move(from, to);
        RenumberTracks();
        UpdatePlacementPreview();
        Status = $"{Videos[to].Name}을(를) {to + 1}번째 레이어로 옮겼습니다.";
    }

    /// <summary>Audio rows are removed the same way video rows are.</summary>
    public void RemoveAudio(AudioTrackViewModel track)
    {
        if (!IsEditingEnabled || !Audios.Remove(track))
        {
            return;
        }

        track.PropertyChanged -= TrackPropertyChanged;
        OnPropertyChanged(nameof(HasComposition));
        OnPropertyChanged(nameof(CanAddAudio));
        NotifyControlState();
        Status = "음원 트랙을 제거했습니다.";
        _ = RefreshStillPreviewAsync();
    }

    /// <summary>Removing the last track is allowed; it returns the editor to its empty state.</summary>
    public void RemoveVideo(VideoTrackViewModel track)
    {
        if (!IsEditingEnabled)
        {
            return;
        }

        if (!Videos.Remove(track))
        {
            return;
        }

        track.PropertyChanged -= TrackPropertyChanged;

        RenumberTracks();
        RelayoutGrid();
        if (ReferenceEquals(SelectedVideo, track))
        {
            SelectVideo(Videos.FirstOrDefault());
        }

        OnPropertyChanged(nameof(HasComposition));
        OnPropertyChanged(nameof(CanAddVideo));
        NotifyControlState();
        Status = "영상 트랙을 제거했습니다.";
        _ = RefreshStillPreviewAsync();
    }

    private static string TrackName(int index) => $"영상 {index + 1}";

    private void RenumberTracks()
    {
        for (var index = 0; index < Videos.Count; index++)
        {
            Videos[index].Name = TrackName(index);
            // The first row draws in front, so row order inverts into layer order.
            Videos[index].ZIndex = Videos.Count - 1 - index;
        }

        OnPropertyChanged(nameof(SelectedVideoTitle));
    }

    /// <summary>
    /// Lays uncustomized tracks out on a ceil(sqrt(N)) grid. Two videos land left and right,
    /// exactly as they did before. Hand-placed tracks are skipped: automatic layout must never
    /// destroy placement work.
    /// </summary>
    private void RelayoutGrid()
    {
        if (Videos.Count == 0)
        {
            return;
        }

        for (var index = 0; index < Videos.Count; index++)
        {
            if (Videos[index].IsPlacementCustomized)
            {
                continue;
            }

            Videos[index].ApplyLayoutDestination(GridLayout.Cell(index, Videos.Count, CanvasWidth, CanvasHeight));
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
            throw new InvalidOperationException("영상 또는 음원이 최소 한 개 필요합니다.");
        }

        var composition = new Composition(
            Videos.Select(video => video.ToModel()).ToArray(),
            Audios.Select(audio => audio.ToModel()).OfType<AudioTrack>().ToArray(),
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
            await _playerPreview.ShowStillAsync(BuildComposition(), ActivePlayheadSeconds, _stillCancellation.Token);
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
            var index = track is null ? -1 : Videos.IndexOf(track);
            _playerPreview.SetRoiFocus(index < 0 ? null : index);
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

    public Task StartPlaybackAsync() => StartPlaybackAsync(PlaybackScope.Segment);
    public Task StartFullPlaybackAsync() => StartPlaybackAsync(PlaybackScope.Full);

    private Task StartPlaybackAsync(PlaybackScope scope)
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
        _activePlaybackScope = scope;
        var startSeconds = PlaybackStart(scope);
        var endSeconds = PlaybackEnd(scope);
        var currentSeconds = PlaybackHead(scope);
        if (currentSeconds < startSeconds || IsAtPlaybackEnd(scope))
        {
            SetPlayhead(scope, startSeconds, transitionFromStopped: false);
        }

        var fromSeconds = PlaybackHead(scope);
        var previewCancellation = _previewCancellation;
        _playingScope = scope;
        SetPlaybackState(PlaybackState.Playing);
        IsPreviewLoading = true;
        Status = "개별 플레이어와 공통 오디오 믹스 재생 준비 중";
        ErrorMessage = "";
        _ = RunPlaybackAsync(composition, fromSeconds, endSeconds, scope, previewCancellation);
        return Task.CompletedTask;
    }

    public Task PausePlaybackAsync() => PausePlaybackAsync(PlaybackScope.Segment);
    public Task PauseFullPlaybackAsync() => PausePlaybackAsync(PlaybackScope.Full);

    private async Task PausePlaybackAsync(PlaybackScope scope)
    {
        if (!IsPlaying || _playingScope != scope)
        {
            return;
        }

        _previewCancellation?.Cancel();
        _playerPreview.PauseImmediately();
        IsPreviewLoading = false;
        _playingScope = null;
        SetPlaybackState(PlaybackState.Paused);
        Status = "일시정지";
        await RefreshStillPreviewAsync();
    }

    public Task StopPlaybackAsync() => StopPlaybackAsync(PlaybackScope.Segment);
    public Task StopFullPlaybackAsync() => StopPlaybackAsync(PlaybackScope.Full);

    private async Task StopPlaybackAsync(PlaybackScope scope)
    {
        if (IsPlaying && _playingScope != scope) { return; }
        _previewCancellation?.Cancel();
        _playerPreview.PauseImmediately();
        IsPreviewLoading = false;
        _playingScope = null;
        SetPlayhead(scope, PlaybackStart(scope), transitionFromStopped: false);
        SetPlaybackState(PlaybackState.Stopped);
        Status = $"{(scope == PlaybackScope.Segment ? "구간" : "전체")} 시작에서 정지";
        await RefreshStillPreviewAsync();
    }

    public Task StepFrameAsync(int direction) => StepFrameAsync(PlaybackScope.Segment, direction);
    public Task StepFullFrameAsync(int direction) => StepFrameAsync(PlaybackScope.Full, direction);

    private async Task StepFrameAsync(PlaybackScope scope, int direction)
    {
        if (!CanNavigate || direction == 0)
        {
            return;
        }

        var target = PlaybackHead(scope) + Math.Sign(direction) / (double)OutputFps;
        SetPlayhead(scope, target, transitionFromStopped: true);
        Status = $"{(scope == PlaybackScope.Segment ? "구간" : "전체")} 프레임 이동 · {FormatTime(PlaybackHead(scope))}";
        await RefreshNavigationPreviewAfterIdleAsync();
    }

    public Task ExportAsync(string outputPath, bool overwrite = false)
    {
        return TryBuildComposition(out var snapshot) ? ExportAsync(WithExportRange(snapshot), outputPath, overwrite) : Task.CompletedTask;
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

    private async Task RunPlaybackAsync(Composition composition, double fromSeconds, double endSeconds, PlaybackScope scope, CancellationTokenSource previewCancellation)
    {
        var completedNaturally = false;
        var reachedScopeEnd = false;
        var token = previewCancellation.Token;
        try
        {
            await _playerPreview.RunAsync(composition, fromSeconds,
                time =>
                {
                    if (!token.IsCancellationRequested && ReferenceEquals(_previewCancellation, previewCancellation))
                    {
                        if (time >= endSeconds)
                        {
                            reachedScopeEnd = true;
                            SetPlayhead(scope, endSeconds, transitionFromStopped: false);
                            previewCancellation.Cancel();
                        }
                        else { SetPlayhead(scope, time, transitionFromStopped: false); }
                        PublishTrackLag();
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
                ClearTrackLag();
                ReportLaggingTracks();
                if (completedNaturally || reachedScopeEnd)
                {
                    SetPlayhead(scope, endSeconds, transitionFromStopped: false);
                    _playingScope = null;
                    SetPlaybackState(PlaybackState.Paused);
                    Status = $"{(scope == PlaybackScope.Segment ? "구간" : "전체")} 종료 프레임에서 일시정지";
                    _ = RefreshStillPreviewAsync();
                }
                else if (!token.IsCancellationRequested)
                {
                    _playingScope = null;
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
            await _playerPreview.ShowStillAsync(composition, ActivePlayheadSeconds, cancellation.Token,
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
        if (track is not VideoTrackViewModel video)
        {
            return composition;
        }

        var index = Videos.IndexOf(video);
        return index < 0
            ? composition
            : composition.WithVideo(index, composition.Videos[index] with { Start = TimeSpan.FromSeconds(pendingStart) });
    }

    /// <summary>Mirrors the session flags onto the tracks so each badge follows its own player.</summary>
    private void PublishTrackLag()
    {
        var lagging = _playerPreview.TrackLagging;
        for (var index = 0; index < Videos.Count && index < lagging.Count; index++)
        {
            Videos[index].IsLagging = lagging[index];
        }
    }

    private void ClearTrackLag()
    {
        foreach (var video in Videos)
        {
            video.IsLagging = false;
        }
    }

    /// <summary>
    /// Playback is never cut short for a slow track, so the only place to say it happened is
    /// once it is over.
    /// </summary>
    private void ReportLaggingTracks()
    {
        var everLagged = _playerPreview.TrackEverLagged;
        var late = Videos
            .Where((_, index) => index < everLagged.Count && everLagged[index])
            .Select(video => video.Name)
            .ToArray();
        if (late.Length == 0) { return; }

        ErrorMessage = $"{string.Join(", ", late)}이(가) 실시간 재생을 따라가지 못했습니다. " +
            $"최대 지연 {_playerPreview.MaximumObservedSkewSeconds:0.00}초 · 내보내기 결과에는 영향이 없습니다.";
    }

    private void CancelInteractivePreview()
    {
        _hasInteractivePreviewPending = false;
        _pendingInteractiveTrack = null;
        _interactivePreviewRevision++;
        _interactivePreviewCancellation?.Cancel();
    }

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
        NormalizeExportRange();
        _suppressTrackPreview = true;
        try
        {
            foreach (var video in Videos)
            {
                video.StartSeconds = PlaybackTimeline.SnapVideoStart(video.StartSeconds, OutputFps, OutputDurationSeconds);
            }

            foreach (var audio in Audios)
            {
                audio.StartSeconds = PlaybackTimeline.SnapAudioStart(audio.StartSeconds, OutputDurationSeconds);
            }
        }
        finally
        {
            _suppressTrackPreview = false;
        }

        SetPlayhead(PlaybackScope.Segment, PlayheadSeconds, transitionFromStopped: false);
        SetPlayhead(PlaybackScope.Full, FullPlayheadSeconds, transitionFromStopped: false);
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
        OnPropertyChanged(nameof(CanPlaySegment));
        OnPropertyChanged(nameof(CanPauseSegment));
        OnPropertyChanged(nameof(CanUseSegmentControls));
        OnPropertyChanged(nameof(CanPlayFull));
        OnPropertyChanged(nameof(CanPauseFull));
        OnPropertyChanged(nameof(CanUseFullControls));
        OnPropertyChanged(nameof(CanEditExportRange));
        OnPropertyChanged(nameof(CanStartExport));
    }

    private double PlaybackHead(PlaybackScope scope) => scope == PlaybackScope.Segment ? PlayheadSeconds : FullPlayheadSeconds;
    private double PlaybackStart(PlaybackScope scope) => scope == PlaybackScope.Segment ? ExportStartSeconds : 0;
    private double PlaybackEnd(PlaybackScope scope)
    {
        var boundary = scope == PlaybackScope.Segment ? ExportEndSeconds : OutputDurationSeconds;
        return Math.Max(PlaybackStart(scope), PlaybackTimeline.LastFrameSeconds(boundary, OutputFps));
    }

    private void SetPlayhead(double seconds, bool transitionFromStopped) =>
        SetPlayhead(PlaybackScope.Segment, seconds, transitionFromStopped);

    private void SetPlayhead(PlaybackScope scope, double seconds, bool transitionFromStopped)
    {
        var start = PlaybackStart(scope);
        var end = PlaybackEnd(scope);
        var snapped = Math.Clamp(PlaybackTimeline.SnapPlayhead(seconds, OutputFps, OutputDurationSeconds), start, end);
        _activePlaybackScope = scope;
        var changed = scope == PlaybackScope.Segment
            ? SetProperty(ref _playheadSeconds, snapped, nameof(PlayheadSeconds))
            : SetProperty(ref _fullPlayheadSeconds, snapped, nameof(FullPlayheadSeconds));
        if (!changed)
        {
            return;
        }

        OnPropertyChanged(nameof(PlayheadText));
        OnPropertyChanged(nameof(SegmentTimeText));
        OnPropertyChanged(nameof(FullTimeText));
        OnPropertyChanged(nameof(SegmentHeadText));
        OnPropertyChanged(nameof(FullHeadText));
        OnPropertyChanged(nameof(CurrentTimeText));
        OnPropertyChanged(nameof(ActivePlayheadSeconds));
        if (transitionFromStopped && CurrentPlaybackState == PlaybackState.Stopped)
        {
            SetPlaybackState(PlaybackState.Paused);
        }
    }

    private bool IsAtPlaybackEnd(PlaybackScope scope)
    {
        var lastFrame = PlaybackEnd(scope);
        return PlaybackHead(scope) >= lastFrame - 0.5 / OutputFps;
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

    public void UpdatePlacementPreview()
    {
        _propertyPreviewDebounceCancellation?.Cancel();
        _stillCancellation?.Cancel();
        CancelInteractivePreview();
        if (IsEditingEnabled && HasComposition && TryBuildComposition(out var composition))
        {
            _playerPreview.ApplyLayout(composition);
        }
    }

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
