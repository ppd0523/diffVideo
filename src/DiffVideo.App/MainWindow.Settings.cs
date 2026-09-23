using System.IO;
using System.Windows;
using DiffVideo.App.Services;
using DiffVideo.Core;

namespace DiffVideo.App;

public partial class MainWindow
{
    private void RestoreWindowSettings(WindowUserSettings settings)
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;
        Width = FiniteClamp(settings.Width, MinWidth, Math.Max(MinWidth, virtualWidth), 1440);
        Height = FiniteClamp(settings.Height, MinHeight, Math.Max(MinHeight, virtualHeight), 900);
        Loaded += (_, _) => ResizeInspectorWidth(settings.InspectorWidth);
        if (settings.Left is { } left && settings.Top is { } top && double.IsFinite(left) && double.IsFinite(top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Clamp(left, virtualLeft, virtualLeft + Math.Max(0, virtualWidth - Width));
            Top = Math.Clamp(top, virtualTop, virtualTop + Math.Max(0, virtualHeight - Height));
        }
        if (settings.Maximized) { WindowState = WindowState.Maximized; }
    }

    private void RestoreOutputSettings(OutputUserSettings settings)
    {
        if (ViewModel is not { } vm) { return; }
        vm.CanvasWidth = settings.Width;
        vm.CanvasHeight = settings.Height;
        vm.OutputFps = settings.FramesPerSecond;
        vm.OutputDurationSeconds = Math.Clamp(
            double.IsFinite(settings.DurationSeconds) ? settings.DurationSeconds : 15,
            0.01, TimeSpan.MaxValue.TotalSeconds);
        vm.Quality = Enum.IsDefined(settings.Quality) ? settings.Quality : OutputQuality.Balanced;
    }

    private void RestoreTimelineSettings(TimelineUserSettings settings)
    {
        ResizeTimelineHeight(double.IsFinite(settings.Height) ? settings.Height : TimelineRow.MinHeight);
        UpdateLayout();
        TimelineView.RestoreZoomRatio(double.IsFinite(settings.ZoomRatio) ? settings.ZoomRatio : 1);
        UpdateTimeline();
    }

    internal void SaveUserSettings()
    {
        if (_settingsStore is null || ViewModel is not { } vm) { return; }
        try
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            var settings = new UserSettings
            {
                Window = new()
                {
                    Width = bounds.Width,
                    Height = bounds.Height,
                    InspectorWidth = InspectorColumn.ActualWidth > 0 ? InspectorColumn.ActualWidth : InspectorColumn.Width.Value,
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Maximized = WindowState == WindowState.Maximized
                },
                Output = new()
                {
                    Width = vm.CanvasWidth,
                    Height = vm.CanvasHeight,
                    FramesPerSecond = vm.OutputFps,
                    DurationSeconds = vm.OutputDurationSeconds,
                    Quality = vm.Quality
                },
                LastExportDirectory = Directory.Exists(_lastExportDirectory) ? _lastExportDirectory : null,
                Timeline = new()
                {
                    Height = TimelineRow.ActualHeight > 0 ? TimelineRow.ActualHeight : TimelineRow.Height.Value,
                    ZoomRatio = TimelineView.ZoomRatio
                }
            };
            _settingsStore.Save(settings);
            _userSettings = settings;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ViewModel.ReportInteraction("설정을 저장하지 못했습니다.", exception.Message);
        }
    }

    private static double FiniteClamp(double value, double minimum, double maximum, double fallback) =>
        Math.Clamp(double.IsFinite(value) ? value : fallback, minimum, maximum);
}
