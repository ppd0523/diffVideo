using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DiffVideo.Core;

namespace DiffVideo.App.Controls;

public sealed class AxisRuler : FrameworkElement
{
    private static readonly FontFamily D2CodingFont = new(new Uri("pack://application:,,,/"), "./DiffVideo;component/Assets/Fonts/#D2Coding");
    public bool Vertical { get; set; }
    public bool SourcePixels { get; set; }
    public double Scale { get; private set; } = 1;
    public double Origin { get; private set; }
    public double Extent { get; private set; }
    public void Configure(double scale, double origin, double extent)
    {
        Scale = scale; Origin = origin; Extent = extent; InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Scale <= 0 || !double.IsFinite(Scale)) { return; }
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        var size = Vertical ? ActualHeight : ActualWidth;
        var minor = SourcePixels ? 100 : TimelineViewport.TickInterval(Scale);
        var labelEvery = SourcePixels ? new[] { 100d, 200, 500, 1000, 2000, 5000, 10000 }.FirstOrDefault(n => n * Scale >= (Vertical ? 28 : 48), 10000) : minor;
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(168, 162, 158)), 1);
        var first = Math.Max(0, Math.Ceiling(-Origin / Scale / minor) * minor);
        var last = Math.Min(Extent, (size - Origin) / Scale);
        for (var value = first; value <= last + 0.0001; value += minor)
        {
            var position = Origin + value * Scale;
            var labeled = Math.Abs(value / labelEvery - Math.Round(value / labelEvery)) < 0.0001;
            if (Vertical)
            {
                dc.DrawLine(pen, new Point(ActualWidth - (labeled ? 8 : 4), position), new Point(ActualWidth, position));
            }
            else
            {
                dc.DrawLine(pen, new Point(position, ActualHeight - (labeled ? 7 : 3)), new Point(position, ActualHeight));
            }
            if (!labeled) { continue; }
            var text = new FormattedText(value.ToString("0", CultureInfo.InvariantCulture) + (SourcePixels ? "" : "s"), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(D2CodingFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                11, new SolidColorBrush(Color.FromRgb(87, 83, 78)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var point = Vertical
                ? new Point(Math.Max(0, ActualWidth - text.Width - 10), Math.Clamp(position - text.Height / 2, 0, Math.Max(0, ActualHeight - text.Height)))
                : new Point(Math.Clamp(position + 3, 0, Math.Max(0, ActualWidth - text.Width)), 0);
            dc.DrawText(text, point);
        }
        dc.Pop();
    }
}
