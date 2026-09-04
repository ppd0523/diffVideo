namespace DiffVideo.Core;

public enum LabelAnchor { TopLeft, TopRight, BottomLeft, BottomRight }

// Coordinates are output pixels relative to the destination rectangle, not source/ROI pixels.
// Kept per track so a later positioning UI need not change the export contract.
public sealed record LabelPosition(int X = 12, int Y = 12, LabelAnchor Anchor = LabelAnchor.TopLeft);
public sealed record LabelStyle(double FontSize = 22, uint ForegroundArgb = 0xFFFFFFFF,
    uint BackgroundArgb = 0xA6000000, int Padding = 6, int MaximumLines = 2);
public sealed record FileNameLabel(bool Enabled, LabelPosition Position, LabelStyle Style)
{
    public static FileNameLabel Default(bool enabled) => new(enabled, new(), new());
}
