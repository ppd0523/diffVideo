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

public sealed record AudioTrack(
    MediaInfo Media,
    TimeSpan Start,
    bool IncludeAudio,
    double Volume);

public sealed record OutputSettings(
    int Width,
    int Height,
    int FramesPerSecond,
    TimeSpan Duration,
    OutputQuality Quality)
{
    public TimeSpan ExportStart { get; init; } = TimeSpan.Zero;
    public TimeSpan? ExportEnd { get; init; }
    public TimeSpan EffectiveExportEnd => ExportEnd ?? Duration;
    public TimeSpan ExportDuration => EffectiveExportEnd - ExportStart;
}

public sealed record Composition(
    IReadOnlyList<VideoTrack> Videos,
    IReadOnlyList<AudioTrack> Audios,
    OutputSettings Output)
{
    /// <summary>
    /// Structural equality over the track list. The generated record equality would compare the
    /// list by reference, so two compositions built from the same editor state would differ.
    /// </summary>
    public bool Equals(Composition? other) =>
        other is not null
        && Output == other.Output
        && Videos.SequenceEqual(other.Videos)
        && Audios.SequenceEqual(other.Audios);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Output);
        foreach (var track in Videos)
        {
            hash.Add(track);
        }

        foreach (var track in Audios)
        {
            hash.Add(track);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Every source that contributes sound, with its share of the mix. Shares sum to one, so
    /// adding a source lowers the others in proportion instead of driving the limiter.
    /// </summary>
    public IEnumerable<(int Input, double Gain)> AudioMix()
    {
        var weights = new List<(int Input, double Weight)>();
        for (var index = 0; index < Videos.Count; index++)
        {
            if (Videos[index] is { IncludeAudio: true, Volume: > 0, Media.HasAudio: true })
            {
                weights.Add((index, Videos[index].Volume));
            }
        }

        for (var index = 0; index < Audios.Count; index++)
        {
            if (Audios[index] is { IncludeAudio: true, Volume: > 0 })
            {
                weights.Add((Videos.Count + index, Audios[index].Volume));
            }
        }

        var total = weights.Sum(item => item.Weight);
        return total <= 0 ? [] : weights.Select(item => (item.Input, item.Weight / total));
    }

    /// <summary>Replaces one video track, leaving every other track and the output untouched.</summary>
    public Composition WithVideo(int index, VideoTrack track)
    {
        var videos = Videos.ToArray();
        videos[index] = track;
        return this with { Videos = videos };
    }
}
