using System.Windows;
using System.Windows.Media;

namespace DiffVideo.App.Controls;

public sealed class SourcePixelGrid : FrameworkElement
{
    public const int Spacing = 100;
    public Rect ImageBounds { get; private set; }
    public double PixelScale { get; private set; }
    private int _width;
    private int _height;
    public SourcePixelGrid() { IsHitTestVisible = false; }
    public void Configure(Rect imageBounds, int sourceWidth, int sourceHeight)
    {
        ImageBounds = imageBounds; _width = sourceWidth; _height = sourceHeight;
        PixelScale = imageBounds.Width / Math.Max(1, sourceWidth); InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (PixelScale <= 0 || ImageBounds.IsEmpty) { return; }
        dc.PushClip(new RectangleGeometry(ImageBounds));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 128, 128, 128)), 1);
        for (var x = Spacing; x < _width; x += Spacing)
        {
            var position = ImageBounds.Left + x * PixelScale;
            dc.DrawLine(pen, new Point(position, ImageBounds.Top), new Point(position, ImageBounds.Bottom));
        }
        for (var y = Spacing; y < _height; y += Spacing)
        {
            var position = ImageBounds.Top + y * PixelScale;
            dc.DrawLine(pen, new Point(ImageBounds.Left, position), new Point(ImageBounds.Right, position));
        }
        dc.Pop();
    }
}
