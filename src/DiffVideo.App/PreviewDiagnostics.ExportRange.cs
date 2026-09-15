using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        vm.PlayheadSeconds = 5;
        window.SetExportStartButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        vm.PlayheadSeconds = 12;
        window.SetExportEndButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert(vm.ExportStartSeconds == 5 && vm.ExportEndSeconds == 12 && vm.ExportDurationSeconds == 7, "Current position buttons select 5 to 12");
        var snapshot = vm.BuildExportComposition();
        Assert(snapshot.Output.ExportStart.TotalSeconds == 5 && snapshot.Output.ExportDuration.TotalSeconds == 7, "Export snapshot freezes selected range");
        Assert(vm.BuildComposition().Output.ExportDuration.TotalSeconds == whole, "Playback composition remains whole");
        var confirmation = new ExportConfirmationWindow(snapshot);
        Assert(confirmation.SummaryText.Text.Contains("5초 → 12초") && confirmation.SummaryText.Text.Contains("7초"), "Confirmation shows boundaries and saved length");
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
        Assert(window.ExportRangeToolbar.ActualHeight > 0 && window.TimelineViewportHost.ActualHeight >= 100, "Range controls and tracks fit minimum window");
        SaveScreenshot(window, Path.ChangeExtension(report, "minimum.png"));
        vm.OutputDurationSeconds = 4;
        Assert(vm.ExportStartSeconds < vm.ExportEndSeconds && vm.ExportEndSeconds <= 4, "Shorter timeline clamps custom range");
        window.ResetExportRangeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert(vm.ExportStartSeconds == 0 && vm.ExportEndSeconds == 4, "Whole range reset");
        Assert(snapshot.Output.ExportStart.TotalSeconds == 5 && snapshot.Output.EffectiveExportEnd.TotalSeconds == 12, "Captured export is unaffected by later edits");
        await vm.StartPlaybackAsync();
        Assert(!window.BeginExportRangeDrag(window.ExportStartHandle), "Playing blocks range edit");
        await vm.PausePlaybackAsync();
        Assert(window.BeginExportRangeDrag(window.ExportStartHandle), "Pause allows range edit");
        window.CancelExportRangeDrag();
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = true, Checks = new[]
        {
            "Whole-range default and automatic end tracking", "Current-position buttons and immutable export snapshot",
            "Confirmation boundaries and saved duration", "Boundary handles with zoom/scroll, commit, cancel and crossing clamp",
            "Minimum window layout", "Duration shrink and reset", "Playing/paused editing gates"
        } }, JsonOptions));
    }
}
