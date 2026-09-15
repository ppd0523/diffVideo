namespace DiffVideo.Core;

/// <summary>Output-pixel placement; the output canvas clips without changing this rectangle.</summary>
public static class DestinationEditing
{
    public static PixelRect Apply(PixelRect before, RoiEdges edges, double dx, double dy, double? aspect)
    {
        if (dx == 0 && dy == 0) { return before; }
        if (edges == RoiEdges.None)
        {
            return before with { X = Coordinate(before.X + dx), Y = Coordinate(before.Y + dy) };
        }
        var horizontal = (edges & (RoiEdges.Left | RoiEdges.Right)) != 0;
        var vertical = (edges & (RoiEdges.Top | RoiEdges.Bottom)) != 0;
        var width = (double)before.Width;
        var height = (double)before.Height;
        if (horizontal) { width += edges.HasFlag(RoiEdges.Left) ? -dx : dx; }
        if (vertical) { height += edges.HasFlag(RoiEdges.Top) ? -dy : dy; }
        if (aspect is > 0)
        {
            if (horizontal && (!vertical || Math.Abs(dx) >= Math.Abs(dy * aspect.Value)))
            {
                width = Math.Max(Math.Max(2, 2 * aspect.Value), width);
                height = width / aspect.Value;
            }
            else
            {
                height = Math.Max(Math.Max(2, 2 / aspect.Value), height);
                width = height * aspect.Value;
            }
        }
        var w = Dimension(width);
        var h = Dimension(height);
        var x = edges.HasFlag(RoiEdges.Left) ? (double)before.X + before.Width - w : before.X;
        var y = edges.HasFlag(RoiEdges.Top) ? (double)before.Y + before.Height - h : before.Y;
        // With a locked ratio, side handles expand the other axis about its center.
        if (aspect is > 0 && !horizontal) { x -= (w - before.Width) / 2d; }
        if (aspect is > 0 && !vertical) { y -= (h - before.Height) / 2d; }
        return new(Coordinate(x), Coordinate(y), w, h);
    }

    private static int Coordinate(double value) => (int)Math.Clamp(Math.Round(value), int.MinValue / 2d, int.MaxValue / 2d);
    private static int Dimension(double value) => (int)Math.Clamp(Math.Round(value / 2) * 2, 2, int.MaxValue / 2 - 1);
}
