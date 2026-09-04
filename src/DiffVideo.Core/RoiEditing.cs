namespace DiffVideo.Core;

[Flags]
public enum RoiEdges { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }
public enum RoiField { X, Y, Width, Height }

/// <summary>Display-oriented source pixels, independent of window size and preview scale.</summary>
public static class RoiEditing
{
    public static PixelRect Move(PixelRect before, double dx, double dy, int width, int height) => before with
    {
        X = RoundClamp(before.X + dx, 0, width - before.Width),
        Y = RoundClamp(before.Y + dy, 0, height - before.Height)
    };
    public static PixelRect Resize(PixelRect before, RoiEdges edges, double dx, double dy, int width, int height)
    {
        var left = before.X; var top = before.Y;
        var right = before.X + before.Width; var bottom = before.Y + before.Height;
        if (edges.HasFlag(RoiEdges.Left)) { left = RoundClamp(left + dx, 0, right - 2); }
        if (edges.HasFlag(RoiEdges.Right)) { right = RoundClamp(right + dx, left + 2, width); }
        if (edges.HasFlag(RoiEdges.Top)) { top = RoundClamp(top + dy, 0, bottom - 2); }
        if (edges.HasFlag(RoiEdges.Bottom)) { bottom = RoundClamp(bottom + dy, top + 2, height); }
        return new(left, top, right - left, bottom - top);
    }
    public static PixelRect Draw(double x1, double y1, double x2, double y2, int width, int height)
    {
        var left = RoundClamp(Math.Min(x1, x2), 0, width - 2);
        var top = RoundClamp(Math.Min(y1, y2), 0, height - 2);
        var right = RoundClamp(Math.Max(x1, x2), left + 2, width);
        var bottom = RoundClamp(Math.Max(y1, y2), top + 2, height);
        return new(left, top, right - left, bottom - top);
    }
    public static PixelRect EditNumber(PixelRect before, RoiField field, decimal value, int width, int height) => field switch
    {
        RoiField.X => before with { X = (int)Math.Clamp(value, 0, width - before.Width) },
        RoiField.Y => before with { Y = (int)Math.Clamp(value, 0, height - before.Height) },
        RoiField.Width => before with { Width = (int)Math.Clamp(value, 2, width - before.X) },
        RoiField.Height => before with { Height = (int)Math.Clamp(value, 2, height - before.Y) },
        _ => before
    };
    private static int RoundClamp(double value, int minimum, int maximum) => (int)Math.Round(Math.Clamp(value, minimum, maximum));
}
