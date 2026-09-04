namespace DiffVideo.Core;

public enum MediaKind
{
    Video,
    Audio
}

public sealed record MediaInfo(
    string Path,
    MediaKind Kind,
    string Container,
    string Codec,
    TimeSpan Duration,
    int Width = 0,
    int Height = 0,
    double FramesPerSecond = 0,
    bool HasAudio = false,
    string AudioCodec = "",
    int AudioSampleRate = 0,
    int AudioChannels = 0,
    int Rotation = 0,
    bool IsHdr = false,
    TimeSpan? VideoDuration = null)
{
    public string DisplayName => System.IO.Path.GetFileName(Path);

    public int DisplayWidth => Math.Abs(Rotation) % 180 == 90 ? Height : Width;

    public int DisplayHeight => Math.Abs(Rotation) % 180 == 90 ? Width : Height;
}
