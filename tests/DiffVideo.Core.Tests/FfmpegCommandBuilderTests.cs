using DiffVideo.Infrastructure;

namespace DiffVideo.Core.Tests;

public sealed class FfmpegCommandBuilderTests
{
    [Fact]
    public void ExportGraph_ContainsFreezeOffsetsAudioMixAndLimiter()
    {
        var invocation = FfmpegCommandBuilder.BuildExport(TestComposition.Create(), "output.mp4.part", H264Encoder.AmdAmf);
        var graph = FilterGraph(invocation);

        Assert.Contains("start_mode=clone:start_duration=3", graph);
        Assert.Contains("stop_mode=clone", graph);
        Assert.Contains("adelay=3000:all=1", graph);
        Assert.Contains("adelay=1000:all=1", graph);
        Assert.Contains("amix=inputs=3", graph);
        Assert.Contains("alimiter=limit=0.95", graph);
        Assert.Contains("h264_amf", invocation.Arguments);
    }

    [Fact]
    public void ExportGraph_UsesSilentAudioWhenEverythingIsMuted()
    {
        var composition = TestComposition.Create(includeMp3: false);
        composition = composition
            .WithVideo(0, composition.Videos[0] with { IncludeAudio = false })
            .WithVideo(1, composition.Videos[1] with { IncludeAudio = false });

        var graph = FilterGraph(FfmpegCommandBuilder.BuildExport(composition, "output.mp4.part", H264Encoder.MediaFoundation));
        Assert.Contains("anullsrc=r=48000:cl=stereo", graph);
        Assert.DoesNotContain("amix=", graph);
    }

    [Fact]
    public void StillPreview_TrimsAtRequestedPlayhead()
    {
        var invocation = FfmpegCommandBuilder.BuildStillPreview(TestComposition.Create(), 4.25);
        Assert.Contains("trim=start=4.25:duration=0.1", FilterGraph(invocation));
        Assert.Equal("pipe:1", invocation.Arguments[^1]);
    }

    [Fact]
    public void AudioPreview_UsesExportMixAndTrimsAtPlayhead()
    {
        var invocation = FfmpegCommandBuilder.BuildAudioPreview(TestComposition.Create(), 4.25);
        var graph = FilterGraph(invocation);

        Assert.Contains("amix=inputs=3", graph);
        Assert.Contains("alimiter=limit=0.95", graph);
        Assert.Contains("atrim=start=4.25", graph);
        Assert.Contains("pcm_s16le", invocation.Arguments);
        Assert.Equal("pipe:1", invocation.Arguments[^1]);
    }

    private static string FilterGraph(FfmpegInvocation invocation)
    {
        var index = invocation.Arguments.ToList().IndexOf("-filter_complex");
        Assert.True(index >= 0);
        return invocation.Arguments[index + 1];
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 2)]
    public void LabelsAreInputsNotFilterTextAndRespectVideoLayers(bool mp3, int firstInput)
    {
        var composition = TestComposition.Create(mp3);
        LabelOverlayAsset[] labels = [new(0, "한글 ' [파일] %.png", 12, 14), new(1, "two.png", 12, 12)];
        var invocation = FfmpegCommandBuilder.BuildExport(composition, "out.mp4", H264Encoder.MediaFoundation, labels);
        var graph = FilterGraph(invocation);
        Assert.Contains(labels[0].Path, invocation.Arguments);
        Assert.DoesNotContain(labels[0].Path, graph);
        Assert.Contains($"[v0][{firstInput}:v]overlay=x=12:y=14", graph);
        Assert.Contains("[base][labeled0]overlay", graph);
        Assert.Contains("[layer0][labeled1]overlay", graph);
        Assert.True(graph.IndexOf("[labeled0]", StringComparison.Ordinal) < graph.IndexOf("[layer0][labeled1]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportRejectsOverwritingSourceBeforeStartingFfmpeg()
    {
        var composition = TestComposition.Create();
        var service = new FfmpegExportService(new("missing.exe", "missing.exe"));
        await Assert.ThrowsAsync<IOException>(() => service.ExportAsync(composition, composition.Videos[0].Media.Path, overwrite: true));
    }
}
