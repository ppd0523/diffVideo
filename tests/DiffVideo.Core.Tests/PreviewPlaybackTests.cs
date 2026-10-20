using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.Core.Tests;

public sealed class PreviewPlaybackTests
{
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(2, 0, true)]
    [InlineData(3.5, 1.5, true)]
    [InlineData(20, 4.95, false)]
    public void SourceTime_RespectsOffsetAndHeldFrames(double timeline, double expected, bool active)
    {
        var track = TestComposition.Create().Videos[0] with { Start = TimeSpan.FromSeconds(2) };
        track = track with { Media = track.Media with { Duration = TimeSpan.FromSeconds(7), VideoDuration = TimeSpan.FromSeconds(5), FramesPerSecond = 30 } };
        var position = PreviewTiming.Map(timeline, track);
        Assert.Equal(expected, position.Seconds, 7);
        Assert.Equal(active, position.IsActive);
    }

    // FFmpeg -ss T returns the first frame at or after T, so the held-frame time must land
    // after the second-to-last frame and no later than the real last frame.
    [Theory]
    [InlineData(5, 30, 4.933333, 4.966667)] // constant 30 fps
    [InlineData(24.457, 3307500d / 110203, 24.389600, 24.422922)] // Jinjo_02.mp4: VFR, last frame is shorter than duration - 1/avg fps
    public void HeldLastFrame_SeeksToTheRealLastFrame(double videoDuration, double fps, double previousFrame, double lastFrame)
    {
        var track = TestComposition.Create().Videos[0];
        track = track with { Media = track.Media with { Duration = TimeSpan.FromSeconds(videoDuration), VideoDuration = TimeSpan.FromSeconds(videoDuration), FramesPerSecond = fps } };
        var held = PreviewTiming.Map(double.MaxValue, track).Seconds;
        Assert.InRange(held, previousFrame + 0.000001, lastFrame);
    }

    [Fact]
    public void FitLayout_ClipsToRoiAndLeavesLetterbox()
    {
        var track = TestComposition.Create().Videos[0] with
        {
            Roi = new(100, 50, 400, 200),
            Destination = new(10, 20, 200, 200),
            FitMode = VideoFitMode.Fit
        };
        var layout = PreviewLayout.From(track);
        Assert.Equal(0.5, layout.ScaleX);
        Assert.Equal(new PreviewRect(10, 70, 200, 100), layout.Clip);
        Assert.Equal(-40, layout.TranslateX);
        Assert.Equal(45, layout.TranslateY);
    }

    [Fact]
    public void FillLayout_ClipsToDestination()
    {
        var track = TestComposition.Create().Videos[0] with
        {
            Roi = new(100, 50, 400, 200),
            Destination = new(10, 20, 200, 200),
            FitMode = VideoFitMode.Fill
        };
        var layout = PreviewLayout.From(track);
        Assert.Equal(1, layout.ScaleX);
        Assert.Equal(new PreviewRect(10, 20, 200, 200), layout.Clip);
        Assert.Equal(-190, layout.TranslateX);
    }

    [Fact]
    public void SourceDecoder_HasOneInputAndNoCompositionFilters()
    {
        var invocation = SourceVideoDecoder.BuildInvocation(TestComposition.Create().Videos[0].Media, 3.5, 30);
        Assert.Single(invocation.Arguments, item => item == "-i");
        Assert.DoesNotContain("-filter_complex", invocation.Arguments);
        Assert.DoesNotContain(invocation.Arguments, value => value.Contains("overlay") || value.Contains("crop="));
        Assert.Contains("3.5", invocation.Arguments);
        Assert.Contains("rawvideo", invocation.Arguments);
    }
}
