using System.IO;
using System.Text.Json;
using System.Windows;
using DiffVideo.Core;

namespace DiffVideo.App;

internal static partial class PreviewDiagnostics
{
    private static async Task CheckPlacementAsync(MainWindow window, string[] files, string report)
    {
        var vm = window.ViewModel!;
        await vm.LoadFilesAsync(files);
        await vm.RefreshStillPreviewAsync();
        window.UpdateLayout();
        var checks = new List<string>();
        foreach (var (track, border) in new[] { (vm.Video1, window.Video1Overlay), (vm.Video2, window.Video2Overlay) })
        {
            var original = track.ToModel().Destination;
            var fingerprint = PreviewFingerprint(vm.PreviewImage!);
            Assert(window.BeginOverlayDrag(border, new Point(0, 0), RoiEdges.None), "Stopped placement begins");
            window.UpdateOverlayDrag(-original.X - 120, -original.Y - 60);
            window.UpdateLayout();
            Assert(track.DestinationX == -120 && track.DestinationY == -60, "Negative coordinates preserved");
            Assert(PreviewFingerprint(vm.PreviewImage!) != fingerprint, "Actual video moves immediately");
            SaveScreenshot(window, Path.ChangeExtension(report, $"{track.Name}.png"));
            window.CancelOverlayDrag();
            Assert(track.ToModel().Destination == original, "Cancel restores entire destination");
            track.AspectRatioLocked = false;
            Assert(window.BeginOverlayDrag(border, new Point(0, 0), RoiEdges.Left | RoiEdges.Top), "Resize begins");
            window.UpdateOverlayDrag(80, 40);
            Assert(track.ToModel().Destination == new PixelRect(original.X + 80, original.Y + 40, original.Width - 80, original.Height - 40), "Free corner resize anchors opposite corner");
            border.ReleaseMouseCapture();
            Assert(track.ToModel().Destination == original, "Capture loss restores rectangle");
            track.AspectRatioLocked = true;
            Assert(window.BeginOverlayDrag(border, new Point(0, 0), RoiEdges.Right), "Locked resize begins");
            window.UpdateOverlayDrag(-80, 0);
            Assert(Math.Abs(track.DestinationWidth / (double)track.DestinationHeight - track.RoiWidth / (double)track.RoiHeight) < 0.02, "Locked aspect follows ROI");
            window.CancelOverlayDrag();
            checks.Add(track.Name + ": immediate move, negative coordinates, free/locked resize, cancel and capture loss");
        }
        await vm.StartPlaybackAsync();
        Assert(!window.BeginOverlayDrag(window.Video1Overlay, new Point(), RoiEdges.None), "Playing placement rejected");
        await vm.PausePlaybackAsync();
        Assert(window.BeginOverlayDrag(window.Video1Overlay, new Point(), RoiEdges.None), "Paused placement allowed");
        window.CancelOverlayDrag();
        checks.Add("Playback blocks editing; pause enables editing");
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = true, Checks = checks }, JsonOptions));
    }
}
