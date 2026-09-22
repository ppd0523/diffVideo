namespace DiffVideo.Core;

/// <summary>
/// Default placement for tracks the user has not positioned by hand. Cells tile the canvas
/// exactly, so two videos land on the same left and right halves they always have.
/// </summary>
public static class GridLayout
{
    public static int Columns(int count) => Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));

    public static int Rows(int count) => Math.Max(1, (int)Math.Ceiling(count / (double)Columns(count)));

    /// <summary>
    /// The cell for one track. Edges are computed from the canvas rather than from a cell width,
    /// so rounding never leaves a seam or an overhang on the far edge.
    /// </summary>
    public static PixelRect Cell(int index, int count, int canvasWidth, int canvasHeight)
    {
        var columns = Columns(count);
        var rows = Rows(count);
        var column = index % columns;
        var row = index / columns;
        var left = canvasWidth * column / columns;
        var top = canvasHeight * row / rows;
        return new(left, top,
            canvasWidth * (column + 1) / columns - left,
            canvasHeight * (row + 1) / rows - top);
    }
}
