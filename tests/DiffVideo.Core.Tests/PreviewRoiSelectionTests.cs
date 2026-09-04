using DiffVideo.Core;

namespace DiffVideo.Core.Tests;

public sealed class PreviewRoiSelectionTests
{
    private static VideoTrack Track(VideoFitMode fit = VideoFitMode.Fit) => TestComposition.Create().Video1 with
    {
        Roi = new(100, 50, 400, 200), Destination = new(10, 20, 200, 200), FitMode = fit
    };

    [Theory]
    [InlineData(VideoFitMode.Fit, 60, 95, 160, 145, 200, 100, 200, 100)]
    [InlineData(VideoFitMode.Fit, 160, 145, 60, 95, 200, 100, 200, 100)]
    [InlineData(VideoFitMode.Fill, 60, 70, 160, 170, 250, 100, 100, 100)]
    [InlineData(VideoFitMode.Stretch, 60, 70, 160, 170, 200, 100, 200, 100)]
    public void UsesThePlayerTransform(VideoFitMode fit, double x1, double y1, double x2, double y2, int x, int y, int width, int height)
    {
        Assert.Equal(new PixelRect(x, y, width, height), PreviewRoiSelection.Map(Track(fit), x1, y1, x2, y2));
    }

    [Fact]
    public void ClampsTheDragToTheActivePicture()
    {
        Assert.Equal(new PixelRect(200, 100, 300, 150), PreviewRoiSelection.Map(Track(), 60, 95, 9999, 9999));
    }

    [Theory]
    [InlineData(60, 30, 160, 145)] // Fit letterbox is not source video.
    [InlineData(0, 100, 160, 145)]
    [InlineData(60, 95, 60, 145)]
    [InlineData(double.NaN, 95, 160, 145)]
    public void RejectsOutsideOrEmptyDrags(double x1, double y1, double x2, double y2)
    {
        Assert.Null(PreviewRoiSelection.Map(Track(), x1, y1, x2, y2));
    }

    [Fact]
    public void RepeatedCropRetainsOriginalSourceCoordinates()
    {
        var track = Track() with { Roi = new(200, 100, 200, 100) };
        Assert.Equal(new PixelRect(250, 125, 100, 50), PreviewRoiSelection.Map(track, 60, 95, 160, 145));
    }

    [Fact]
    public void TinyCropAtEdgeRemainsInsidePreviousRoi()
    {
        Assert.Equal(new PixelRect(498, 248, 2, 2), PreviewRoiSelection.Map(Track(), 209.9, 169.9, 300, 300));
    }

    [Fact]
    public void RotatedVideoUsesDisplayOrientedCoordinates()
    {
        var media = TestComposition.VideoMedia("portrait.mp4") with { Rotation = 90 };
        var track = Track() with { Media = media, Roi = PixelRect.FullFrame(media), Destination = new(0, 0, 200, 200) };
        Assert.Equal(new PixelRect(0, 0, 1080, 1920), PreviewRoiSelection.Map(track, 43.75, 0, 156.25, 200));
    }
}
