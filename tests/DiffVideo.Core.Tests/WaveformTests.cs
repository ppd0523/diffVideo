namespace DiffVideo.Core.Tests;

public sealed class WaveformTests
{
    [Fact]
    public void Peaks_TakesAbsoluteMaximumPerBucket()
    {
        short[] samples = [100, -16384, 0, 0, 32767, -32768];
        var peaks = Waveform.Peaks(samples, 3);
        Assert.Equal([0.5, 0, 1], peaks);
    }

    [Fact]
    public void Scale_OneLoudPeakDoesNotFlattenTheRest()
    {
        // 조용한 음원(0.05) 가운데 튀는 피크 하나(1.0)
        var peaks = Enumerable.Repeat(0.05, 99).Append(1.0).ToArray();
        var levels = Waveform.Scale(peaks);
        Assert.Equal(Waveform.Q3Height, levels[0], 6);
        Assert.Equal(1, levels[^1]);
    }

    [Fact]
    public void Scale_MapsQ3ToSixtyPercentAndClipsAbove()
    {
        double[] peaks = [0.1, 0.2, 0.3, 0.4, 0.8];
        var levels = Waveform.Scale(peaks);
        Assert.Equal(0.6, levels[3], 6);
        Assert.Equal(0.3, levels[1], 6);
        Assert.Equal(1, levels[4]);
    }

    [Fact]
    public void Scale_SilenceStaysFlat()
    {
        Assert.All(Waveform.Scale([0, 0, 0]), level => Assert.Equal(0, level));
        Assert.Empty(Waveform.Scale([]));
    }
}
