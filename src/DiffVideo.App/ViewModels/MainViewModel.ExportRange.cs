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

    internal void ApplyExportRange(ExportRange range)
    {
        _exportStartSeconds = range.Start;
        _exportEndSeconds = range.End >= OutputDurationSeconds ? null : range.End;
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
