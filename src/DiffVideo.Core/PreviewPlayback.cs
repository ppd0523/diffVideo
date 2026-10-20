namespace DiffVideo.Core;

public readonly record struct SourcePosition(double Seconds, bool IsActive);

public static class PreviewTiming
{
    public static SourcePosition Map(double timelineSeconds, VideoTrack track)
    {
        var duration = track.Media.VideoDuration?.TotalSeconds ?? track.Media.Duration.TotalSeconds;
        var local = timelineSeconds - track.Start.TotalSeconds;
        return new(Math.Clamp(local, 0, LastFrameSeconds(track.Media)), local >= 0 && local < duration);
    }

    /// <summary>
    /// A seek time that FFmpeg (-ss returns the first frame at or after it) resolves to the last frame.
    /// The last frame start is only estimated from the average fps, so aim half a frame early:
    /// variable-rate phone videos can end slightly before duration - 1/fps, and seeking past
    /// the real last frame yields no frame at all.
    /// </summary>
    public static double LastFrameSeconds(MediaInfo media)
    {
        var duration = media.VideoDuration?.TotalSeconds ?? media.Duration.TotalSeconds;
        var frameDuration = 1 / Math.Max(1, media.FramesPerSecond);
        return Math.Max(0, duration - frameDuration * 1.5);
    }
}

public readonly record struct PreviewRect(double X, double Y, double Width, double Height);
public readonly record struct PreviewLayout(double ScaleX, double ScaleY, double TranslateX, double TranslateY, PreviewRect Clip)
{
    public static PreviewLayout From(VideoTrack track)
    {
        var roi = track.Roi;
        var dst = track.Destination;
        var sx = dst.Width / (double)roi.Width;
        var sy = dst.Height / (double)roi.Height;
        if (track.FitMode != VideoFitMode.Stretch)
        {
            sx = sy = track.FitMode == VideoFitMode.Fit ? Math.Min(sx, sy) : Math.Max(sx, sy);
        }

        var roiX = dst.X + (dst.Width - roi.Width * sx) / 2;
        var roiY = dst.Y + (dst.Height - roi.Height * sy) / 2;
        var x = Math.Max(dst.X, roiX);
        var y = Math.Max(dst.Y, roiY);
        var right = Math.Min(dst.X + dst.Width, roiX + roi.Width * sx);
        var bottom = Math.Min(dst.Y + dst.Height, roiY + roi.Height * sy);
        return new(sx, sy, roiX - roi.X * sx, roiY - roi.Y * sy, new(x, y, right - x, bottom - y));
    }
}
