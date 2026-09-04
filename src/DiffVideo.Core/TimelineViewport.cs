namespace DiffVideo.Core;

/// <summary>Display-only timeline state. Never modifies composition or playback time.</summary>
public sealed class TimelineViewport
{
    public double Duration { get; private set; } = 1;
    public double Width { get; private set; } = 1;
    public double PixelsPerSecond { get; private set; } = 1;
    public double Offset { get; private set; }
    public bool IsFit { get; private set; } = true;
    public double ContentWidth => Math.Max(Width, Duration * PixelsPerSecond);
    public double MaximumOffset => Math.Max(0, ContentWidth - Width);
    public double Position(double seconds) => seconds * PixelsPerSecond - Offset;
    public double TimeAt(double x) => Math.Clamp((x + Offset) / PixelsPerSecond, 0, Duration);

    public void Resize(double width, double duration)
    {
        Width = Math.Max(1, width);
        Duration = Math.Max(0.001, duration);
        PixelsPerSecond = IsFit ? Width / Duration : Math.Max(Width / Duration, PixelsPerSecond);
        Scroll(Offset);
    }

    public void Fit()
    {
        IsFit = true;
        PixelsPerSecond = Width / Duration;
        Offset = 0;
    }

    public void Zoom(double factor, double playhead)
    {
        var anchor = Math.Clamp(Position(playhead), 0, Width);
        PixelsPerSecond = Math.Clamp(PixelsPerSecond * factor, Width / Duration, Math.Max(240, Width / Duration));
        IsFit = Math.Abs(PixelsPerSecond - Width / Duration) < 0.000001;
        Scroll(playhead * PixelsPerSecond - anchor);
    }

    public void Scroll(double offset) => Offset = Math.Clamp(offset, 0, MaximumOffset);

    public void Reveal(double playhead)
    {
        var x = Position(playhead);
        if (x < 0 || x >= Width - 6) { Scroll(playhead * PixelsPerSecond - Width * 0.05); }
    }

    public static double TickInterval(double pixelsPerSecond)
    {
        double[] intervals = [1, 2, 5, 10, 30, 60, 120, 300, 600, 1800, 3600];
        return intervals.FirstOrDefault(step => step * pixelsPerSecond >= 55,
            Math.Ceiling(55 / Math.Max(0.000001, pixelsPerSecond) / 3600) * 3600);
    }
}
