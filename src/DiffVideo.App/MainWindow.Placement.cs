using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DiffVideo.Core;
using DiffVideo.App.ViewModels;

namespace DiffVideo.App;

public partial class MainWindow
{
    private RoiEdges PlacementEdges(Border border, Point point)
    {
        // Hit targets stay usable when the logical output canvas is scaled down.
        var transform = PreviewLogicalCanvas.TransformToAncestor(PreviewDropHost);
        var scale = (transform.Transform(new Point(1, 0)) - transform.Transform(new Point(0, 0))).Length;
        var threshold = 9 / Math.Max(0.01, scale);
        var edges = RoiEdges.None;
        if (point.X <= Math.Min(threshold, border.ActualWidth / 3)) { edges |= RoiEdges.Left; }
        else if (point.X >= border.ActualWidth - Math.Min(threshold, border.ActualWidth / 3)) { edges |= RoiEdges.Right; }
        if (point.Y <= Math.Min(threshold, border.ActualHeight / 3)) { edges |= RoiEdges.Top; }
        else if (point.Y >= border.ActualHeight - Math.Min(threshold, border.ActualHeight / 3)) { edges |= RoiEdges.Bottom; }
        return edges;
    }

    private static Cursor PlacementCursor(RoiEdges edges) => edges switch
    {
        RoiEdges.Left | RoiEdges.Top or RoiEdges.Right | RoiEdges.Bottom => Cursors.SizeNWSE,
        RoiEdges.Right | RoiEdges.Top or RoiEdges.Left | RoiEdges.Bottom => Cursors.SizeNESW,
        RoiEdges.Left or RoiEdges.Right => Cursors.SizeWE,
        RoiEdges.Top or RoiEdges.Bottom => Cursors.SizeNS,
        _ => Cursors.SizeAll
    };

    internal bool BeginOverlayDrag(Border border, Point origin, RoiEdges edges)
    {
        if (IsRoiDragging || ViewModel is not { IsEditingEnabled: true } || border.DataContext is not VideoTrackViewModel { HasMedia: true } track) { return false; }
        ViewModel.SelectVideo(track);
        PreviewLogicalCanvas.Focus();
        _overlayDragTrack = track;
        _overlayDragOrigin = origin;
        _overlayInitial = new(track.DestinationX, track.DestinationY, track.DestinationWidth, track.DestinationHeight);
        _overlayInitialCustomized = track.IsPlacementCustomized;
        _overlayEdges = edges;
        _overlayDragBorder = border;
        if (border.CaptureMouse()) { return true; }
        CancelOverlayDrag();
        return false;
    }

    internal void UpdateOverlayDrag(double dx, double dy)
    {
        if (_overlayDragTrack is not { } track || ViewModel is not { IsEditingEnabled: true }) { return; }
        track.SetDestination(DestinationEditing.Apply(_overlayInitial, _overlayEdges, dx, dy,
            track.AspectRatioLocked ? track.RoiWidth / (double)track.RoiHeight : null));
        ViewModel.UpdatePlacementPreview();
    }

    internal void CancelOverlayDrag()
    {
        if (_overlayDragTrack is not { } track) { return; }
        var border = _overlayDragBorder;
        _overlayDragTrack = null;
        _overlayDragBorder = null;
        track.SetDestination(_overlayInitial);
        // Restoring the rectangle must also restore the flag: a cancelled drag moved nothing.
        track.IsPlacementCustomized = _overlayInitialCustomized;
        ViewModel?.UpdatePlacementPreview();
        border?.ReleaseMouseCapture();
    }
}
