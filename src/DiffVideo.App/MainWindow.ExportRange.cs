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
        UpdateExportRangeDrag(TimelineView.TimeAt(pointer - _exportRangePointerOffset));
    }

    internal void UpdateExportRangeDrag(double seconds)
    {
        if (_exportRangeThumb is null) { return; }
        ViewModel?.SetExportBoundary(ReferenceEquals(_exportRangeThumb, ExportStartHandle), seconds);
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
        ViewModel?.ApplyExportRange(_exportRangeBefore);
        if (thumb.IsDragging) { thumb.CancelDrag(); }
    }
}
