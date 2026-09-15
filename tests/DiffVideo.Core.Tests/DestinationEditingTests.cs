using DiffVideo.Core;

namespace DiffVideo.Core.Tests;

public sealed class DestinationEditingTests
{
    [Fact]
    public void ClickingHandleWithoutDraggingDoesNotChangeExistingFitBox()
    {
        var before = new PixelRect(0, 0, 960, 1080);
        Assert.Equal(before, DestinationEditing.Apply(before, RoiEdges.Right, 0, 0, 16d / 9));
    }

    [Fact]
    public void MovePreservesSizeAndAllowsOffCanvas()
    {
        Assert.Equal(new PixelRect(-150, -70, 400, 200),
            DestinationEditing.Apply(new(50, 30, 400, 200), RoiEdges.None, -200, -100, null));
    }

    [Theory]
    [InlineData(RoiEdges.Left, -20, 0, 80, 100, 420, 200)]
    [InlineData(RoiEdges.Top, 0, -20, 100, 80, 400, 220)]
    [InlineData(RoiEdges.Right, -20, 0, 100, 100, 380, 200)]
    [InlineData(RoiEdges.Bottom, 0, -20, 100, 100, 400, 180)]
    [InlineData(RoiEdges.Left | RoiEdges.Top, 20, 20, 120, 120, 380, 180)]
    [InlineData(RoiEdges.Right | RoiEdges.Top, 20, 20, 100, 120, 420, 180)]
    [InlineData(RoiEdges.Left | RoiEdges.Bottom, 20, 20, 120, 100, 380, 220)]
    [InlineData(RoiEdges.Right | RoiEdges.Bottom, 20, 20, 100, 100, 420, 220)]
    public void FreeResizeAnchorsOppositeEdges(RoiEdges edges, double dx, double dy, int x, int y, int w, int h)
    {
        Assert.Equal(new PixelRect(x, y, w, h), DestinationEditing.Apply(new(100, 100, 400, 200), edges, dx, dy, null));
    }

    [Fact]
    public void LockedCornerKeepsRatioAndOppositeCorner()
    {
        Assert.Equal(new PixelRect(140, 120, 360, 180),
            DestinationEditing.Apply(new(100, 100, 400, 200), RoiEdges.Left | RoiEdges.Top, 40, 10, 2));
    }

    [Fact]
    public void LockedSideCentersOtherAxis()
    {
        Assert.Equal(new PixelRect(100, 120, 320, 160),
            DestinationEditing.Apply(new(100, 100, 400, 200), RoiEdges.Right, -80, 0, 2));
    }

    [Fact]
    public void CrossingOppositeCornerStopsAtMinimumSize()
    {
        Assert.Equal(new PixelRect(498, 298, 2, 2),
            DestinationEditing.Apply(new(100, 100, 400, 200), RoiEdges.Left | RoiEdges.Top, 1000, 1000, null));
    }

    [Theory]
    [InlineData(-200, -100)]
    [InlineData(1900, 1000)]
    [InlineData(-4000, -3000)]
    public void ValidatorAcceptsPartlyAndFullyOffCanvas(int x, int y)
    {
        var composition = TestComposition.Create();
        composition = composition with { Video1 = composition.Video1 with { Destination = new(x, y, 960, 1080) } };
        Assert.Empty(CompositionValidator.Validate(composition));
        var command = DiffVideo.Infrastructure.FfmpegCommandBuilder.BuildExport(composition, "out.mp4", DiffVideo.Infrastructure.H264Encoder.MediaFoundation);
        Assert.Contains(command.Arguments, argument => argument.Contains($"overlay=x={x}:y={y}:"));
    }
}
