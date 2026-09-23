using System.IO;
using System.Text.Json;
using System.Windows;
using DiffVideo.App.Services;
using DiffVideo.Core;

namespace DiffVideo.App;

internal static partial class PreviewDiagnostics
{
    private static async Task CheckSettingsAsync(MainWindow window, UserSettingsStore store, string report)
    {
        var vm = window.ViewModel!;
        window.UpdateLayout();
        Assert(Math.Abs(window.Width - 1280) < 1 && Math.Abs(window.Height - 780) < 1, "Window size restored");
        Assert(window.WindowStartupLocation == WindowStartupLocation.Manual && double.IsFinite(window.Left) && double.IsFinite(window.Top), "Window position restored and visible");
        Assert(Math.Abs(window.InspectorColumn.ActualWidth - 300) < 1, "Inspector width restored");
        Assert(vm.CanvasWidth == 1280 && vm.CanvasHeight == 720 && vm.OutputFps == 25 && vm.OutputDurationSeconds == 19 && vm.Quality == OutputQuality.High, "Output settings restored");
        Assert(Math.Abs(window.TimelineRow.ActualHeight - 300) < 1 && Math.Abs(window.TimelineView.ZoomRatio - 4) < 0.001, "Timeline height and zoom restored");

        vm.CanvasWidth = 1920;
        vm.CanvasHeight = 1080;
        vm.OutputFps = 30;
        vm.OutputDurationSeconds = 42;
        vm.Quality = OutputQuality.Small;
        window.ResizeTimelineHeight(320);
        window.ResizeInspectorWidth(1000);
        window.UpdateLayout();
        Assert(Math.Abs(window.InspectorColumn.ActualWidth - window.ActualWidth * 0.3) < 1, "Inspector width stops at 30 percent");
        window.Width = 1120;
        window.UpdateLayout();
        Assert(window.InspectorColumn.ActualWidth <= window.ActualWidth * 0.3 + 1, "Inspector width follows window resize");
        Assert(((FrameworkElement)window.OutputDurationLabel.Parent).ActualWidth >= 134 &&
            window.ExportStartHost.ActualWidth < 134 && window.ExportEndHost.ActualWidth < 134,
            "Segment time labels use their text width until editing");
        var rangeRight = window.SegmentInfoGroup.TranslatePoint(new Point(window.SegmentInfoGroup.ActualWidth, 0), window.TransportBar).X;
        var presetLeft = window.SegmentPresetGroup.TranslatePoint(new Point(), window.TransportBar).X;
        var presetRight = window.SegmentPresetGroup.TranslatePoint(new Point(window.SegmentPresetGroup.ActualWidth, 0), window.TransportBar).X;
        var controlsLeft = window.SegmentControlsGroup.TranslatePoint(new Point(), window.TransportBar).X;
        Assert(rangeRight <= presetLeft && presetRight <= controlsLeft, "Timeline range, presets, and playback controls do not overlap at minimum window width");
        window.Width = 1280;
        window.ResizeInspectorWidth(100);
        window.UpdateLayout();
        Assert(Math.Abs(window.InspectorColumn.ActualWidth - 216) < 1, "Inspector width keeps its 216-pixel minimum");
        window.ResizeInspectorWidth(320);
        window.UpdateLayout();
        window.TimelineView.RestoreZoomRatio(2);
        window.SaveUserSettings();
        var saved = store.Load();
        Assert(saved.SchemaVersion == UserSettings.CurrentSchemaVersion && saved.Output == new OutputUserSettings
        {
            Width = 1920, Height = 1080, FramesPerSecond = 30, DurationSeconds = 42, Quality = OutputQuality.Small
        }, "Output settings round trip");
        Assert(Math.Abs(saved.Timeline.Height - 320) < 1 && Math.Abs(saved.Timeline.ZoomRatio - 2) < 0.001, "Timeline settings round trip");
        Assert(Math.Abs(saved.Window.InspectorWidth - 320) < 1, "Inspector width round trip");
        Assert(saved.LastExportDirectory == Path.GetDirectoryName(store.SettingsPath), "Last export directory round trip");
        Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(store.SettingsPath)!, "*.tmp").Any(), "Atomic save leaves no temporary file");

        await File.WriteAllTextAsync(store.SettingsPath, "{ damaged json");
        var fallback = store.Load();
        Assert(fallback == new UserSettings(), "Damaged settings fall back to defaults");
        await File.WriteAllTextAsync(store.SettingsPath, "{\"SchemaVersion\":1,\"Window\":null}");
        Assert(store.Load() == new UserSettings(), "Incomplete settings fall back to defaults");
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = true, SettingsPath = store.SettingsPath, Checks = new[]
        {
            "Window size and last visible position", "Output settings", "Timeline height and zoom ratio", "Inspector width and resize limits", "Timeline time slots and control spacing",
            "Atomic JSON save and load", "Damaged or incomplete JSON fallback"
        } }, JsonOptions));
        Directory.Delete(Path.GetDirectoryName(store.SettingsPath)!, recursive: true);
    }
}
