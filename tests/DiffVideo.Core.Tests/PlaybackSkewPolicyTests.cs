using DiffVideo.Core;

namespace DiffVideo.Core.Tests;

public sealed class PlaybackSkewPolicyTests
{
    private static PlaybackSkewPolicy Observed(params double[] skews)
    {
        var policy = new PlaybackSkewPolicy();
        foreach (var skew in skews)
        {
            policy.Observe(skew);
        }

        return policy;
    }

    private static PlaybackSkewPolicy Repeated(double skew, int count) =>
        Observed(Enumerable.Repeat(skew, count).ToArray());

    [Fact]
    public void FreshPolicyUsesTheDefaults()
    {
        var policy = new PlaybackSkewPolicy();
        Assert.Equal(0.1, policy.BadgeSeconds);
        Assert.Equal(0.5, policy.ReseekSeconds);
    }

    [Fact]
    public void TooFewSamplesKeepTheDefault()
    {
        var policy = Repeated(0.6, PlaybackSkewPolicy.MinimumSamples - 1);
        Assert.Equal(PlaybackSkewPolicy.DefaultBadgeSeconds, policy.BadgeSeconds);
    }

    [Fact]
    public void EnoughSamplesAdaptTheBadgeAndCarryTheReseekWithIt()
    {
        var policy = Repeated(0.3, PlaybackSkewPolicy.MinimumSamples);
        Assert.Equal(0.3, policy.BadgeSeconds, 6);
        Assert.Equal(1.5, policy.ReseekSeconds, 6);
    }

    [Fact]
    public void QuietPlaybackCannotPushTheBadgeBelowItsFloor()
    {
        var policy = Repeated(0.001, PlaybackSkewPolicy.MinimumSamples);
        Assert.Equal(PlaybackSkewPolicy.MinimumBadgeSeconds, policy.BadgeSeconds);
        Assert.Equal(PlaybackSkewPolicy.MinimumReseekSeconds, policy.ReseekSeconds);
    }

    [Fact]
    public void OneTerribleRunCannotDisableTheBadgeEntirely()
    {
        var policy = Repeated(9.0, PlaybackSkewPolicy.MinimumSamples);
        Assert.Equal(PlaybackSkewPolicy.MaximumBadgeSeconds, policy.BadgeSeconds);
        Assert.Equal(PlaybackSkewPolicy.MaximumReseekSeconds, policy.ReseekSeconds);
    }

    [Fact]
    public void OutliersAboveTheNinetyFifthPercentileAreExcluded()
    {
        var samples = Enumerable.Repeat(0.2, 95).Concat(Enumerable.Repeat(5.0, 5)).ToArray();
        Assert.Equal(0.2, Observed(samples).BadgeSeconds, 6);
    }

    [Fact]
    public void NegativeAndNonFiniteObservationsAreDropped()
    {
        var policy = Observed(-1, double.NaN, double.PositiveInfinity, 0.4);
        Assert.Equal(1, policy.SampleCount);
    }
}
