using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using DiffVideo.Infrastructure;

namespace DiffVideo.App;

internal static partial class PreviewDiagnostics
{
    private static async Task CheckLayersAsync(MainWindow window, string[] files, string report)
    {
        var vm = window.ViewModel!;
        await vm.LoadFilesAsync(files);
        vm.Videos[1].SetDestination(vm.Videos[0].ToModel().Destination);
        await vm.RefreshStillPreviewAsync();
        vm.UpdatePlacementPreview();
        var group = (DrawingGroup)((DrawingImage)vm.PreviewImage!).Drawing;
        Assert(group.Children.Count == 5, "Preview layout is ready: " + vm.ErrorMessage);

        // Row order is layer order: the first row carries the highest ZIndex and is drawn last.
        var first = vm.Videos[0];
        var second = vm.Videos[1];
        Assert(first.ZIndex > second.ZIndex, "The first row is the front layer");
        Assert(Panel.GetZIndex(window.TrackOverlayContainer(0)!) > Panel.GetZIndex(window.TrackOverlayContainer(1)!), "A hit target is on top");
        var imageBefore = PreviewFingerprint(vm.PreviewImage!);

        var snapshot = vm.BuildExportComposition();
        var command = FfmpegCommandBuilder.BuildExport(snapshot, "layers.mp4", H264Encoder.MediaFoundation);
        Assert(command.Arguments.Any(value => value.Contains("[base][v1]overlay=") && value.Contains("[layer0][v0]overlay=")),
            "Export overlays the back row first and the front row last");
        var confirmation = new ExportConfirmationWindow(snapshot);
        var summary = new System.Windows.Documents.TextRange(confirmation.SummaryText.ContentStart, confirmation.SummaryText.ContentEnd).Text;
        Assert(summary.Contains($"1번째  {Path.GetFileName(first.Media!.Path)}"), "Confirmation lists rows front to back");
        confirmation.Close();
        window.UpdateLayout();
        SaveScreenshot(window, Path.ChangeExtension(report, "png"));

        // Sending the front row backward moves the row itself, not a separate layer number.
        vm.SelectVideo(first);
        var selected = vm.SelectedVideo;
        window.SendBackwardButton.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        Assert(ReferenceEquals(vm.Videos[1], first) && ReferenceEquals(vm.Videos[0], second), "Send-backward moves the row down");
        Assert(second.ZIndex > first.ZIndex, "Row order drives layer order");
        await vm.RefreshStillPreviewAsync();
        Assert(PreviewFingerprint(vm.PreviewImage!) != imageBefore, "Actual preview now shows the other video in front");
        Assert(ReferenceEquals(vm.SelectedVideo, selected), "Changing layer does not change editing selection");
        Assert(snapshot.Videos[0].ZIndex == 1, "Confirmed export snapshot keeps its layer order");

        window.BringForwardButton.RaiseEvent(new System.Windows.RoutedEventArgs(Button.ClickEvent));
        Assert(ReferenceEquals(vm.Videos[0], first) && first.ZIndex > second.ZIndex, "Bring-forward restores the original row order");

        await vm.StartPlaybackAsync();
        Assert(!window.BringForwardButton.IsEnabled && !window.SendBackwardButton.IsEnabled, "Playing disables layer control");
        await vm.PausePlaybackAsync();
        Assert(window.BringForwardButton.IsEnabled && window.SendBackwardButton.IsEnabled, "Pause enables layer control");
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = true, Checks = new[]
        {
            "Row order is layer order, model and hit-target order",
            "Actual preview drawing order and FFmpeg export order",
            "Confirmation and immutable snapshot", "Layer actions synchronized", "Playback editing gate"
        } }, JsonOptions));
    }
}
