namespace DiffVideo.Core;

public enum VideoFitMode
{
    Fit,
    Fill,
    Stretch
}

public enum OutputQuality
{
    High,
    Balanced,
    Small
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static PixelRect FullFrame(MediaInfo media) => new(0, 0, media.DisplayWidth, media.DisplayHeight);
}

public sealed record VideoTrack(
    MediaInfo Media,
    TimeSpan Start,
    PixelRect Roi,
    PixelRect Destination,
    VideoFitMode FitMode,
    bool AspectRatioLocked,
    int ZIndex,
    bool IncludeAudio,
    double Volume)
{
    public FileNameLabel? FileNameLabel { get; init; }
}

public sealed record ExtraAudioTrack(
    MediaInfo Media,
    TimeSpan Start,
    bool IncludeAudio,
    double Volume);

public sealed record OutputSettings(
    int Width,
    int Height,
    int FramesPerSecond,
    TimeSpan Duration,
    OutputQuality Quality);

public sealed record Composition(
    VideoTrack Video1,
    VideoTrack Video2,
    ExtraAudioTrack? ExtraAudio,
    OutputSettings Output);
