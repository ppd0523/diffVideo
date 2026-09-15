namespace DiffVideo.Core;

/// <summary>Timeline seconds: start is included, end is an exclusive boundary.</summary>
public readonly record struct ExportRange(double Start, double End)
{
    public double Duration => End - Start;

    public static ExportRange Normalize(double start, double end, double duration, int fps)
    {
        var frame = Math.Min(duration, 1d / fps);
        var lastStart = Math.Max(0, Math.Floor((duration - frame) * fps + 1e-9) / fps);
        start = Math.Clamp(Snap(start, fps), 0, lastStart);
        end = end >= duration ? duration : Math.Clamp(Snap(end, fps), start + frame, duration);
        return new(start, end);
    }

    public ExportRange MoveStart(double seconds, int fps)
    {
        var maximum = Math.Max(0, Math.Floor((End - 1d / fps) * fps + 1e-9) / fps);
        return this with { Start = Math.Clamp(Snap(seconds, fps), 0, maximum) };
    }

    public ExportRange MoveEnd(double seconds, double duration, int fps) => this with
    {
        End = seconds >= duration ? duration : Math.Clamp(Snap(seconds, fps), Math.Min(duration, Start + 1d / fps), duration)
    };

    private static double Snap(double seconds, int fps) => Math.Round(seconds * fps, MidpointRounding.AwayFromZero) / fps;
}
