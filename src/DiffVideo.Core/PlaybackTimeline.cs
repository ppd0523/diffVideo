namespace DiffVideo.Core;

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused
}

public static class PlaybackTimeline
{
    public static double SnapVideoStart(double seconds, int framesPerSecond, double durationSeconds) =>
        Math.Clamp(SnapToFrame(seconds, framesPerSecond), 0, LastFrameSeconds(durationSeconds, framesPerSecond));

    public static double SnapPlayhead(double seconds, int framesPerSecond, double durationSeconds) =>
        Math.Clamp(SnapToFrame(seconds, framesPerSecond), 0, LastFrameSeconds(durationSeconds, framesPerSecond));

    public static double SnapAudioStart(double seconds, double durationSeconds)
    {
        var maximum = Math.Floor(Math.Max(0, durationSeconds) * 1000 + 1e-9) / 1000d;
        return Math.Clamp(
            Math.Round(Math.Max(0, seconds) * 1000, MidpointRounding.AwayFromZero) / 1000d,
            0,
            maximum);
    }

    public static double LastFrameSeconds(double durationSeconds, int framesPerSecond)
    {
        if (durationSeconds <= 0 || framesPerSecond <= 0)
        {
            return 0;
        }

        var frameCount = Math.Max(1L, (long)Math.Ceiling(durationSeconds * framesPerSecond - 1e-9));
        return (frameCount - 1) / (double)framesPerSecond;
    }

    private static double SnapToFrame(double seconds, int framesPerSecond)
    {
        if (framesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        }

        return Math.Round(Math.Max(0, seconds) * framesPerSecond, MidpointRounding.AwayFromZero) / framesPerSecond;
    }
}
