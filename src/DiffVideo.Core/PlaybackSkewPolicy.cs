namespace DiffVideo.Core;

/// <summary>
/// Decides when a lagging preview track shows a buffering badge and when its player is re-seeked.
/// Session scoped: observations live as long as the session and are never written to disk.
/// </summary>
public sealed class PlaybackSkewPolicy
{
    // ponytail: estimates, not measurements. Tune after profiling real four-video playback.
    public const double DefaultBadgeSeconds = 0.1;
    public const double MinimumBadgeSeconds = 0.1;
    public const double MaximumBadgeSeconds = 1.0;
    public const double ReseekMultiplier = 5;
    public const double MinimumReseekSeconds = 0.5;
    public const double MaximumReseekSeconds = 2.0;
    public const int MinimumSamples = 50;
    public const double BadgePercentile = 0.95;

    private const int SampleCapacity = 1000;
    private const int RecomputeInterval = 25;

    private readonly List<double> _samples = new(SampleCapacity);
    private int _cursor;
    private int _sinceRecompute;
    private double _badgeSeconds = DefaultBadgeSeconds;

    /// <summary>Seconds a track may lag before its badge appears. Adapts once enough samples exist.</summary>
    public double BadgeSeconds => _badgeSeconds;

    /// <summary>
    /// Seconds a self-clocked player may drift before it is re-seeked. Derived from the badge
    /// threshold rather than measured on its own, so a re-seek hitch cannot lower the very
    /// threshold that triggered it.
    /// </summary>
    public double ReseekSeconds =>
        Math.Clamp(_badgeSeconds * ReseekMultiplier, MinimumReseekSeconds, MaximumReseekSeconds);

    public int SampleCount => _samples.Count;

    /// <summary>Records one observed lag. Negative and non-finite values are ignored, never clamped.</summary>
    public void Observe(double skewSeconds)
    {
        if (!double.IsFinite(skewSeconds) || skewSeconds < 0)
        {
            return;
        }

        if (_samples.Count < SampleCapacity)
        {
            _samples.Add(skewSeconds);
        }
        else
        {
            _samples[_cursor] = skewSeconds;
            _cursor = (_cursor + 1) % SampleCapacity;
        }

        if (++_sinceRecompute >= RecomputeInterval)
        {
            _sinceRecompute = 0;
            _badgeSeconds = _samples.Count < MinimumSamples
                ? DefaultBadgeSeconds
                : Math.Clamp(Percentile(_samples, BadgePercentile), MinimumBadgeSeconds, MaximumBadgeSeconds);
        }
    }

    /// <summary>Nearest-rank percentile; exact on short samples where interpolation would invent values.</summary>
    private static double Percentile(List<double> samples, double percentile)
    {
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        var rank = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }
}
