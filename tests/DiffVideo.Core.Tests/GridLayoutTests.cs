using DiffVideo.Core;

namespace DiffVideo.Core.Tests;

public sealed class GridLayoutTests
{
    [Fact]
    public void OneVideoFillsTheCanvas()
    {
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), GridLayout.Cell(0, 1, 1920, 1080));
    }

    [Theory]
    [InlineData(1920)]
    [InlineData(1281)]
    public void TwoVideosKeepTheOriginalLeftAndRightHalves(int width)
    {
        // The rule the editor used before it took an arbitrary number of videos.
        var half = width / 2;
        Assert.Equal(new PixelRect(0, 0, half, 720), GridLayout.Cell(0, 2, width, 720));
        Assert.Equal(new PixelRect(half, 0, width - half, 720), GridLayout.Cell(1, 2, width, 720));
    }

    [Fact]
    public void ThreeVideosUseATwoByTwoGridAndLeaveTheLastCellFree()
    {
        Assert.Equal(2, GridLayout.Columns(3));
        Assert.Equal(2, GridLayout.Rows(3));
        Assert.Equal(new PixelRect(0, 0, 960, 540), GridLayout.Cell(0, 3, 1920, 1080));
        Assert.Equal(new PixelRect(960, 0, 960, 540), GridLayout.Cell(1, 3, 1920, 1080));
        Assert.Equal(new PixelRect(0, 540, 960, 540), GridLayout.Cell(2, 3, 1920, 1080));
    }

    [Fact]
    public void FourVideosFillATwoByTwoGrid()
    {
        Assert.Equal(new PixelRect(960, 540, 960, 540), GridLayout.Cell(3, 4, 1920, 1080));
    }

    [Theory]
    [InlineData(1, 1920, 1080)]
    [InlineData(2, 1920, 1080)]
    [InlineData(3, 1279, 721)]
    [InlineData(4, 1279, 721)]
    public void CellsTileTheCanvasWithoutGapsOrOverhang(int count, int width, int height)
    {
        var columns = GridLayout.Columns(count);
        var rows = GridLayout.Rows(count);
        for (var index = 0; index < columns * rows; index++)
        {
            var cell = GridLayout.Cell(index, count, width, height);
            Assert.True(cell.Width > 0 && cell.Height > 0, $"Cell {index} is empty: {cell}");

            // A cell ends exactly where its neighbour starts, and the last one ends on the canvas edge.
            var isLastColumn = index % columns == columns - 1;
            var isLastRow = index / columns == rows - 1;
            var right = cell.X + cell.Width;
            var bottom = cell.Y + cell.Height;
            Assert.Equal(isLastColumn ? width : GridLayout.Cell(index + 1, count, width, height).X, right);
            if (isLastRow)
            {
                Assert.Equal(height, bottom);
            }
            else
            {
                Assert.Equal(GridLayout.Cell(index + columns, count, width, height).Y, bottom);
            }
        }
    }
}
