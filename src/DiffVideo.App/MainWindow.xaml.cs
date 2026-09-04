using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DiffVideo.App.ViewModels;
using DiffVideo.Core;
using DiffVideo.Infrastructure;
using Microsoft.Win32;

namespace DiffVideo.App;

public partial class MainWindow : Window
{
    private VideoTrackViewModel? _overlayDragTrack;
    private Point _overlayDragOrigin;
    private int _overlayInitialX;
    private int _overlayInitialY;
    private object? _timelineDragTrack;
    private Border? _timelineDragBorder;
    private TextBlock? _timelineDragStartLabel;
    private Point _timelineDragOrigin;
    private double _timelineInitialStart;
    private double _timelinePendingStart;
    private bool _timelineDragActive;
    private bool _playheadDragging;
    private bool _allowClose;
    private VideoTrackViewModel? _roiDragTrack;
    private VideoTrack? _roiDragSnapshot;
    private Point _roiDragOrigin;
    private Rect _roiDragBounds;
    private Rect _roiSelection;
    private bool _spaceHeld;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            ViewModel = new(FfmpegPaths.Discover());
            DataContext = ViewModel;
            InitializeTimeline();
            ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.SelectedVideo) ||
                    e.PropertyName == nameof(MainViewModel.IsPlaying) && ViewModel.IsPlaying)
                {
                    CancelPreviewRoi();
                }
            };
        }
        catch (FileNotFoundException exception)
        {
            MessageBox.Show(exception.Message, "FFmpeg가 필요합니다", MessageBoxButton.OK, MessageBoxImage.Error);
            Loaded += (_, _) => Close();
        }
    }

    public MainViewModel? ViewModel { get; }

    internal bool IsRoiDragging => _roiDragTrack is not null;

    internal bool BeginPreviewRoi(Point point)
    {
        if (ViewModel is not { IsEditingEnabled: true, HasComposition: true } vm ||
            IsRoiDragging || _overlayDragTrack is not null || _timelineDragActive || _playheadDragging) { return false; }
        PreviewLogicalCanvas.Focus();
        VideoTrack snapshot;
        try
        {
            var composition = vm.BuildComposition();
            snapshot = ReferenceEquals(vm.SelectedVideo, vm.Video1) ? composition.Video1 : composition.Video2;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OverflowException)
        {
            vm.ReportInteraction("설정값을 확인해 주세요.", exception.Message);
            return false;
        }
        var clip = PreviewLayout.From(snapshot).Clip;
        var bounds = new Rect(clip.X, clip.Y, clip.Width, clip.Height);
        if (!bounds.Contains(point)) { return false; }
        if (!PreviewLogicalCanvas.CaptureMouse()) { return false; }
        vm.BeginTimelineInteraction();
        _roiDragTrack = vm.SelectedVideo;
        _roiDragSnapshot = snapshot;
        _roiDragOrigin = point;
        _roiDragBounds = bounds;
        PreviewLogicalCanvas.Cursor = Cursors.Cross;
        PreviewLogicalCanvas.ForceCursor = true;
        vm.SetRoiPreviewFocus(_roiDragTrack);
        var overlay = ReferenceEquals(_roiDragTrack, vm.Video1) ? Video1Overlay : Video2Overlay;
        overlay.SetCurrentValue(Panel.ZIndexProperty, int.MaxValue);
        PreviewRoiSelection.Visibility = Visibility.Visible;
        UpdatePreviewRoi(point);
        vm.ReportInteraction($"{_roiDragTrack.Name} ROI 선택 · 오른쪽 버튼을 놓으면 적용 · Esc 취소", "");
        return true;
    }

    internal void UpdatePreviewRoi(Point point)
    {
        if (!IsRoiDragging) { return; }
        var end = new Point(Math.Clamp(point.X, _roiDragBounds.Left, _roiDragBounds.Right),
            Math.Clamp(point.Y, _roiDragBounds.Top, _roiDragBounds.Bottom));
        _roiSelection = new Rect(_roiDragOrigin, end);
        Canvas.SetLeft(PreviewRoiSelection, _roiSelection.X);
        Canvas.SetTop(PreviewRoiSelection, _roiSelection.Y);
        PreviewRoiSelection.Width = _roiSelection.Width;
        PreviewRoiSelection.Height = _roiSelection.Height;
        var scale = PreviewLogicalCanvas.TransformToAncestor(PreviewDropHost).TransformBounds(new Rect(0, 0, 1, 1)).Width;
        PreviewRoiSelection.StrokeThickness = 2 / Math.Max(0.01, scale);
        if (_roiDragBounds.Contains(point) && _roiDragSnapshot is { } snapshot)
        {
            var layout = PreviewLayout.From(snapshot);
            var x = Math.Clamp((int)((point.X - layout.TranslateX) / layout.ScaleX), 0, snapshot.Media.DisplayWidth - 1);
            var y = Math.Clamp((int)((point.Y - layout.TranslateY) / layout.ScaleY), 0, snapshot.Media.DisplayHeight - 1);
            PreviewCoordinateTip.ShowAt(PreviewLogicalCanvas.TranslatePoint(point, PreviewCoordinateTip), x, y);
        }
        else { PreviewCoordinateTip.Hide(); }
    }

    internal async Task CompletePreviewRoiAsync(Point point)
    {
        if (_roiDragTrack is not { } track || _roiDragSnapshot is not { } snapshot || ViewModel is null) { return; }
        UpdatePreviewRoi(point);
        var screenRect = PreviewLogicalCanvas.TransformToAncestor(PreviewDropHost).TransformBounds(_roiSelection);
        var roi = screenRect.Width >= 3 && screenRect.Height >= 3
            ? Core.PreviewRoiSelection.Map(snapshot, _roiDragOrigin.X, _roiDragOrigin.Y, point.X, point.Y) : null;
        EndPreviewRoi();
        if (roi is { } selected) { await ViewModel.ApplyRoiAsync(track, selected); }
        else { ViewModel.ReportInteraction("ROI 변경 없음 · 사각형을 드래그해 선택하세요."); }
    }

    internal void CancelPreviewRoi()
    {
        if (!IsRoiDragging) { return; }
        EndPreviewRoi();
        ViewModel?.ReportInteraction("ROI 선택 취소 · 기존 영역 유지");
    }

    private void EndPreviewRoi()
    {
        _roiDragTrack = null;
        _roiDragSnapshot = null;
        PreviewCoordinateTip.Hide();
        PreviewRoiSelection.Visibility = Visibility.Collapsed;
        PreviewLogicalCanvas.ClearValue(CursorProperty);
        PreviewLogicalCanvas.ForceCursor = false;
        Video1Overlay.GetBindingExpression(Panel.ZIndexProperty)?.UpdateTarget();
        Video2Overlay.GetBindingExpression(Panel.ZIndexProperty)?.UpdateTarget();
        ViewModel?.SetRoiPreviewFocus(null);
        if (PreviewLogicalCanvas.IsMouseCaptured) { PreviewLogicalCanvas.ReleaseMouseCapture(); }
    }

    private void PreviewRoi_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (BeginPreviewRoi(e.GetPosition(PreviewLogicalCanvas))) { e.Handled = true; }
    }

    private void PreviewRoi_MouseMove(object sender, MouseEventArgs e)
    {
        if (!IsRoiDragging) { return; }
        if (e.RightButton == MouseButtonState.Pressed) { UpdatePreviewRoi(e.GetPosition(PreviewLogicalCanvas)); }
        else { CancelPreviewRoi(); }
        e.Handled = true;
    }

    private async void PreviewRoi_RightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsRoiDragging) { return; }
        e.Handled = true;
        await CompletePreviewRoiAsync(e.GetPosition(PreviewLogicalCanvas));
    }

    private void PreviewRoi_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!PreviewLogicalCanvas.IsMouseCaptured) { CancelPreviewRoi(); }
    }

    private void PreviewDropHost_SizeChanged(object sender, SizeChangedEventArgs e) => CancelPreviewRoi();

    private async void ResetRoi_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { IsEditingEnabled: true } vm || vm.SelectedVideo.Media is not { } media) { return; }
        CancelPreviewRoi();
        await vm.ApplyRoiAsync(vm.SelectedVideo, PixelRect.FullFrame(media));
    }

    private async void Preview_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        PreviewDropHint.Visibility = Visibility.Collapsed;
        await ImportPreviewDropAsync(e.Data);
    }

    internal bool CanAcceptPreviewDrop(IDataObject data) =>
        ViewModel is { IsEditingEnabled: true } && PreviewDropFiles(data).Length > 0;

    internal async Task ImportPreviewDropAsync(IDataObject data)
    {
        if (!CanAcceptPreviewDrop(data)) { return; }
        await ViewModel!.LoadFilesAsync(PreviewDropFiles(data));
    }

    private static string[] PreviewDropFiles(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(IsSupportedFile).ToArray() : [];

    private void Preview_DragOver(object sender, DragEventArgs e)
    {
        var accepted = CanAcceptPreviewDrop(e.Data);
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        PreviewDropHint.Visibility = accepted ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Preview_DragLeave(object sender, DragEventArgs e)
    {
        var point = e.GetPosition(PreviewDropHost);
        if (!new Rect(PreviewDropHost.RenderSize).Contains(point)) { PreviewDropHint.Visibility = Visibility.Collapsed; }
        e.Handled = true;
    }

    private async void OpenFiles_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !ViewModel.IsEditingEnabled)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "MP4 영상과 MP3 음원 선택",
            Filter = "지원 미디어 (*.mp4;*.mp3)|*.mp4;*.mp3|MP4 영상 (*.mp4)|*.mp4|MP3 음원 (*.mp3)|*.mp3",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            await ViewModel.LoadFilesAsync(dialog.FileNames);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || !ViewModel.HasComposition)
        {
            MessageBox.Show("MP4 영상 두 개를 먼저 불러오세요.", "내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Composition snapshot;
        try { snapshot = ViewModel.BuildComposition(); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OverflowException)
        {
            ViewModel.ReportInteraction("설정값을 확인해 주세요.", exception.Message);
            return;
        }
        var confirmation = new ExportConfirmationWindow(snapshot) { Owner = this };
        if (confirmation.ShowDialog() != true)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "합성 MP4 저장",
            Filter = "MP4 영상 (*.mp4)|*.mp4",
            DefaultExt = ".mp4",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("N")[..4]}.mp4"
        };
        if (dialog.ShowDialog(this) == true)
        {
            await ViewModel.ExportAsync(confirmation.ConfirmedComposition, dialog.FileName, overwrite: true);
        }
    }

    private void CancelExport_Click(object sender, RoutedEventArgs e) => ViewModel?.CancelExport();
    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("DiffVideo 0.4.0\nMIT License\nMaterial Icons by Google · Apache-2.0\n\n이 프로그램은 LGPL 조건의 FFmpeg를 별도 실행 파일로 사용합니다. 자세한 내용은 THIRD-PARTY-NOTICES.md와 licenses 폴더를 확인하세요.", "DiffVideo 정보", MessageBoxButton.OK, MessageBoxImage.Information);
    private void Canvas720_Click(object sender, RoutedEventArgs e) => ViewModel?.ApplyCanvasPreset(1280, 720);
    private void Canvas1080_Click(object sender, RoutedEventArgs e) => ViewModel?.ApplyCanvasPreset(1920, 1080);
    private void Canvas4K_Click(object sender, RoutedEventArgs e) => ViewModel?.ApplyCanvasPreset(3840, 2160);

    private void MediaVideo_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null && sender is FrameworkElement { DataContext: VideoTrackViewModel track })
        {
            ViewModel.SelectVideo(track);
        }
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.StartPlaybackAsync();
        }
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.PausePlaybackAsync();
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.StopPlaybackAsync();
        }
    }

    private async void PreviousFrame_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.StepFrameAsync(-1);
        }
    }

    private async void NextFrame_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.StepFrameAsync(1);
        }
    }

    private async void SetPlaybackStart_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.SetPlaybackStartAsync();
        }
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsRoiDragging || ViewModel is null || !ViewModel.IsEditingEnabled || sender is not Border { DataContext: VideoTrackViewModel track } border)
        {
            return;
        }

        ViewModel.SelectVideo(track);
        PreviewLogicalCanvas.Focus();
        _overlayDragTrack = track;
        _overlayDragOrigin = e.GetPosition(PreviewLogicalCanvas);
        _overlayInitialX = track.DestinationX;
        _overlayInitialY = track.DestinationY;
        border.CaptureMouse();
        e.Handled = true;
    }

    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel is null || _overlayDragTrack is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(PreviewLogicalCanvas);
        var delta = position - _overlayDragOrigin;
        _overlayDragTrack.DestinationX = Math.Clamp(_overlayInitialX + (int)Math.Round(delta.X), 0, Math.Max(0, ViewModel.CanvasWidth - _overlayDragTrack.DestinationWidth));
        _overlayDragTrack.DestinationY = Math.Clamp(_overlayInitialY + (int)Math.Round(delta.Y), 0, Math.Max(0, ViewModel.CanvasHeight - _overlayDragTrack.DestinationHeight));
    }

    private async void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border)
        {
            border.ReleaseMouseCapture();
        }

        _overlayDragTrack = null;
        if (ViewModel is not null)
        {
            await ViewModel.RefreshStillPreviewAsync();
        }
    }

    private void TimelineClip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || !ViewModel.IsEditingEnabled || sender is not Border border || VisualTreeParentCanvas(border) is not { } canvas)
        {
            return;
        }

        _timelineDragTrack = border.DataContext;
        PreviewLogicalCanvas.Focus();
        ViewModel.BeginTimelineInteraction();
        _timelineDragBorder = border;
        _timelineDragStartLabel = FindStartLabel(border);
        _timelineInitialStart = GetStart(_timelineDragTrack);
        _timelinePendingStart = _timelineInitialStart;
        _timelineDragOrigin = e.GetPosition(canvas);
        _timelineDragActive = true;
        if (_timelineDragTrack is VideoTrackViewModel video)
        {
            ViewModel.SelectVideo(video);
        }

        border.RenderTransform = new TranslateTransform();
        border.CaptureMouse();
        e.Handled = true;
    }

    private void TimelineClip_MouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel is null || _timelineDragTrack is null || e.LeftButton != MouseButtonState.Pressed || sender is not Border border || VisualTreeParentCanvas(border) is not { } canvas || canvas.ActualWidth <= 0)
        {
            return;
        }

        var delta = e.GetPosition(canvas).X - _timelineDragOrigin.X;
        var rawSeconds = Math.Clamp(_timelineInitialStart + delta / canvas.ActualWidth * ViewModel.OutputDurationSeconds, 0, ViewModel.OutputDurationSeconds);
        _timelinePendingStart = ViewModel.SnapTrackStart(_timelineDragTrack, rawSeconds);
        var initialLeft = _timelineInitialStart / ViewModel.OutputDurationSeconds * canvas.ActualWidth;
        var pendingLeft = _timelinePendingStart / ViewModel.OutputDurationSeconds * canvas.ActualWidth;
        if (_timelineDragBorder?.RenderTransform is TranslateTransform transform)
        {
            transform.X = pendingLeft - initialLeft;
        }

        _timelineDragStartLabel?.SetCurrentValue(TextBlock.TextProperty, $"예상 {_timelinePendingStart:F3}초");
        ViewModel.RequestTimelineDragPreview(_timelineDragTrack, _timelinePendingStart);
        e.Handled = true;
    }

    private async void TimelineClip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_timelineDragActive || ViewModel is null)
        {
            return;
        }

        var track = _timelineDragTrack;
        var pendingStart = _timelinePendingStart;
        _timelineDragActive = false;
        if (sender is Border border)
        {
            border.RenderTransform = Transform.Identity;
            border.ReleaseMouseCapture();
        }

        ClearTimelineDragFields();
        await ViewModel.CommitTimelineTrackStartAsync(track, pendingStart);
        e.Handled = true;
    }

    private void TimelineClip_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_timelineDragActive)
        {
            CancelTimelineDrag();
        }
    }

    private void PlayheadSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || !ViewModel.CanNavigate)
        {
            e.Handled = true;
            return;
        }

        _playheadDragging = true;
        ViewModel.BeginTimelineInteraction();
    }

    private void PlayheadSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_playheadDragging)
        {
            ViewModel?.RequestPlayheadPreview();
        }
    }

    private void PlayheadSlider_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null || !ViewModel.CanNavigate || !IsNavigationKey(e.Key))
        {
            return;
        }

        _playheadDragging = true;
        ViewModel.BeginTimelineInteraction();
    }

    private async void PlayheadSlider_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_playheadDragging || ViewModel is null || !IsNavigationKey(e.Key))
        {
            return;
        }

        _playheadDragging = false;
        ViewModel.CancelTimelineDragPreview();
        await ViewModel.RefreshStillPreviewAsync();
    }

    private async void PlayheadSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_playheadDragging || ViewModel is null)
        {
            return;
        }

        _playheadDragging = false;
        ViewModel.CancelTimelineDragPreview();
        await ViewModel.RefreshStillPreviewAsync();
    }

    private async void EditRoi_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedVideo.Media is null)
        {
            MessageBox.Show("ROI를 편집할 영상을 먼저 선택하세요.", "ROI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var track = ViewModel.SelectedVideo;
            var png = await ViewModel.GetSourceFramePngAsync(track);
            var editor = new RoiEditorWindow(png, track.Media.DisplayWidth, track.Media.DisplayHeight, new(track.RoiX, track.RoiY, track.RoiWidth, track.RoiHeight)) { Owner = this };
            if (editor.ShowDialog() == true)
            {
                await ViewModel.ApplyRoiAsync(track, editor.SelectedRoi);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "ROI 편집 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SendBackward_Click(object sender, RoutedEventArgs e) => ViewModel?.MoveSelectedLayer(forward: false);
    private void BringForward_Click(object sender, RoutedEventArgs e) => ViewModel?.MoveSelectedLayer(forward: true);

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsRoiDragging)
        {
            CancelPreviewRoi();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _timelineDragActive)
        {
            CancelTimelineDrag();
            e.Handled = true;
        }
        if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None || IsTextEntry(Keyboard.FocusedElement as DependencyObject)) { return; }
        e.Handled = true;
        await HandleSpacePressAsync(e.IsRepeat);
    }

    internal async Task HandleSpacePressAsync(bool isRepeat)
    {
        if (_spaceHeld || isRepeat) { return; }
        _spaceHeld = true;
        if (ViewModel is null || IsRoiDragging || _timelineDragActive || _overlayDragTrack is not null || _playheadDragging || Mouse.Captured is not null) { return; }
        if (ViewModel.IsPlaying) { await ViewModel.PausePlaybackAsync(); }
        else { await ViewModel.StartPlaybackAsync(); }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceHeld) { _spaceHeld = false; e.Handled = true; }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        _spaceHeld = false;
        CancelPreviewRoi();
    }

    internal static bool IsTextEntry(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is TextBoxBase or PasswordBox or ComboBox) { return true; }
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        CancelPreviewRoi();
        if (_allowClose || ViewModel is null)
        {
            ViewModel?.Dispose();
            return;
        }

        if (!ViewModel.IsExporting)
        {
            ViewModel.Dispose();
            return;
        }

        e.Cancel = true;
        var result = MessageBox.Show(
            "내보내기가 진행 중입니다. 취소하고 종료하시겠습니까?",
            "DiffVideo 종료",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        IsEnabled = false;
        await ViewModel.CancelExportAndWaitAsync();
        _allowClose = true;
        Close();
    }

    private static bool IsSupportedFile(string path) =>
        Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase);

    private static Canvas? VisualTreeParentCanvas(DependencyObject element)
    {
        var current = element;
        while (current is not null)
        {
            if (current is Canvas canvas)
            {
                return canvas;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static double GetStart(object? track) => track switch
    {
        VideoTrackViewModel video => video.StartSeconds,
        AudioTrackViewModel audio => audio.StartSeconds,
        _ => 0
    };

    private static bool IsNavigationKey(Key key) => key is
        Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;

    private void CancelTimelineDrag()
    {
        if (!_timelineDragActive)
        {
            return;
        }

        _timelineDragActive = false;
        ViewModel?.CancelTimelineDragPreview();
        if (_timelineDragBorder is { } border)
        {
            border.RenderTransform = Transform.Identity;
            if (border.IsMouseCaptured)
            {
                border.ReleaseMouseCapture();
            }
        }

        _timelineDragStartLabel?.SetCurrentValue(TextBlock.TextProperty, $"시작 {_timelineInitialStart:F3}초");
        ClearTimelineDragFields();
        if (ViewModel is not null) { _ = ViewModel.RefreshStillPreviewAsync(); }
    }

    private void ClearTimelineDragFields()
    {
        _timelineDragTrack = null;
        _timelineDragBorder = null;
        _timelineDragStartLabel = null;
    }

    private static TextBlock? FindStartLabel(Border border)
    {
        if (border.Child is not StackPanel panel)
        {
            return null;
        }

        return panel.Children.OfType<TextBlock>().FirstOrDefault(item => Equals(item.Tag, "StartLabel"));
    }
}
