using DiffVideo.Core;

namespace DiffVideo.Core.Tests;

public sealed class CompositionValidatorTests
{
    [Theory]
    [InlineData(1919, 1920)]
    [InlineData(1920, 1920)]
    [InlineData(3841, 3840)]
    [InlineData(1, 16)]
    public void NormalizeEvenDimension_ClampsAndReturnsEven(int input, int expected)
    {
        Assert.Equal(expected, CompositionValidator.NormalizeEvenDimension(input, 16, 3840));
    }

    [Fact]
    public void Validate_AcceptsAValidComposition()
    {
        var composition = TestComposition.Create();
        Assert.Empty(CompositionValidator.Validate(composition));
    }

    [Fact]
    public void Validate_RejectsOutOfBoundsRoiAndOddCanvas()
    {
        var composition = TestComposition.Create();
        composition = composition with
        {
            Video1 = composition.Video1 with { Roi = new(1900, 1000, 100, 100) },
            Output = composition.Output with { Width = 1919 }
        };

        var issues = CompositionValidator.Validate(composition);
        Assert.Contains(issues, issue => issue.Code == "VIDEO_1_ROI");
        Assert.Contains(issues, issue => issue.Code == "OUTPUT_WIDTH");
    }

    [Fact]
    public void RotatedMedia_SwapsDisplayDimensions()
    {
        var media = TestComposition.VideoMedia("rotated.mp4") with { Width = 1920, Height = 1080, Rotation = 90 };
        Assert.Equal(1080, media.DisplayWidth);
        Assert.Equal(1920, media.DisplayHeight);
    }
}

internal static class TestComposition
{
    public static Composition Create(bool includeMp3 = true)
    {
        var video1 = VideoMedia("one.mp4");
        var video2 = VideoMedia("two.mp4");
        var track1 = new VideoTrack(video1, TimeSpan.Zero, PixelRect.FullFrame(video1), new(0, 0, 960, 1080), VideoFitMode.Fit, true, 0, true, 1);
        var track2 = new VideoTrack(video2, TimeSpan.FromSeconds(3), PixelRect.FullFrame(video2), new(960, 0, 960, 1080), VideoFitMode.Fill, true, 1, true, 0.8);
        var audio = includeMp3
            ? new ExtraAudioTrack(new("music.mp3", MediaKind.Audio, "mp3", "mp3", TimeSpan.FromSeconds(8)), TimeSpan.FromSeconds(1), true, 0.3)
            : null;
        return new(track1, track2, audio, new(1920, 1080, 30, TimeSpan.FromSeconds(15), OutputQuality.Balanced));
    }

    public static MediaInfo VideoMedia(string path) => new(path, MediaKind.Video, "mov,mp4", "h264", TimeSpan.FromSeconds(10), 1920, 1080, 30, true, "aac", 48000, 2);
}
