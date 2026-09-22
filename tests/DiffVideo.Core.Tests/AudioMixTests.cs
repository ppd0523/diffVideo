using DiffVideo.Core;

namespace DiffVideo.Core.Tests;

public sealed class AudioMixTests
{
    private static MediaInfo Video(bool hasAudio = true) =>
        new("v.mp4", MediaKind.Video, "mov,mp4", "h264", TimeSpan.FromSeconds(10), 1920, 1080, 30, hasAudio, "aac", 48000, 2);

    private static MediaInfo Music() =>
        new("m.mp3", MediaKind.Audio, "mp3", "mp3", TimeSpan.FromSeconds(10));

    private static VideoTrack Track(double volume, bool includeAudio = true, bool hasAudio = true)
    {
        var media = Video(hasAudio);
        return new(media, TimeSpan.Zero, PixelRect.FullFrame(media), new(0, 0, 960, 1080),
            VideoFitMode.Fit, true, 0, includeAudio, volume);
    }

    private static Composition Compose(VideoTrack[] videos, params AudioTrack[] audios) =>
        new(videos, audios, new(1920, 1080, 30, TimeSpan.FromSeconds(10), OutputQuality.Balanced));

    [Fact]
    public void TwoEqualVideosSplitTheMixInHalf()
    {
        var mix = Compose([Track(1), Track(1)]).AudioMix().ToArray();
        Assert.Equal([(0, 0.5), (1, 0.5)], mix);
    }

    [Fact]
    public void SharesStayProportionalToTheConfiguredVolumes()
    {
        var mix = Compose([Track(1), Track(1)], new AudioTrack(Music(), TimeSpan.Zero, true, 0.3)).AudioMix().ToArray();
        Assert.Equal(1.0, mix.Sum(item => item.Gain), 9);
        // 1 : 1 : 0.3 out of 2.3
        Assert.Equal(1 / 2.3, mix[0].Gain, 9);
        Assert.Equal(0.3 / 2.3, mix[2].Gain, 9);
        // Audio inputs follow every video input on the ffmpeg command line.
        Assert.Equal(2, mix[2].Input);
    }

    [Fact]
    public void ASingleSourceKeepsFullScale()
    {
        var mix = Compose([Track(0.4)]).AudioMix().ToArray();
        Assert.Equal([(0, 1.0)], mix);
    }

    [Fact]
    public void MutedZeroVolumeAndSilentSourcesAreLeftOut()
    {
        var mix = Compose([Track(1), Track(1, includeAudio: false), Track(0), Track(1, hasAudio: false)])
            .AudioMix().ToArray();
        Assert.Equal([(0, 1.0)], mix);
    }

    [Fact]
    public void EverythingMutedProducesNoMixAtAll()
    {
        Assert.Empty(Compose([Track(1, includeAudio: false)], new AudioTrack(Music(), TimeSpan.Zero, false, 1)).AudioMix());
    }

    [Fact]
    public void GraphAppliesTheSharesRatherThanTheRawVolumes()
    {
        var graph = string.Join(' ', DiffVideo.Infrastructure.FfmpegCommandBuilder
            .BuildExport(Compose([Track(1), Track(1)]), "out.mp4", DiffVideo.Infrastructure.H264Encoder.MediaFoundation)
            .Arguments);
        Assert.Contains("volume=0.5", graph);
        Assert.DoesNotContain("volume=1,", graph);
    }
}
