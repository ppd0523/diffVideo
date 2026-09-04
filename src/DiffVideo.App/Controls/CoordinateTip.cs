using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DiffVideo.App.Controls;

public sealed class CoordinateTip : FrameworkElement
{
    private Point _point;
    public string? Coordinates { get; private set; }
    public CoordinateTip() { IsHitTestVisible = false; }
    public void ShowAt(Point cursor, int x, int y)
    {
        Coordinates = $"({x},{y})"; _point = cursor; InvalidateVisual();
    }
    public void Hide() { Coordinates = null; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        if (Coordinates is null) { return; }
        var text = new FormattedText(Coordinates, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var width = text.Width + 12; var height = text.Height + 8;
        var x = _point.X + 16; var y = _point.Y + 18;
        if (x + width > ActualWidth) { x = _point.X - width - 12; }
        if (y + height > ActualHeight) { y = _point.Y - height - 12; }
        x = Math.Clamp(x, 0, Math.Max(0, ActualWidth - width));
        y = Math.Clamp(y, 0, Math.Max(0, ActualHeight - height));
        dc.DrawRoundedRectangle(Brushes.White, new Pen(Brushes.Gray, 1), new Rect(x, y, width, height), 4, 4);
        dc.DrawText(text, new Point(x + 6, y + 4));
    }
}
