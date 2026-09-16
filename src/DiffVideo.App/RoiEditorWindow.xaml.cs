using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DiffVideo.Core;

namespace DiffVideo.App;

public partial class RoiEditorWindow : Window
{
    private enum DragMode { None, Move, Resize, Draw }
    private static bool _sessionGridVisible = true;
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly Rectangle[] _handles = new Rectangle[8];
    private Point _dragOrigin;
    private Point _dragSource;
    private PixelRect _beforeDrag;
    private DragMode _dragMode;
    private RoiEdges _dragEdges;
    private Rect _displayedImage;
    private bool _updatingInputs;
    private bool _closing;

    public RoiEditorWindow(byte[] png, int sourceWidth, int sourceHeight, PixelRect initialRoi)
    {
        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
        SelectedRoi = RoiBounds.Clamp(initialRoi, sourceWidth, sourceHeight);
        InitializeComponent();
        SourceImage.Source = DecodePng(png);
        for (var i = 0; i < _handles.Length; i++)
        {
            _handles[i] = new Rectangle { Width = 8, Height = 8,
                Fill = (Brush)FindResource("CanvasBrush"), Stroke = (Brush)FindResource("PrimaryBrush"), StrokeThickness = 1 };
            HandleCanvas.Children.Add(_handles[i]);
        }
        UpdateGridState();
        Loaded += (_, _) => UpdateImageLayout();
        Closing += (_, _) => { _closing = true; CancelRoiDrag(); HideGuides(); };
        UpdateNumbers();
    }

    public PixelRect SelectedRoi { get; private set; }
    internal bool IsDragging => _dragMode != DragMode.None;
    internal Rect DisplayedImage => _displayedImage;
    internal bool GridVisible => _sessionGridVisible;

