using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DiffVideo.Core;

namespace DiffVideo.App;

public partial class MainWindow
{
    private Thumb? _exportRangeThumb;
    private ExportRange _exportRangeBefore;
    private double _exportRangePointerOffset;
    private double _exportDurationBefore;
    // 맞춤 보기에서는 길이가 늘면 축척이 바뀌므로, 드래그 중에는 시작 때 축척으로 시간을 잰다.
    private double _exportRangePixelsPerSecond = 1;

    private void UpdateExportRangeMarkers()
    {
        if (ViewModel is not { } vm) { return; }
        var start = TimelineView.Position(vm.ExportStartSeconds);
        var end = TimelineView.Position(vm.ExportEndSeconds);
        var width = TimelineView.Width;
        var height = TimelineViewportHost.ActualHeight;
        var visibleStart = Math.Clamp(start, 0, width);
        var visibleEnd = Math.Clamp(end, 0, width);
        ExportBeforeShade.Width = visibleStart;
        ExportBeforeShade.Height = ExportAfterShade.Height = height;
        Canvas.SetLeft(ExportAfterShade, visibleEnd);
        ExportAfterShade.Width = width - visibleEnd;
        Canvas.SetLeft(ExportRangeBand, visibleStart);
        ExportRangeBand.Width = Math.Max(0, visibleEnd - visibleStart);
        Canvas.SetLeft(ExportStartLine, start);
        Canvas.SetLeft(ExportEndLine, end - 2);
        ExportStartLine.Height = ExportEndLine.Height = height;
        Canvas.SetLeft(ExportStartHandle, start);
        Canvas.SetLeft(ExportEndHandle, end - ExportEndHandle.Width);
        if (end - start < ExportStartHandle.Width + ExportEndHandle.Width && start >= 0 && end <= width)
        {
            // Keep both handles reachable even when the selected range is only one frame wide.
            var left = Math.Clamp((start + end) / 2 - ExportStartHandle.Width, 0,
                Math.Max(0, width - ExportStartHandle.Width - ExportEndHandle.Width));
            Canvas.SetLeft(ExportStartHandle, left);
            Canvas.SetLeft(ExportEndHandle, left + ExportStartHandle.Width);
        }
    }

    private void ExportRange_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb thumb) { return; }
        if (!BeginExportRangeDrag(thumb)) { thumb.CancelDrag(); return; }
        var seconds = ReferenceEquals(thumb, ExportStartHandle) ? ViewModel!.ExportStartSeconds : ViewModel!.ExportEndSeconds;
        _exportRangePointerOffset = Mouse.GetPosition(TimelineViewportHost).X - TimelineView.Position(seconds);
    }

    internal bool BeginExportRangeDrag(Thumb thumb)
    {
        if (ViewModel is not { CanEditExportRange: true } vm || _overlayDragTrack is not null || _timelineDragActive || _playheadDragging || IsRoiDragging) { return false; }
        _exportRangeBefore = new(vm.ExportStartSeconds, vm.ExportEndSeconds);
        _exportDurationBefore = vm.OutputDurationSeconds;
        _exportRangePixelsPerSecond = TimelineView.PixelsPerSecond;
        _exportRangeThumb = thumb;
        PreviewLogicalCanvas.Focus();
        return true;
    }

    private void ExportRange_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_exportRangeThumb is null) { return; }
        var pointer = Mouse.GetPosition(TimelineViewportHost).X;
        if (pointer < 0 || pointer > TimelineView.Width)
        {
            ScrollTimeline(TimelineView.Offset + (pointer < 0 ? pointer : pointer - TimelineView.Width), manual: true);
        }
        var x = pointer - _exportRangePointerOffset;
        UpdateExportRangeDrag(ReferenceEquals(_exportRangeThumb, ExportStartHandle)
            ? TimelineView.TimeAt(x)
            : Math.Max(0, (x + TimelineView.Offset) / _exportRangePixelsPerSecond)); // 끝은 전체 길이에서 자르지 않는다
    }

    internal void UpdateExportRangeDrag(double seconds)
    {
        if (_exportRangeThumb is null || ViewModel is not { } vm) { return; }
        if (ReferenceEquals(_exportRangeThumb, ExportStartHandle)) { vm.SetExportBoundary(true, seconds); }
        else { vm.DragExportEnd(seconds, _exportDurationBefore); }
    }

    private void ExportRange_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled) { CancelExportRangeDrag(); }
        else { _exportRangeThumb = null; }
    }

    internal void CancelExportRangeDrag()
    {
        if (_exportRangeThumb is not { } thumb) { return; }
        _exportRangeThumb = null;
        if (ViewModel is { } vm) { vm.OutputDurationSeconds = _exportDurationBefore; }
        ViewModel?.ApplyExportRange(_exportRangeBefore);
        if (thumb.IsDragging) { thumb.CancelDrag(); }
    }
}
