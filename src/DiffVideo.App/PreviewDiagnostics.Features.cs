using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using DiffVideo.App.Services;
using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.App;

internal static partial class PreviewDiagnostics
{
    private static async Task CheckFeaturesAsync(MainWindow window, string[] files, string report)
    {
        var checks = new List<string>();
        var vm = window.ViewModel!;
        await vm.LoadFilesAsync(files);
        vm.OutputDurationSeconds = 100;
        vm.PlayheadSeconds = 30;
        await vm.RefreshStillPreviewAsync();
        window.UpdateLayout(); window.UpdateTimeline();
        var segmentEnd = window.ExportEndLabel.TransformToAncestor(window.TransportBar).TransformBounds(new(window.ExportEndLabel.RenderSize));
        var presets = window.SegmentPresetGroup.TransformToAncestor(window.TransportBar).TransformBounds(new(window.SegmentPresetGroup.RenderSize));
        Assert(segmentEnd.Right <= presets.Left + 0.01, "Segment end label clears preset controls");
        var fullControls = window.FullControlsGroup.TransformToAncestor(window.IntegratedTimeline).TransformBounds(new(window.FullControlsGroup.RenderSize));
        var segmentControls = window.SegmentControlsGroup.TransformToAncestor(window.IntegratedTimeline).TransformBounds(new(window.SegmentControlsGroup.RenderSize));
        Assert(Math.Abs(fullControls.Left - segmentControls.Left) < 0.01 && Math.Abs(fullControls.Right - segmentControls.Right) < 0.01,
            "Full and segment controls align");
        var before = vm.BuildComposition();
        var anchor = window.TimelineView.Position(30);
        window.ZoomInButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        window.UpdateLayout();
        Assert(Math.Abs(window.TimelineView.Position(30) - anchor) < 1, "Zoom anchors current playhead");
        Assert(vm.BuildComposition() == before && vm.PlayheadSeconds == 30, "Zoom never changes composition/time");
        Assert(window.Video1FileName.Text == Path.GetFileName(files[0]) && Equals(window.Video1FileName.ToolTip, window.Video1FileName.Text), "Fixed filename with full-name tooltip");
        Assert(window.TimelineVideo1.ActualWidth > window.TimelineViewportHost.ActualWidth, "Clips use full zoomed timeline width");
        Assert(window.PlayheadBar.ActualHeight > window.TimelineVideo1.ActualHeight * 3, "Playhead spans all tracks");
        checks.Add("Zoom button / playhead anchor / fixed filenames / full-height playhead");
        var scale = window.TimelineView.PixelsPerSecond;
        window.ResizeTimelineLabels(5000); window.UpdateLayout();
        Assert(window.TimelineLabelColumn.ActualWidth <= window.TimelineBody.ActualWidth * 0.4 + 1, "Label max 40 percent");
        Assert(window.TimelineView.PixelsPerSecond == scale, "Resize preserves zoom scale");
        window.ResizeTimelineLabels(1); window.UpdateLayout();
        Assert(window.TimelineLabelColumn.ActualWidth == 180, "Label minimum width");
        window.ResizeTimelineHeight(5000); window.UpdateLayout();
        Assert(window.TimelineRow.ActualHeight <= window.ActualHeight / 2 + 1, $"Height maximum: {window.TimelineRow.ActualHeight}/{window.ActualHeight}");
        Assert(Math.Abs(window.TimelineVideo1.ActualHeight - window.TimelineAudio.ActualHeight) < 1, "Rows share added height equally");
        SaveScreenshot(window, Path.ChangeExtension(report, ".expanded.png"));
        window.ResizeTimelineHeight(1); window.ResizeTimelineLabels(204); window.UpdateLayout();
        Assert(Math.Abs(window.TimelineRow.ActualHeight - window.TimelineRow.MinHeight) < 1, "Height minimum");
        window.ScrollTimeline(200, true);
        Assert(vm.PlayheadSeconds == 30 && !window.TimelineAutoFollow, "Manual scrolling does not seek and disables follow");
        await vm.StartPlaybackAsync();
        Assert(window.TimelineAutoFollow, "Play restores follow");
        window.ScrollTimeline(0, true);
        Assert(!window.TimelineAutoFollow, "Manual scrolling suspends follow during playback");
        await vm.PausePlaybackAsync();
        window.FitTimelineButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); window.UpdateLayout();
        Assert(window.TimelineView.IsFit && window.TimelineView.Offset == 0, "Fit button restores full view");
        checks.Add("Panel resize bounds / equal rows / zoom preservation / manual scroll and follow");

