using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DiffVideo.App.ViewModels;
using DiffVideo.Core;

namespace DiffVideo.App;

public partial class MainWindow
{
    internal TimelineViewport TimelineView { get; } = new();
    internal bool TimelineAutoFollow { get; private set; } = true;
    private bool _updatingTimeline;
    private bool _barDragging;
    private double _barPointerX;

    private void InitializeTimeline()
    {
        Loaded += (_, _) => UpdateTimeline();
        SizeChanged += (_, _) => UpdateTimeline();
        if (ViewModel is { } vm) { vm.PropertyChanged += TimelineModelChanged; }
    }

    private void TimelineModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPlaying) && ViewModel?.IsPlaying == true)
        {
            TimelineAutoFollow = true;
            TimelineView.Reveal(ViewModel.PlayheadSeconds);
        }
        if (e.PropertyName is nameof(MainViewModel.PlayheadSeconds) or nameof(MainViewModel.PlaybackStartSeconds)
            or nameof(MainViewModel.OutputDurationSeconds) or nameof(MainViewModel.IsPlaying)) { UpdateTimeline(); }
    }

    private void Timeline_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTimeline();

    internal void UpdateTimeline()
    {
        if (_updatingTimeline || !IsLoaded || ViewModel is not { } vm) { return; }
        _updatingTimeline = true;
        try
        {
            TimelineRow.MaxHeight = Math.Max(TimelineRow.MinHeight, ActualHeight / 2);
            if (TimelineRow.Height.Value > TimelineRow.MaxHeight) { TimelineRow.Height = new(TimelineRow.MaxHeight); }
            TimelineLabelColumn.MaxWidth = Math.Max(180, TimelineBody.ActualWidth * 0.4);
            if (TimelineLabelColumn.Width.Value > TimelineLabelColumn.MaxWidth) { TimelineLabelColumn.Width = new(TimelineLabelColumn.MaxWidth); }
            TimelineView.Resize(TimelineViewportHost.ActualWidth, vm.OutputDurationSeconds);
            if (vm.IsPlaying && TimelineAutoFollow) { TimelineView.Reveal(vm.PlayheadSeconds); }
            TimelineContent.Width = TimelineView.ContentWidth;
            TimelineTranslation.X = -TimelineView.Offset;
            TimelineRuler.Configure(TimelineView.PixelsPerSecond, -TimelineView.Offset, TimelineView.Duration);
            TimelineScrollBar.Maximum = TimelineView.MaximumOffset;
            TimelineScrollBar.ViewportSize = TimelineView.Width;
            TimelineScrollBar.SmallChange = Math.Max(1, TimelineView.PixelsPerSecond);
            TimelineScrollBar.LargeChange = TimelineView.Width * 0.9;
            TimelineScrollBar.Value = TimelineView.Offset;
            Canvas.SetLeft(PlayheadBar, TimelineView.Position(vm.PlayheadSeconds) - 6);
            PlayheadBar.Height = Math.Max(0, TimelineViewportHost.ActualHeight - 8);
            Canvas.SetLeft(PlaybackStartFlag, TimelineView.Position(vm.PlaybackStartSeconds));
            ZoomInButton.IsEnabled = TimelineView.PixelsPerSecond < Math.Max(240, TimelineView.Width / TimelineView.Duration) - 0.001;
            ZoomOutButton.IsEnabled = !TimelineView.IsFit;
        }
        finally { _updatingTimeline = false; }
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomTimeline(2);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomTimeline(0.5);
    internal void ZoomTimeline(double factor)
    {
        TimelineView.Zoom(factor, ViewModel?.PlayheadSeconds ?? 0);
        UpdateTimeline();
    }
    private void FitTimeline_Click(object sender, RoutedEventArgs e) { TimelineView.Fit(); UpdateTimeline(); }

    private void TimelineScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingTimeline || !IsLoaded) { return; }
        ScrollTimeline(e.NewValue, manual: true);
    }
    private void TimelineScrollBar_Scroll(object sender, ScrollEventArgs e) => ScrollTimeline(e.NewValue, manual: true);
    internal void ScrollTimeline(double offset, bool manual)
    {
        if (manual) { TimelineAutoFollow = false; }
        TimelineView.Scroll(offset); UpdateTimeline();
    }

    private void TimelineHeight_DragDelta(object sender, DragDeltaEventArgs e) => ResizeTimelineHeight(TimelineRow.ActualHeight - e.VerticalChange);
    internal void ResizeTimelineHeight(double height)
    {
        TimelineRow.Height = new(Math.Clamp(height, TimelineRow.MinHeight, Math.Max(TimelineRow.MinHeight, ActualHeight / 2)));
    }
    private void TimelineLabel_DragDelta(object sender, DragDeltaEventArgs e) => ResizeTimelineLabels(TimelineLabelColumn.ActualWidth + e.HorizontalChange);
    internal void ResizeTimelineLabels(double width)
    {
        TimelineLabelColumn.Width = new(Math.Clamp(width, 180, Math.Max(180, TimelineBody.ActualWidth * 0.4)));
    }

    private void PlayheadBar_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (ViewModel is not { CanNavigate: true } vm || _timelineDragActive || IsRoiDragging) { PlayheadBar.CancelDrag(); return; }
        _barDragging = _playheadDragging = true;
        _barPointerX = TimelineView.Position(vm.PlayheadSeconds);
        vm.BeginTimelineInteraction();
    }
    private void PlayheadBar_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_barDragging || ViewModel is not { } vm) { return; }
        // Thumb moves with the playhead; use the stable viewport coordinate, not its local delta.
        _barPointerX = Mouse.GetPosition(TimelineViewportHost).X;
        vm.PlayheadSeconds = TimelineView.TimeAt(_barPointerX);
        vm.RequestPlayheadPreview();
    }
    private async void PlayheadBar_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_barDragging) { return; }
        _barDragging = _playheadDragging = false;
        if (ViewModel is { } vm) { vm.CancelTimelineDragPreview(); await vm.RefreshStillPreviewAsync(); }
    }
}
