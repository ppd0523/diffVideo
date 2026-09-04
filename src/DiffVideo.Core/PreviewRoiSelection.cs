namespace DiffVideo.Core;

/// <summary>Maps a canvas-space drag through the same transform used to draw the source player.</summary>
public static class PreviewRoiSelection
{
    public static PixelRect? Map(VideoTrack track, double startX, double startY, double endX, double endY)
    {
        if (!double.IsFinite(startX) || !double.IsFinite(startY) || !double.IsFinite(endX) || !double.IsFinite(endY)) { return null; }
        var layout = PreviewLayout.From(track);
        var clip = layout.Clip;
        if (layout.ScaleX <= 0 || layout.ScaleY <= 0 || clip.Width <= 0 || clip.Height <= 0) { return null; }
        if (startX < clip.X || startX > clip.X + clip.Width || startY < clip.Y || startY > clip.Y + clip.Height) { return null; }
        endX = Math.Clamp(endX, clip.X, clip.X + clip.Width);
        endY = Math.Clamp(endY, clip.Y, clip.Y + clip.Height);
        if (Math.Abs(startX - endX) < 0.001 || Math.Abs(startY - endY) < 0.001) { return null; }
        var roi = track.Roi;
        var left = (int)Math.Clamp(Math.Round((Math.Min(startX, endX) - layout.TranslateX) / layout.ScaleX), roi.X, roi.X + roi.Width - 2);
        var top = (int)Math.Clamp(Math.Round((Math.Min(startY, endY) - layout.TranslateY) / layout.ScaleY), roi.Y, roi.Y + roi.Height - 2);
        var right = (int)Math.Clamp(Math.Round((Math.Max(startX, endX) - layout.TranslateX) / layout.ScaleX), left + 2, roi.X + roi.Width);
        var bottom = (int)Math.Clamp(Math.Round((Math.Max(startY, endY) - layout.TranslateY) / layout.ScaleY), top + 2, roi.Y + roi.Height);
        return RoiBounds.Clamp(new(left, top, right - left, bottom - top), track.Media.DisplayWidth, track.Media.DisplayHeight);
    }
}
