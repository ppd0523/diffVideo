using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DiffVideo.App.Controls;

public sealed class CoordinateTip : FrameworkElement
{
    private static readonly FontFamily D2CodingFont = new(new Uri("pack://application:,,,/"), "./DiffVideo;component/Assets/Fonts/#D2Coding");
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
            new Typeface(D2CodingFont, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
            13, new SolidColorBrush(Color.FromRgb(28, 25, 23)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var width = text.Width + 28; var height = text.Height + 16;
        var x = _point.X + 16; var y = _point.Y + 18;
        if (x + width > ActualWidth) { x = _point.X - width - 12; }
        if (y + height > ActualHeight) { y = _point.Y - height - 12; }
        x = Math.Clamp(x, 0, Math.Max(0, ActualWidth - width));
        y = Math.Clamp(y, 0, Math.Max(0, ActualHeight - height));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(250, 250, 249)),
            new Pen(new SolidColorBrush(Color.FromRgb(214, 211, 209)), 1), new Rect(x, y, width, height));
        dc.DrawText(text, new Point(x + 14, y + 8));
    }
}
