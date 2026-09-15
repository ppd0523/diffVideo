using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.Core.Tests;

public sealed class ExportRangeTests
{
    [Fact]
    public void DefaultOutputExportsEntireTimeline()
    {
        var output = TestComposition.Create().Output;
        Assert.Equal(TimeSpan.Zero, output.ExportStart);
        Assert.Equal(output.Duration, output.EffectiveExportEnd);
        Assert.Equal(output.Duration, output.ExportDuration);
    }

    [Theory]
    [InlineData(0, 15, 15, 0, 15)]
    [InlineData(8, 12, 5, 4.9666666667, 5)]
    [InlineData(-2, 20, 15, 0, 15)]
    [InlineData(1.01, 2.02, 15, 1, 2.0333333333)]
    public void NormalizeSnapsAndKeepsRangeInsideTimeline(double start, double end, double duration, double expectedStart, double expectedEnd)
    {
        var range = ExportRange.Normalize(start, end, duration, 30);
        Assert.Equal(expectedStart, range.Start, 6);
        Assert.Equal(expectedEnd, range.End, 6);
    }

    [Fact]
    public void BoundariesCannotCrossAndKeepAtLeastOneFrame()
    {
        var range = new ExportRange(2, 5);
        Assert.Equal(new ExportRange(2, 5), range.MoveStart(2, 30));
        Assert.Equal(5 - 1d / 30, range.MoveStart(9, 30).Start, 8);
        Assert.Equal(2 + 1d / 30, range.MoveEnd(0, 10, 30).End, 8);
        Assert.Equal(10, range.MoveEnd(100, 10, 30).End);
    }

    [Fact]
    public void NonFrameAlignedFullEndIsPreserved()
    {
        var range = ExportRange.Normalize(0, 10.01, 10.01, 30);
        Assert.Equal(10.01, range.End);
        Assert.True(range.MoveStart(20, 30).Duration >= 1d / 30);
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(0, 16)]
    public void ValidationRejectsInvalidExportRange(double start, double end)
    {
        var composition = TestComposition.Create();
        composition = composition with { Output = composition.Output with { ExportStart = TimeSpan.FromSeconds(start), ExportEnd = TimeSpan.FromSeconds(end) } };
        Assert.Contains(CompositionValidator.Validate(composition), issue => issue.Code == "EXPORT_RANGE");
    }

    [Fact]
    public void ExportTrimsVideoAndAudioAfterCompositionAndResetsTimestamps()
    {
        var composition = TestComposition.Create();
        composition = composition with { Output = composition.Output with { ExportStart = TimeSpan.FromSeconds(2), ExportEnd = TimeSpan.FromSeconds(5) } };
        var invocation = FfmpegCommandBuilder.BuildExport(composition, "out.mp4", H264Encoder.MediaFoundation);
        var arguments = invocation.Arguments.ToList();
        var graph = arguments[arguments.IndexOf("-filter_complex") + 1];
        Assert.Contains("[vout]trim=start_frame=60:end_frame=150,setpts=PTS-STARTPTS[exportvideo]", graph);
        Assert.Contains("[aout]atrim=start=2:end=5,asetpts=PTS-STARTPTS[exportaudio]", graph);
        Assert.Contains("adelay=3000:all=1", graph);
        Assert.Equal("3", arguments[arguments.IndexOf("-t") + 1]);
        Assert.Equal(TimeSpan.FromSeconds(15), composition.Output.Duration);
        Assert.DoesNotContain("[exportvideo]", string.Join(" ", FfmpegCommandBuilder.BuildStillPreview(composition, 7).Arguments));
    }

    [Fact]
    public void SingleFrameRangeUsesFrameIndicesWithoutTimeRoundingExtraFrame()
    {
        var composition = TestComposition.Create();
        composition = composition with { Output = composition.Output with { ExportStart = TimeSpan.FromSeconds(1d / 30), ExportEnd = TimeSpan.FromSeconds(2d / 30) } };
        var invocation = FfmpegCommandBuilder.BuildExport(composition, "out.mp4", H264Encoder.MediaFoundation);
        Assert.Contains(invocation.Arguments, value => value.Contains("trim=start_frame=1:end_frame=2,"));
    }
}
