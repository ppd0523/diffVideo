using DiffVideo.Core;

namespace DiffVideo.Core.Tests;

public sealed class TimelineViewportTests
{
    [Fact]
    public void ZoomKeepsPlayheadAnchorAndTimeMapping()
    {
        var view = new TimelineViewport(); view.Resize(1000, 100);
        view.Zoom(2, 30);
        Assert.Equal(300, view.Position(30));
        Assert.Equal(30, view.TimeAt(300));
        Assert.Equal(20, view.PixelsPerSecond);
        Assert.False(view.IsFit);
    }

    [Fact]
    public void ResizingFitRefitsButZoomPreservesScale()
    {
        var view = new TimelineViewport(); view.Resize(1000, 100); view.Resize(800, 100);
        Assert.Equal(8, view.PixelsPerSecond);
        view.Zoom(2, 50); view.Resize(600, 100);
        Assert.Equal(16, view.PixelsPerSecond);
        view.Fit(); Assert.Equal(6, view.PixelsPerSecond); Assert.Equal(0, view.Offset);
    }

    [Fact]
    public void ScrollingClampsAndRevealPagesForward()
    {
        var view = new TimelineViewport(); view.Resize(1000, 100); view.Zoom(4, 0);
        view.Reveal(26);
        Assert.InRange(view.Position(26), 0, 100);
        view.Scroll(double.MaxValue); Assert.Equal(3000, view.Offset);
        view.Scroll(-100); Assert.Equal(0, view.Offset);
    }

    [Theory]
    [InlineData(240, 1)]
    [InlineData(30, 2)]
    [InlineData(10, 10)]
    [InlineData(1, 60)]
    public void TicksHaveAtLeastOneSecondSpacing(double scale, double expected) => Assert.Equal(expected, TimelineViewport.TickInterval(scale));

    [Fact]
    public void ZoomLimitsAndTinyDurationsAreFinite()
    {
        var view = new TimelineViewport(); view.Resize(1000, 0.033); view.Zoom(2, 0);
        Assert.True(double.IsFinite(view.ContentWidth)); Assert.True(view.IsFit);
        view.Resize(1000, 100); view.Zoom(1000, 50);
        Assert.Equal(240, view.PixelsPerSecond);
    }

    [Theory]
    [InlineData(1, true, 10)]
    [InlineData(4, false, 40)]
    [InlineData(1000, false, 240)]
    [InlineData(double.NaN, true, 10)]
    public void RestoreZoomRatioClampsAndStartsAtTimelineBeginning(double ratio, bool isFit, double scale)
    {
        var view = new TimelineViewport();
        view.Resize(1000, 100);
        view.Scroll(500);
        view.RestoreZoomRatio(ratio);
        Assert.Equal(isFit, view.IsFit);
        Assert.Equal(scale, view.PixelsPerSecond);
        Assert.Equal(0, view.Offset);
    }
}
