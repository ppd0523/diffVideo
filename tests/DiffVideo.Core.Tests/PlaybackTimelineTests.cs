using DiffVideo.Core;

namespace DiffVideo.Core.Tests;

public sealed class PlaybackTimelineTests
{
    [Theory]
    [InlineData(3.01, 30, 3.0)]
    [InlineData(3.02, 30, 3.033333333333333)]
    [InlineData(-1, 30, 0)]
    public void SnapVideoStart_UsesOutputFrameGrid(double input, int fps, double expected)
    {
        Assert.Equal(expected, PlaybackTimeline.SnapVideoStart(input, fps, 10), precision: 9);
    }

    [Fact]
    public void SnapAudioStart_UsesOneMillisecondGrid()
    {
        Assert.Equal(1.235, PlaybackTimeline.SnapAudioStart(1.2346, 10), precision: 9);
        Assert.Equal(5.049, PlaybackTimeline.SnapAudioStart(10, 5.0499), precision: 9);
    }

    [Fact]
    public void LastFrame_IsOneFrameBeforeExactDuration()
    {
        Assert.Equal(4.966666666666667, PlaybackTimeline.LastFrameSeconds(5, 30), precision: 9);
        Assert.Equal(4.966666666666667, PlaybackTimeline.SnapPlayhead(5, 30, 5), precision: 9);
    }

    [Fact]
    public void VideoStart_ClampsToLastOutputFrameAndStaysOnGrid()
    {
        Assert.Equal(5.033333333333333, PlaybackTimeline.SnapVideoStart(10, 30, 5.05), precision: 9);
    }
}
