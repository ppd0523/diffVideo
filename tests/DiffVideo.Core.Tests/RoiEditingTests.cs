using DiffVideo.Core;

namespace DiffVideo.Core.Tests;

public sealed class RoiEditingTests
{
    private static readonly PixelRect Initial = new(100, 80, 300, 200);

    [Fact]
    public void MovePreservesSizeAndClampsToFrame()
    {
        Assert.Equal(new PixelRect(0, 280, 300, 200), RoiEditing.Move(Initial, -1000, 1000, 640, 480));
        Assert.Equal(new PixelRect(340, 0, 300, 200), RoiEditing.Move(Initial, 1000, -1000, 640, 480));
    }

    [Theory]
    [InlineData(RoiEdges.Left, 120, 80, 280, 200)]
    [InlineData(RoiEdges.Right, 100, 80, 320, 200)]
    [InlineData(RoiEdges.Top, 100, 100, 300, 180)]
    [InlineData(RoiEdges.Bottom, 100, 80, 300, 220)]
    [InlineData(RoiEdges.Left | RoiEdges.Top, 120, 100, 280, 180)]
    [InlineData(RoiEdges.Right | RoiEdges.Top, 100, 100, 320, 180)]
    [InlineData(RoiEdges.Left | RoiEdges.Bottom, 120, 80, 280, 220)]
    [InlineData(RoiEdges.Right | RoiEdges.Bottom, 100, 80, 320, 220)]
    public void ResizeAnchorsOppositeEdge(RoiEdges edges, int x, int y, int w, int h) =>
        Assert.Equal(new PixelRect(x, y, w, h), RoiEditing.Resize(Initial, edges, 20, 20, 640, 480));

    [Fact]
    public void ResizeCannotCrossOppositeEdges()
    {
        Assert.Equal(new PixelRect(398, 278, 2, 2), RoiEditing.Resize(Initial, RoiEdges.Left | RoiEdges.Top, 1000, 1000, 640, 480));
        Assert.Equal(new PixelRect(100, 80, 2, 2), RoiEditing.Resize(Initial, RoiEdges.Right | RoiEdges.Bottom, -1000, -1000, 640, 480));
    }

    [Theory]
    [InlineData(600, 450, 200, 100, 200, 100, 400, 350)]
    [InlineData(-10, -10, 900, 900, 0, 0, 640, 480)]
    [InlineData(640, 480, 640, 480, 638, 478, 2, 2)]
    public void DrawSupportsReverseDragAndBoundaries(double x1, double y1, double x2, double y2, int x, int y, int w, int h) =>
        Assert.Equal(new PixelRect(x, y, w, h), RoiEditing.Draw(x1, y1, x2, y2, 640, 480));

    [Theory]
    [InlineData(RoiField.X, 340, 80, 300, 200)]
    [InlineData(RoiField.Y, 100, 280, 300, 200)]
    [InlineData(RoiField.Width, 100, 80, 540, 200)]
    [InlineData(RoiField.Height, 100, 80, 300, 400)]
    public void NumericOverflowClampsWithoutChangingOtherFields(RoiField field, int x, int y, int w, int h) =>
        Assert.Equal(new PixelRect(x, y, w, h), RoiEditing.EditNumber(Initial, field, decimal.MaxValue, 640, 480));

    [Fact]
    public void NegativeNumericInputsClamp()
    {
        Assert.Equal(0, RoiEditing.EditNumber(Initial, RoiField.X, -5, 640, 480).X);
        Assert.Equal(2, RoiEditing.EditNumber(Initial, RoiField.Width, -5, 640, 480).Width);
    }

    [Fact]
    public void RepeatedOperationsRemainValid()
    {
        var random = new Random(24);
        var roi = Initial;
        for (var i = 0; i < 1000; i++)
        {
            roi = RoiEditing.Move(roi, random.Next(-800, 800), random.Next(-800, 800), 640, 480);
            roi = RoiEditing.Resize(roi, RoiEdges.Right | RoiEdges.Bottom, random.Next(-800, 800), random.Next(-800, 800), 640, 480);
            Assert.Equal(roi, RoiBounds.Clamp(roi, 640, 480));
        }
    }
}
