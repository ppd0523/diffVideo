using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.App.Services;

/// <summary>Renders Unicode text once per confirmed export; never renders preview overlays.</summary>
internal sealed class ExportLabelRenderer : IDisposable
{
    private string? _directory;
    public List<LabelOverlayAsset> Assets { get; } = [];

    public static ExportLabelRenderer Create(Composition snapshot)
    {
        var result = new ExportLabelRenderer();
        try
        {
            for (var i = 0; i < snapshot.Videos.Count; i++)
            {
                var track = snapshot.Videos[i];
                if (track.FileNameLabel is not { Enabled: true } label) { continue; }
                result._directory ??= Directory.CreateTempSubdirectory("DiffVideo-labels-").FullName;
                result.Assets.Add(Render(track, label, i, result._directory));
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static LabelOverlayAsset Render(VideoTrack track, FileNameLabel label, int index, string directory)
    {
        var destination = track.Destination;
        var x = Math.Clamp(label.Position.X, 0, Math.Max(0, destination.Width - 16));
        var y = Math.Clamp(label.Position.Y, 0, Math.Max(0, destination.Height - 16));
        var availableWidth = Math.Max(1, destination.Width - x * 2);
        var availableHeight = Math.Max(1, destination.Height - y * 2);
        var padding = Math.Min(label.Style.Padding, Math.Min(availableWidth, availableHeight) / 4);
        var fontSize = Math.Clamp(label.Style.FontSize, 1, Math.Max(1, availableHeight - padding * 2));
        var text = new FormattedText(Path.GetFileName(track.Media.Path), CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI, Malgun Gothic"), fontSize,
            Brush(label.Style.ForegroundArgb), 1)
        {
            MaxTextWidth = Math.Max(1, availableWidth - padding * 2),
            MaxTextHeight = Math.Max(1, availableHeight - padding * 2),
            MaxLineCount = Math.Clamp(label.Style.MaximumLines, 1, 2),
            LineHeight = fontSize * 1.35,
            Trimming = TextTrimming.CharacterEllipsis
        };
        var width = Math.Clamp((int)Math.Ceiling(text.WidthIncludingTrailingWhitespace) + padding * 2, 1, availableWidth);
        var height = Math.Clamp((int)Math.Ceiling(text.Height) + padding * 2, 1, availableHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
            dc.DrawRectangle(Brush(label.Style.BackgroundArgb), null, new Rect(0, 0, width, height));
            dc.DrawText(text, new Point(padding, padding));
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(directory, $"label-{index}.png");
        using (var stream = File.Create(path)) { encoder.Save(stream); }
        if (label.Position.Anchor is LabelAnchor.TopRight or LabelAnchor.BottomRight) { x = destination.Width - x - width; }
        if (label.Position.Anchor is LabelAnchor.BottomLeft or LabelAnchor.BottomRight) { y = destination.Height - y - height; }
        return new(index, path, x, y);
    }

    private static SolidColorBrush Brush(uint argb) => new(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));

    public void Dispose()
    {
        // Only this export's explicitly created files; never recursively delete a computed parent.
        foreach (var asset in Assets) { if (File.Exists(asset.Path)) { File.Delete(asset.Path); } }
        if (_directory is not null && Directory.Exists(_directory))
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "label-*.png")) { File.Delete(path); }
            Directory.Delete(_directory, recursive: false);
        }
    }
}