        var png = await vm.GetSourceFramePngAsync(vm.Video1);
        var media = vm.Video1.Media!;
        var editor = new RoiEditorWindow(png, media.DisplayWidth, media.DisplayHeight, PixelRect.FullFrame(media)) { Owner = window };
        editor.Show(); editor.UpdateLayout();
        var center = new Point(editor.ImageHost.ActualWidth / 2, editor.ImageHost.ActualHeight / 2);
        editor.UpdateCoordinateTip(center);
        Assert(editor.SourceCoordinateTip.Coordinates == $"({media.DisplayWidth / 2},{media.DisplayHeight / 2})", "Original-pixel coordinate tooltip");
        Assert(editor.SourceXRuler.SourcePixels && editor.SourceYRuler.Vertical && editor.SourceXRuler.Extent == media.DisplayWidth, "Source rulers use real pixel extent");
        SaveScreenshot(editor, Path.ChangeExtension(report, ".rulers.png"));
        editor.UpdateCoordinateTip(new Point(-1, -1));
        Assert(editor.SourceCoordinateTip.Coordinates is null, "Tooltip hidden outside image");
        editor.Close();
        checks.Add("100 px source rulers / source coordinate tooltip / outside-image hiding");

        var snapshot = vm.BuildComposition();
        snapshot = snapshot with { Video1 = snapshot.Video1 with { FileNameLabel = new(false, new(18, 24, LabelAnchor.BottomRight), new(20)) } };
        var outputDirectory = Path.GetDirectoryName(report)!;
        var confirmation = new ExportConfirmationWindow(snapshot) { Owner = window };
        confirmation.Show(); confirmation.UpdateLayout();
        Assert(!confirmation.ConfirmedComposition.Video1.FileNameLabel!.Enabled, "Filename checkbox defaults off");
        vm.Video1.StartSeconds = 5;
        Assert(confirmation.ConfirmedComposition.Video1.Start == snapshot.Video1.Start, "Confirmation uses immutable captured snapshot");
        confirmation.IncludeFileNamesCheckBox.IsChecked = true;
        Assert(confirmation.ConfirmedComposition.Video1.FileNameLabel!.Enabled && confirmation.ConfirmedComposition.Video2.FileNameLabel!.Enabled, "Checkbox enables both video labels");
        Assert(confirmation.ConfirmedComposition.Video1.FileNameLabel!.Position == snapshot.Video1.FileNameLabel!.Position && confirmation.ConfirmedComposition.Video1.FileNameLabel.Style == snapshot.Video1.FileNameLabel.Style, "Checkbox preserves future per-video label position and style");
        SaveScreenshot(confirmation, Path.ChangeExtension(report, ".confirmation.png"));
        confirmation.Close();
        Assert(!File.Exists(Path.Combine(outputDirectory, "not-confirmed.mp4")), "Unconfirmed dialog creates no output");
        checks.Add("Confirmation defaults / immutable snapshot / checkbox / cancel without output");
        await CheckLabelExportAsync(window, outputDirectory, checks);
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = true, Checks = checks }, JsonOptions));
    }

    private static async Task CheckLabelExportAsync(MainWindow window, string outputDirectory, List<string> checks)
    {
        var paths = FfmpegPaths.Discover();
        var directory = Directory.CreateTempSubdirectory("DiffVideo-label-test-").FullName;
        try
        {
            var source1 = Path.Combine(directory, "첫 번째 영상 - 한글 공백 ' [괄호] % 아주 긴 파일 이름을 두 줄까지 표시하고 그 이후에는 말줄임으로 표시합니다.mp4");
            var source2 = Path.Combine(directory, "두 번째 영상.mp4");
            await DiagnosticFfmpegAsync(paths.Ffmpeg, ["-f", "lavfi", "-i", "color=c=red:s=320x180:r=30:d=1", "-c:v", "mpeg4", source1]);
            await DiagnosticFfmpegAsync(paths.Ffmpeg, ["-f", "lavfi", "-i", "color=c=blue:s=320x180:r=30:d=1", "-c:v", "mpeg4", source2]);
            var probe = new MediaProbeService(paths);
            var one = await probe.ProbeAsync(source1); var two = await probe.ProbeAsync(source2);
            var snapshot = new Composition(
                new(one, TimeSpan.FromSeconds(0.5), PixelRect.FullFrame(one), new(0, 0, 320, 180), VideoFitMode.Fit, false, 0, false, 1),
                new(two, TimeSpan.FromSeconds(1), PixelRect.FullFrame(two), new(320, 0, 320, 180), VideoFitMode.Fit, false, 1, false, 1),
                null, new(640, 180, 30, TimeSpan.FromSeconds(3), OutputQuality.High));
            var labeled = snapshot with
            {
                Video1 = snapshot.Video1 with { FileNameLabel = FileNameLabel.Default(true) },
                Video2 = snapshot.Video2 with { FileNameLabel = FileNameLabel.Default(true) }
            };
            var plainPath = Path.Combine(outputDirectory, "labels-off.mp4");
            var labeledPath = Path.Combine(outputDirectory, "labels-on.mp4");
            var service = new FfmpegExportService(paths);
            await service.ExportAsync(snapshot, plainPath, overwrite: true);
            List<string> labelPaths;
            using (var renderer = ExportLabelRenderer.Create(labeled))
            {
                labelPaths = renderer.Assets.Select(a => a.Path).ToList();
                Assert(renderer.Assets.Count == 2, "Only video filename labels are rendered");
                foreach (var asset in renderer.Assets)
                {
                    using var input = File.OpenRead(asset.Path);
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
                    Assert(bitmap.PixelWidth <= 296 && bitmap.PixelHeight <= 72, $"Label bounded to destination and at most two lines: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
                }
                await service.ExportAsync(labeled, labeledPath, overwrite: true, labels: renderer.Assets);
            }
            Assert(labelPaths.All(path => !File.Exists(path)), "Per-export labels cleaned up");
            var output = await probe.ProbeAsync(labeledPath);
            Assert(output.Codec == "h264" && output.AudioCodec == "aac" && Math.Abs(output.Duration.TotalSeconds - 3) < 0.05, "Labeled MP4 retains duration and codecs");
            foreach (var time in new[] { "0.1", "2.8" })
            {
                var bytes = await DiagnosticFfmpegAsync(paths.Ffmpeg, ["-ss", time, "-i", labeledPath, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1"]);
                for (var video = 0; video < 2; video++)
                {
                    var white = 0;
                    for (var y = 12; y < 82; y++) for (var x = video * 320 + 12; x < video * 320 + 308; x++)
                    {
                        var p = (y * 640 + x) * 3;
                        if (bytes[p] > 180 && bytes[p + 1] > 180 && bytes[p + 2] > 180) { white++; }
                    }
                    Assert(white > 80, "Both filenames appear during first/last frame holds");
                }
            }
            await DiagnosticFfmpegAsync(paths.Ffmpeg, ["-y", "-ss", "0.1", "-i", labeledPath, "-frames:v", "1", Path.Combine(outputDirectory, "labels-on.png")]);
            checks.Add("Actual Unicode/long-filename H.264 export / 2 lines / both held endpoints / temp cleanup");
            var overlapped = labeled with { Video2 = labeled.Video2 with { Destination = labeled.Video1.Destination }, Output = labeled.Output with { Duration = TimeSpan.FromSeconds(0.5) } };
            var overlapPath = Path.Combine(outputDirectory, "labels-overlap.mp4");
            using (var labels = ExportLabelRenderer.Create(overlapped))
            {
                await service.ExportAsync(overlapped, overlapPath, overwrite: true, labels: labels.Assets);
            }
            var front = await DiagnosticFfmpegAsync(paths.Ffmpeg, ["-ss", "0.1", "-i", labeledPath, "-vf", "crop=320:180:320:0", "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1"]);
            var overlap = await DiagnosticFfmpegAsync(paths.Ffmpeg, ["-ss", "0.1", "-i", overlapPath, "-vf", "crop=320:180:0:0", "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", "pipe:1"]);
            Assert(front.Length == overlap.Length && front.Zip(overlap, (a, b) => Math.Abs(a - b)).Average() < 4, "Front video occludes rear label as well as rear pixels");
            var sentinel = Path.Combine(outputDirectory, "cancelled-labels.mp4");
            await File.WriteAllTextAsync(sentinel, "previous output must survive cancellation");
            var sentinelPart = sentinel + ".part";
            await File.WriteAllTextAsync(sentinelPart, "unrelated existing partial file");
            using (var labels = ExportLabelRenderer.Create(labeled))
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try { await service.ExportAsync(labeled, sentinel, cancellationToken: cancellation.Token, overwrite: true, labels: labels.Assets); Assert(false, "Cancelled export should throw"); }
                catch (OperationCanceledException) { }
            }
            Assert(await File.ReadAllTextAsync(sentinel) == "previous output must survive cancellation" && await File.ReadAllTextAsync(sentinelPart) == "unrelated existing partial file", "Cancellation preserves existing output and unrelated .part file");
            File.Delete(sentinel); File.Delete(sentinelPart);
            Assert(!Directory.EnumerateFiles(outputDirectory, "cancelled-labels.mp4.*.part").Any(), "Unique cancelled partial output cleaned");
            checks.Add("Actual overlapping layer order / cancellation preserves previous output and unrelated partial files");
            // Exercise the same immutable-snapshot overload used by the confirmation button.
            var task = window.ViewModel!.ExportAsync(labeled, Path.Combine(outputDirectory, "labels-snapshot.mp4"), overwrite: true);
            window.ViewModel.Video1.StartSeconds = 7;
            Assert(window.ViewModel.IsEditingEnabled, "Confirmed export does not lock editing");
            await task;
            Assert(string.IsNullOrEmpty(window.ViewModel.ErrorMessage), "Snapshot export succeeds");
            checks.Add("Confirmed snapshot asynchronous export keeps editing enabled");
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(directory)) { File.Delete(path); }
            Directory.Delete(directory, recursive: false);
        }
    }

    private static async Task<byte[]> DiagnosticFfmpegAsync(string executable, string[] arguments)
    {
        using var process = new Process { StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) { process.StartInfo.ArgumentList.Add(argument); }
        process.Start();
        var errors = process.StandardError.ReadToEndAsync();
        using var stream = new MemoryStream();
        var read = process.StandardOutput.BaseStream.CopyToAsync(stream);
        await Task.WhenAll(process.WaitForExitAsync(), read);
        Assert(process.ExitCode == 0, await errors);
        return stream.ToArray();
    }
}
