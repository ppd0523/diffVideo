using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DiffVideo.Core;

namespace DiffVideo.App;

internal static partial class PreviewDiagnostics
{
    private static async Task CheckExportRangeAsync(MainWindow window, string[] files, string report)
    {
        var vm = window.ViewModel!;
        await vm.LoadFilesAsync(files);
        var whole = vm.OutputDurationSeconds;
        Assert(vm.ExportStartSeconds == 0 && vm.ExportEndSeconds == whole, "Default covers whole timeline");
        vm.OutputDurationSeconds = whole + 1;
        Assert(vm.ExportEndSeconds == whole + 1, "Default end follows timeline duration");
        vm.OutputDurationSeconds = whole;
        window.ExportStartLabel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        Assert(window.ExportStartInput.Width == 100 && window.ExportStartEditor.Margin.Left == -4 && window.ExportEndHost.Visibility == Visibility.Collapsed,
            "Boundary editor expands left without shifting its number and hides the opposite label");
        window.ExportStartInput.Text = "5";
        window.ApplyExportStartButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
        window.ApplyExportStartButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        window.PlayButton.Focus();
        window.ExportEndLabel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        window.ExportEndInput.Text = "12";
        window.ExportEndInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.ExportEndInput), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert(vm.ExportStartSeconds == 5 && vm.ExportEndSeconds == 12 && vm.ExportDurationSeconds == 7,
            $"Inline editors select 5 to 12: {vm.ExportStartSeconds} to {vm.ExportEndSeconds}");
        var snapshot = vm.BuildExportComposition();
        Assert(snapshot.Output.ExportStart.TotalSeconds == 5 && snapshot.Output.ExportDuration.TotalSeconds == 7, "Export snapshot freezes selected range");
        Assert(vm.BuildComposition().Output.ExportDuration.TotalSeconds == whole, "Playback composition remains whole");
        var confirmation = new ExportConfirmationWindow(snapshot);
        var confirmationText = new System.Windows.Documents.TextRange(confirmation.SummaryText.ContentStart, confirmation.SummaryText.ContentEnd).Text;
        Assert(confirmationText.Contains("5초 → 12초") && confirmationText.Contains("7초"), "Confirmation shows boundaries and saved length");
        Assert(confirmation.ConfirmedComposition.Output == snapshot.Output, "Confirmation preserves range");
        confirmation.Close();
        window.UpdateLayout(); window.UpdateTimeline();
        Assert(Math.Abs(Canvas.GetLeft(window.ExportStartHandle) - window.TimelineView.Position(5)) < 0.01, "Start marker follows timeline coordinates");
        window.ZoomTimeline(4);
        window.ScrollTimeline(40, manual: true);
        Assert(Math.Abs(Canvas.GetLeft(window.ExportEndHandle) + window.ExportEndHandle.Width - window.TimelineView.Position(12)) < 0.01, "End marker follows zoom and scroll");
        Assert(window.BeginExportRangeDrag(window.ExportStartHandle), "Start handle drag begins");
        window.UpdateExportRangeDrag(6);
        window.ExportStartHandle.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert(vm.ExportStartSeconds == 6, "Completed drag persists start");
        Assert(window.BeginExportRangeDrag(window.ExportEndHandle), "End handle drag begins");
        window.UpdateExportRangeDrag(4);
        Assert(Math.Abs(vm.ExportDurationSeconds - 1d / vm.OutputFps) < 1e-8, "Crossing boundary clamps to one frame");
        window.CancelExportRangeDrag();
        Assert(vm.ExportEndSeconds == 12, "Cancelled drag restores end");
        window.TimelineView.Fit(); window.UpdateTimeline();
        await vm.RefreshStillPreviewAsync();
        SaveScreenshot(window, Path.ChangeExtension(report, "png"));
        window.Width = window.MinWidth; window.Height = window.MinHeight;
        window.UpdateLayout(); window.UpdateTimeline();
        Assert(window.TransportBar.ActualHeight > 0 && window.TimelineViewportHost.ActualHeight >= 100, "Range controls and tracks fit minimum window");
        SaveScreenshot(window, Path.ChangeExtension(report, "minimum.png"));
        vm.Video1.StartSeconds = 1;
        vm.Video2.StartSeconds = 3;
        var expectedOverlapEnd = Math.Min(vm.OutputDurationSeconds, Math.Min(
            vm.Video1.StartSeconds + vm.Video1.DurationSeconds,
            vm.Video2.StartSeconds + vm.Video2.DurationSeconds));
        vm.SetOverlapExportRange();
        Assert(vm.ExportStartSeconds == 3 && vm.ExportEndSeconds == expectedOverlapEnd, "Overlap preset selects the video intersection");
        vm.OutputDurationSeconds = 200;
        vm.Video1.StartSeconds = 1;
        vm.Video2.StartSeconds = vm.Video1.StartSeconds + vm.Video1.DurationSeconds + 1;
        vm.SetExportBoundary(true, 10);
        vm.SetExportBoundary(false, 20);
        var unchangedStart = vm.ExportStartSeconds;
        var unchangedEnd = vm.ExportEndSeconds;
        vm.SetOverlapExportRange();
        Assert(vm.ExportStartSeconds == unchangedStart && vm.ExportEndSeconds == unchangedEnd, "Disjoint videos preserve current range");
        vm.Video1.StartSeconds = 1;
        vm.Video2.StartSeconds = 3;
        vm.OutputDurationSeconds = 4;
        Assert(vm.ExportStartSeconds < vm.ExportEndSeconds && vm.ExportEndSeconds <= 4, "Shorter timeline clamps custom range");
        var expectedVideoStart = Math.Min(vm.Video1.StartSeconds, vm.Video2.StartSeconds);
        var expectedVideoEnd = Math.Max(vm.Video1.StartSeconds + vm.Video1.DurationSeconds, vm.Video2.StartSeconds + vm.Video2.DurationSeconds);
        vm.SetVideoExportRange();
        Assert(vm.OutputDurationSeconds >= expectedVideoEnd && vm.ExportStartSeconds == expectedVideoStart && vm.ExportEndSeconds == vm.OutputDurationSeconds, "Whole video range extends the output duration");
        Assert(snapshot.Output.ExportStart.TotalSeconds == 5 && snapshot.Output.EffectiveExportEnd.TotalSeconds == 12, "Captured export is unaffected by later edits");
        await vm.StartPlaybackAsync();
        Assert(!window.BeginExportRangeDrag(window.ExportStartHandle), "Playing blocks range edit");
        await vm.PausePlaybackAsync();
        Assert(window.BeginExportRangeDrag(window.ExportStartHandle), "Pause allows range edit");
        window.CancelExportRangeDrag();
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = true, Checks = new[]
        {
            "Whole-range default and automatic end tracking", "Inline boundaries and immutable export snapshot",
            "Confirmation boundaries and saved duration", "Boundary handles with zoom/scroll, commit, cancel and crossing clamp",
            "Minimum window layout", "Overlap intersection, disjoint preservation and whole-video duration extension", "Playing/paused editing gates"
        } }, JsonOptions));
    }
}