    private void ImageHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded) { return; }
        CancelRoiDrag();
        UpdateImageLayout();
    }

    private void UpdateImageLayout()
    {
        if (ImageHost.ActualWidth <= 0 || ImageHost.ActualHeight <= 0) { return; }
        var scale = Math.Min(ImageHost.ActualWidth / _sourceWidth, ImageHost.ActualHeight / _sourceHeight);
        _displayedImage = new((ImageHost.ActualWidth - _sourceWidth * scale) / 2,
            (ImageHost.ActualHeight - _sourceHeight * scale) / 2, _sourceWidth * scale, _sourceHeight * scale);
        SourceXRuler.Configure(scale, _displayedImage.X, _sourceWidth);
        SourceYRuler.Configure(scale, _displayedImage.Y, _sourceHeight);
        PixelGrid.Configure(_displayedImage, _sourceWidth, _sourceHeight);
        HideGuides();
        UpdateSelectionVisual();
    }

    private Point ToSource(Point point) => new(
        Math.Clamp((point.X - _displayedImage.X) / _displayedImage.Width * _sourceWidth, 0, _sourceWidth),
        Math.Clamp((point.Y - _displayedImage.Y) / _displayedImage.Height * _sourceHeight, 0, _sourceHeight));

    internal Point FromSource(double x, double y) => new(_displayedImage.X + x / _sourceWidth * _displayedImage.Width,
        _displayedImage.Y + y / _sourceHeight * _displayedImage.Height);

    private Rect SelectionBounds()
    {
        var roi = SelectedRoi;
        return new(FromSource(roi.X, roi.Y), FromSource(roi.X + roi.Width, roi.Y + roi.Height));
    }

    private (DragMode Mode, RoiEdges Edges) HitTestRoi(Point point)
    {
        if (!_displayedImage.Contains(point)) { return (DragMode.None, RoiEdges.None); }
        var rect = SelectionBounds();
        const double tolerance = 7;
        var nearLeft = Math.Abs(point.X - rect.Left) <= tolerance;
        var nearRight = Math.Abs(point.X - rect.Right) <= tolerance;
        var nearTop = Math.Abs(point.Y - rect.Top) <= tolerance;
        var nearBottom = Math.Abs(point.Y - rect.Bottom) <= tolerance;
        var horizontal = point.X >= rect.Left - tolerance && point.X <= rect.Right + tolerance;
        var vertical = point.Y >= rect.Top - tolerance && point.Y <= rect.Bottom + tolerance;
        var edges = RoiEdges.None;
        if (vertical && (nearLeft || nearRight))
            edges |= nearLeft && (!nearRight || Math.Abs(point.X - rect.Left) <= Math.Abs(point.X - rect.Right)) ? RoiEdges.Left : RoiEdges.Right;
        if (horizontal && (nearTop || nearBottom))
            edges |= nearTop && (!nearBottom || Math.Abs(point.Y - rect.Top) <= Math.Abs(point.Y - rect.Bottom)) ? RoiEdges.Top : RoiEdges.Bottom;
        if (edges != RoiEdges.None) { return (DragMode.Resize, edges); }
        return (rect.Contains(point) ? DragMode.Move : DragMode.Draw, RoiEdges.None);
    }

    internal bool BeginRoiDrag(Point point)
    {
        if (IsDragging || _displayedImage.Width <= 0 || _closing) { return false; }
        ImageHost.Focus(); // Commits the active numeric field before starting a gesture.
        var hit = HitTestRoi(point);
        if (hit.Mode == DragMode.None || !ImageHost.CaptureMouse()) { return false; }
        _beforeDrag = SelectedRoi;
        _dragOrigin = point;
        _dragSource = ToSource(point);
        _dragMode = hit.Mode;
        _dragEdges = hit.Edges;
        UpdateCursor(hit.Mode, hit.Edges);
        UpdateCoordinateTip(point);
        return true;
    }

    internal void UpdateRoiDrag(Point point)
    {
        if (!IsDragging) { return; }
        var source = ToSource(point);
        var dx = source.X - _dragSource.X; var dy = source.Y - _dragSource.Y;
        SelectedRoi = _dragMode switch
        {
            DragMode.Move => RoiEditing.Move(_beforeDrag, dx, dy, _sourceWidth, _sourceHeight),
            DragMode.Resize => RoiEditing.Resize(_beforeDrag, _dragEdges, dx, dy, _sourceWidth, _sourceHeight),
            _ => RoiEditing.Draw(_dragSource.X, _dragSource.Y, source.X, source.Y, _sourceWidth, _sourceHeight)
        };
        UpdateSelectionVisual();
        UpdateNumbers();
        UpdateCoordinateTip(point);
    }

    internal void CompleteRoiDrag(Point point)
    {
        if (!IsDragging) { return; }
        UpdateRoiDrag(point);
        // A click outside the selection must not accidentally replace it with a 2px crop.
        if (_dragMode == DragMode.Draw && (Math.Abs(point.X - _dragOrigin.X) < 3 || Math.Abs(point.Y - _dragOrigin.Y) < 3))
            SelectedRoi = _beforeDrag;
        EndDrag();
        UpdateSelectionVisual();
        UpdateNumbers();
    }

    internal void CancelRoiDrag()
    {
        if (!IsDragging) { return; }
        SelectedRoi = _beforeDrag;
        EndDrag();
        UpdateSelectionVisual();
        UpdateNumbers();
    }

    private void EndDrag()
    {
        _dragMode = DragMode.None;
        _dragEdges = RoiEdges.None;
        if (ImageHost.IsMouseCaptured) { ImageHost.ReleaseMouseCapture(); }
        ImageHost.Cursor = Cursors.Arrow;
    }

    private void ImageHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (BeginRoiDrag(e.GetPosition(ImageHost))) { e.Handled = true; }
    }
    private void ImageHost_MouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(ImageHost);
        if (IsDragging)
        {
            if (e.LeftButton == MouseButtonState.Pressed) { UpdateRoiDrag(point); }
            else { CancelRoiDrag(); }
        }
        else
        {
            var hit = HitTestRoi(point);
            UpdateCursor(hit.Mode, hit.Edges);
            UpdateCoordinateTip(point);
        }
    }
    private void ImageHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsDragging) { return; }
        CompleteRoiDrag(e.GetPosition(ImageHost)); e.Handled = true;
    }
    private void ImageHost_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!ImageHost.IsMouseCaptured) { CancelRoiDrag(); }
    }
    private void ImageHost_MouseLeave(object sender, MouseEventArgs e) => HideGuides();
    private void Window_Deactivated(object? sender, EventArgs e) { CancelRoiDrag(); HideGuides(); }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsDragging) { CancelRoiDrag(); e.Handled = true; }
    }
    private void UpdateCursor(DragMode mode, RoiEdges edges)
    {
        ImageHost.Cursor = mode switch
        {
            DragMode.Move => Cursors.SizeAll,
            DragMode.Draw => Cursors.Cross,
            DragMode.Resize when edges is (RoiEdges.Left | RoiEdges.Top) or (RoiEdges.Right | RoiEdges.Bottom) => Cursors.SizeNWSE,
            DragMode.Resize when edges is (RoiEdges.Right | RoiEdges.Top) or (RoiEdges.Left | RoiEdges.Bottom) => Cursors.SizeNESW,
            DragMode.Resize when edges is RoiEdges.Left or RoiEdges.Right => Cursors.SizeWE,
            DragMode.Resize => Cursors.SizeNS,
            _ => Cursors.Arrow
        };
    }

    internal void UpdateCoordinateTip(Point point)
    {
        if (!_displayedImage.Contains(point) || _displayedImage.Width <= 0) { HideGuides(); return; }
        var source = ToSource(point);
        SourceCoordinateTip.ShowAt(point, Math.Min((int)source.X, _sourceWidth - 1), Math.Min((int)source.Y, _sourceHeight - 1));
        var cursor = ImageHost.TranslatePoint(point, CrosshairOverlay);
        var bottomRight = ImageHost.TranslatePoint(_displayedImage.BottomRight, CrosshairOverlay);
        VerticalGuide.X1 = VerticalGuide.X2 = cursor.X;
        VerticalGuide.Y1 = 0; VerticalGuide.Y2 = bottomRight.Y;
        HorizontalGuide.Y1 = HorizontalGuide.Y2 = cursor.Y;
        HorizontalGuide.X1 = 0; HorizontalGuide.X2 = bottomRight.X;
        CrosshairOverlay.Visibility = Visibility.Visible;
    }
    private void HideGuides()
    {
        SourceCoordinateTip.Hide(); CrosshairOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateSelectionVisual()
    {
        if (_displayedImage.Width <= 0) { return; }
        var rect = SelectionBounds();
        Canvas.SetLeft(RoiRectangle, rect.Left); Canvas.SetTop(RoiRectangle, rect.Top);
        RoiRectangle.Width = rect.Width; RoiRectangle.Height = rect.Height;
        Point[] points = [rect.TopLeft, new(rect.Left + rect.Width / 2, rect.Top), rect.TopRight,
            new(rect.Right, rect.Top + rect.Height / 2), rect.BottomRight,
            new(rect.Left + rect.Width / 2, rect.Bottom), rect.BottomLeft, new(rect.Left, rect.Top + rect.Height / 2)];
        for (var i = 0; i < _handles.Length; i++)
        {
            if (_handles[i] is not { } handle) { continue; }
            Canvas.SetLeft(handle, points[i].X - 4); Canvas.SetTop(handle, points[i].Y - 4);
        }
    }

    internal void SetRoi(PixelRect roi)
    {
        CancelRoiDrag();
        SelectedRoi = RoiBounds.Clamp(roi, _sourceWidth, _sourceHeight);
        UpdateSelectionVisual(); UpdateNumbers();
    }
    private void UpdateNumbers()
    {
        _updatingInputs = true;
        try
        {
            XInput.Text = SelectedRoi.X.ToString(CultureInfo.InvariantCulture);
            YInput.Text = SelectedRoi.Y.ToString(CultureInfo.InvariantCulture);
            WidthInput.Text = SelectedRoi.Width.ToString(CultureInfo.InvariantCulture);
            HeightInput.Text = SelectedRoi.Height.ToString(CultureInfo.InvariantCulture);
            RoiText.Text = "원본 픽셀 기준 · 최소 2×2px · Enter 또는 입력란 이동 시 반영";
        }
        finally { _updatingInputs = false; }
    }
    internal void CommitNumber(TextBox input)
    {
        if (_updatingInputs || _closing || IsDragging || !Enum.TryParse<RoiField>(input.Tag?.ToString(), out var field)) { return; }
        if (decimal.TryParse(input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            var updated = RoiEditing.EditNumber(SelectedRoi, field, value, _sourceWidth, _sourceHeight);
            SelectedRoi = updated;
            UpdateSelectionVisual(); UpdateNumbers();
            if (value != decimal.Parse(input.Text, CultureInfo.InvariantCulture))
                RoiText.Text = "이미지 경계 또는 최소 크기에 맞춰 값을 보정했습니다.";
        }
        else
        {
            UpdateNumbers();
            RoiText.Text = "정수를 입력하세요. 이전 값으로 복원했습니다.";
        }
    }
    private void Number_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitNumber((TextBox)sender);
    private void Number_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitNumber((TextBox)sender); e.Handled = true; }
    }
    private void CommitFocusedNumber()
    {
        if (Keyboard.FocusedElement is TextBox input && (input == XInput || input == YInput || input == WidthInput || input == HeightInput))
            CommitNumber(input);
    }
    private void FullFrame_Click(object sender, RoutedEventArgs e) => SetRoi(new(0, 0, _sourceWidth, _sourceHeight));
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        CancelRoiDrag();
        CommitFocusedNumber();
        DialogResult = true;
    }
    private void GridToggle_Click(object sender, RoutedEventArgs e)
    {
        _sessionGridVisible = !_sessionGridVisible; UpdateGridState();
    }
    private void UpdateGridState()
    {
        PixelGrid.Visibility = _sessionGridVisible ? Visibility.Visible : Visibility.Collapsed;
        GridToggleButton.Content = FindResource(_sessionGridVisible ? "Icon.GridOn" : "Icon.GridOff");
        GridToggleButton.ToolTip = _sessionGridVisible ? "그리드 숨기기 (100px)" : "그리드 표시 (100px)";
        GridToggleButton.Background = (Brush)FindResource(_sessionGridVisible ? "InkBrush" : "SurfaceBrush");
        GridToggleButton.Foreground = (Brush)FindResource(_sessionGridVisible ? "CanvasBrush" : "InkBrush");
    }
    private static BitmapImage DecodePng(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }
}
