namespace DiffVideo.Core;

public static class RoiBounds
{
    /// <summary>Keep a fixed crop inside the display-oriented source, with at least 2 pixels per axis.</summary>
    public static PixelRect Clamp(PixelRect roi, int frameWidth, int frameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameWidth, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameHeight, 2);
        var x = Math.Clamp(roi.X, 0, frameWidth - 2);
        var y = Math.Clamp(roi.Y, 0, frameHeight - 2);
        return new(x, y, Math.Clamp(roi.Width, 2, frameWidth - x), Math.Clamp(roi.Height, 2, frameHeight - y));
    }
}
