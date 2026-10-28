namespace DiffVideo.Core;

public static class Waveform
{
    // 상위 이상치 한두 개에 끌려 전체가 납작해지지 않도록 Q3를 기준으로 맞춘다.
    public const double Q3Height = 0.6;

    /// <summary>16bit PCM 샘플을 bucketCount개 구간의 절대값 피크(0~1)로 줄인다.</summary>
    public static double[] Peaks(ReadOnlySpan<short> samples, int bucketCount)
    {
        if (samples.IsEmpty || bucketCount <= 0)
        {
            return [];
        }

        var count = Math.Min(bucketCount, samples.Length);
        var peaks = new double[count];
        for (var bucket = 0; bucket < count; bucket++)
        {
            var from = (int)((long)samples.Length * bucket / count);
            var to = (int)((long)samples.Length * (bucket + 1) / count);
            var peak = 0;
            for (var index = from; index < to; index++)
            {
                peak = Math.Max(peak, Math.Abs((int)samples[index]));
            }

            peaks[bucket] = peak / 32768d;
        }

        return peaks;
    }

    /// <summary>Q3가 높이 60%가 되도록 배율을 맞추고 1을 넘는 값은 자른다. 화면 표시 전용.</summary>
    public static double[] Scale(IReadOnlyList<double> peaks)
    {
        if (peaks.Count == 0)
        {
            return [];
        }

        var sorted = peaks.Order().ToArray();
        var q3 = sorted[(int)Math.Round((sorted.Length - 1) * 0.75)];
        if (q3 <= 0)
        {
            // 무음 위주: 최대값 기준으로라도 보이게 한다.
            q3 = sorted[^1];
        }

        if (q3 <= 0)
        {
            return new double[peaks.Count];
        }

        var gain = Q3Height / q3;
        return peaks.Select(peak => Math.Min(1, peak * gain)).ToArray();
    }
}
