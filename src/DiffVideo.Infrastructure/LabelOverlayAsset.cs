namespace DiffVideo.Infrastructure;

/// <summary>A rendered label, positioned within its video destination before layer composition.</summary>
public sealed record LabelOverlayAsset(int VideoIndex, string Path, int X, int Y);
