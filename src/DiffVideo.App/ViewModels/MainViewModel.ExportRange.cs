using DiffVideo.Core;

namespace DiffVideo.App.ViewModels;

public sealed partial class MainViewModel
{
    private double _exportStartSeconds;
    private double? _exportEndSeconds;

    public double ExportStartSeconds { get => _exportStartSeconds; set => SetExportBoundary(true, value); }
    public double ExportEndSeconds { get => _exportEndSeconds ?? OutputDurationSeconds; set => SetExportBoundary(false, value); }
    public double ExportDurationSeconds => ExportEndSeconds - ExportStartSeconds;
    public string ExportRangeText => $"저장 길이 {FormatTime(ExportDurationSeconds)}";
    public bool CanEditExportRange => HasComposition && !IsPlaying;

    public void SetExportBoundary(bool start, double seconds)
    {
        if (!CanEditExportRange || !double.IsFinite(seconds)) { NotifyExportRange(); return; }
        var range = new ExportRange(ExportStartSeconds, ExportEndSeconds);
        ApplyExportRange(start ? range.MoveStart(seconds, OutputFps) : range.MoveEnd(seconds, OutputDurationSeconds, OutputFps));
    }

    public void ResetExportRange()
    {
        if (!CanEditExportRange) { return; }
        ApplyExportRange(new(0, OutputDurationSeconds));
    }

    public void SetOverlapExportRange()
    {
        if (!CanEditExportRange) { return; }
        var start = Math.Max(Video1.StartSeconds, Video2.StartSeconds);
        var end = Math.Min(OutputDurationSeconds, Math.Min(
            Video1.StartSeconds + Video1.DurationSeconds,
            Video2.StartSeconds + Video2.DurationSeconds));
        if (end <= start)
        {
            Status = "두 영상이 겹치는 구간이 없어 내보내기 구간을 유지했습니다.";
            return;
        }

        ApplyExportRange(ExportRange.Normalize(start, end, OutputDurationSeconds, OutputFps));
        Status = "두 영상이 겹치는 구간을 내보내기 구간으로 설정했습니다.";
    }

    public void SetVideoExportRange()
    {
        if (!CanEditExportRange) { return; }
        var requiredEnd = Math.Max(
            Video1.StartSeconds + Video1.DurationSeconds,
            Video2.StartSeconds + Video2.DurationSeconds);
        if (requiredEnd > OutputDurationSeconds)
        {
            OutputDurationSeconds = requiredEnd;
        }

        var start = Math.Min(Video1.StartSeconds, Video2.StartSeconds);
        var end = Math.Max(
            Video1.StartSeconds + Video1.DurationSeconds,
            Video2.StartSeconds + Video2.DurationSeconds);
        ApplyExportRange(ExportRange.Normalize(start, end, OutputDurationSeconds, OutputFps));
        Status = "두 영상의 전체 구간을 내보내기 구간으로 설정했습니다.";
    }

    internal void ApplyExportRange(ExportRange range)
    {
        _exportStartSeconds = range.Start;
        _exportEndSeconds = range.End >= OutputDurationSeconds ? null : range.End;
        SetPlayhead(PlaybackScope.Segment, PlayheadSeconds, transitionFromStopped: false);
        NotifyExportRange();
    }

    private void NormalizeExportRange() => ApplyExportRange(ExportRange.Normalize(
        ExportStartSeconds, ExportEndSeconds, OutputDurationSeconds, OutputFps));

    private void NotifyExportRange()
    {
        OnPropertyChanged(nameof(ExportStartSeconds));
        OnPropertyChanged(nameof(ExportEndSeconds));
        OnPropertyChanged(nameof(ExportDurationSeconds));
        OnPropertyChanged(nameof(ExportRangeText));
        OnPropertyChanged(nameof(SegmentTimeText));
        OnPropertyChanged(nameof(ExportStartText));
        OnPropertyChanged(nameof(ExportEndText));
    }

    public Composition BuildExportComposition() => WithExportRange(BuildComposition());

    private Composition WithExportRange(Composition composition) => composition with
    {
        Output = composition.Output with
        {
            ExportStart = TimeSpan.FromSeconds(ExportStartSeconds),
            ExportEnd = TimeSpan.FromSeconds(ExportEndSeconds)
        }
    };
}
