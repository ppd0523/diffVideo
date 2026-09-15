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
        vm.Video2.SetDestination(vm.Video1.ToModel().Destination);
        await vm.RefreshStillPreviewAsync();
        vm.UpdatePlacementPreview();
        var group = (DrawingGroup)((DrawingImage)vm.PreviewImage!).Drawing;
        Assert(group.Children.Count == 5, "Preview layout is ready: " + vm.ErrorMessage);
        Drawing SourceAt(int index) => ((DrawingGroup)((DrawingGroup)group.Children[index]).Children[0]).Children[0];
        var sourceA = SourceAt(2);
        var sourceB = SourceAt(4);
        var selected = vm.SelectedVideo;
        window.FrontVideoCombo.SelectedValue = 1;
        Assert(vm.Video1.ZIndex == 1 && vm.Video2.ZIndex == 0, "A selector updates model");
        Assert(Panel.GetZIndex(window.Video1Overlay) > Panel.GetZIndex(window.Video2Overlay), "A hit target is on top");
        Assert(ReferenceEquals(sourceA, SourceAt(4)), "Actual preview draws A last");
        var snapshot = vm.BuildExportComposition();
        var command = FfmpegCommandBuilder.BuildExport(snapshot, "layers.mp4", H264Encoder.MediaFoundation);
        Assert(command.Arguments.Any(value => value.Contains("[base][v1]overlay=") && value.Contains("[layer0][v0]overlay=")), "Export draws B then A");
        var confirmation = new ExportConfirmationWindow(snapshot);
        Assert(confirmation.SummaryText.Text.Contains("위에 표시  A · 영상 1"), "Confirmation names front video");
        confirmation.Close();
        window.UpdateLayout();
        SaveScreenshot(window, Path.ChangeExtension(report, "png"));
        window.FrontVideoCombo.SelectedValue = 2;
        Assert(vm.Video2.ZIndex == 1 && vm.Video1.ZIndex == 0 && ReferenceEquals(sourceB, SourceAt(4)), "B selector reverses actual preview order");
        Assert(ReferenceEquals(vm.SelectedVideo, selected), "Changing layer does not change editing selection");
        Assert(snapshot.Video1.ZIndex == 1, "Confirmed export snapshot keeps its layer order");
        vm.SelectVideo(vm.Video1);
        vm.MoveSelectedLayer(true);
        Assert(Equals(window.FrontVideoCombo.SelectedValue, 1), "Existing forward action stays synchronized");
        vm.MoveSelectedLayer(false);
        Assert(Equals(window.FrontVideoCombo.SelectedValue, 2), "Existing backward action stays synchronized");
        await vm.StartPlaybackAsync();
        Assert(!window.FrontVideoCombo.IsEnabled, "Playing disables layer control");
        await vm.PausePlaybackAsync();
        Assert(window.FrontVideoCombo.IsEnabled, "Pause enables layer control");
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = true, Checks = new[]
        {
            "A/B selection, model and hit-target order", "Actual preview drawing order and FFmpeg export order",
            "Confirmation and immutable snapshot", "Legacy layer actions synchronized", "Playback editing gate"
        } }, JsonOptions));
    }
}
