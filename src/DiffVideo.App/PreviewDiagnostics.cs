using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiffVideo.App.Services;
using DiffVideo.App.ViewModels;
using DiffVideo.Core;
using DiffVideo.Infrastructure;

namespace DiffVideo.App;

/// <summary>Explicit, opt-in local diagnostics; not used by normal editing or export.</summary>
internal static partial class PreviewDiagnostics
{
    public static void Start(string[] args)
    {
        if (args.Length != 7) { throw new ArgumentException("--preview-diagnostics mode video1 video2 mp3 report.json seconds"); }
        var mode = args[1];
        var report = Path.GetFullPath(args[5]);
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        UserSettingsStore? settingsStore = null;
        if (mode == "settings")
        {
            var settingsDirectory = Directory.CreateTempSubdirectory("DiffVideo-settings-diagnostics-").FullName;
            settingsStore = new(Path.Combine(settingsDirectory, "settings.json"));
            settingsStore.Save(new()
            {
                Window = new() { Width = 1280, Height = 780, Left = 80, Top = 60 },
                Output = new() { Width = 1280, Height = 720, FramesPerSecond = 25, DurationSeconds = 19, Quality = OutputQuality.High },
                Timeline = new() { Height = 300, ZoomRatio = 4 },
                LastExportDirectory = settingsDirectory
            });
        }
        Window window = mode is "checks" or "ui" or "features" or "roi" or "placement" or "export-range" or "layers" or "settings"
            ? new MainWindow(settingsStore) : new Window
        {
            Title = "DiffVideo 프리뷰 측정 · " + mode,
            Width = 1000,
            Height = 640,
            Background = Brushes.Black,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        Application.Current.MainWindow = window;
        window.Loaded += async (_, _) =>
        {
            try
            {
                if (mode == "checks") { await CheckAsync((MainWindow)window, args[2..5], report); }
                else if (mode == "ui") { await CheckUiAsync((MainWindow)window, args[2..5], report); }
                else if (mode == "features") { await CheckFeaturesAsync((MainWindow)window, args[2..5], report); }
                else if (mode == "roi") { await CheckRoiEditorAsync((MainWindow)window, args[2..5], report); }
                else if (mode == "placement") { await CheckPlacementAsync((MainWindow)window, args[2..5], report); }
                else if (mode == "export-range") { await CheckExportRangeAsync((MainWindow)window, args[2..5], report); }
                else if (mode == "layers") { await CheckLayersAsync((MainWindow)window, args[2..5], report); }
                else if (mode == "settings") { await CheckSettingsAsync((MainWindow)window, settingsStore!, report); }
                else { await MeasureAsync(window, mode, args[2..5], report, int.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture)); }
                window.Close();
            }
            catch (Exception exception)
            {
                await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = false, Error = exception.ToString() }, JsonOptions));
                Application.Current.Shutdown(1);
            }
        };
        window.Show();
    }

    private static async Task MeasureAsync(Window window, string mode, string[] files, string report, int seconds)
    {
        var paths = FfmpegPaths.Discover();
        var probe = new MediaProbeService(paths);
        var one = await probe.ProbeAsync(files[0]);
        var two = await probe.ProbeAsync(files[1]);
        var music = await probe.ProbeAsync(files[2]);
        var composition = new Composition(
            new(one, TimeSpan.Zero, PixelRect.FullFrame(one), new(0, 0, 960, 1080), VideoFitMode.Fill, false, 0, true, 1),
            new(two, TimeSpan.FromSeconds(1), PixelRect.FullFrame(two), new(960, 0, 960, 1080), VideoFitMode.Fill, false, 1, true, 1),
            new(music, TimeSpan.FromSeconds(0.123), true, 0.3),
            new(1920, 1080, 30, TimeSpan.FromSeconds(seconds + 15), OutputQuality.Balanced));
        var image = new Image { Stretch = Stretch.Uniform };
        window.Content = image;
        using var cancellation = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var time = 0d;
        void OnTime(double value)
        {
            time = value;
            if (value >= 3 && !ready.Task.IsCompleted)
            {
                File.WriteAllText(report + ".ready", "ready");
                ready.SetResult();
            }
        }
        using var players = new PlayerPreviewSession(paths, mode == "fallback");
        using var legacyAudio = new TimelineAudioPreview(paths);
        Task playback;
        if (mode == "legacy")
        {
            playback = new FfmpegPreviewService(paths).StreamAsync(composition, 0, async frame =>
            {
                await window.Dispatcher.InvokeAsync(() =>
                {
                    var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels, frame.Width * 4);
                    bitmap.Freeze();
                    image.Source = bitmap;
                    OnTime(frame.TimeSeconds);
                });
            }, () => { legacyAudio.Start(composition, 0, cancellation.Token); return Task.CompletedTask; }, cancellation.Token);
        }
        else
        {
            image.Source = players.Image;
            playback = players.RunAsync(composition, 0, OnTime, _ => { }, cancellation.Token);
        }

        var first = await Task.WhenAny(playback, ready.Task, Task.Delay(TimeSpan.FromSeconds(45)));
        if (first != ready.Task)
        {
            cancellation.Cancel();
            try { await playback; } catch (OperationCanceledException) { }
            throw new InvalidOperationException("측정 준비 중 재생이 끝났거나 45초 안에 타임라인이 진행되지 않았습니다.");
        }
        var wall = Stopwatch.StartNew();
        await Task.WhenAny(playback, Task.Delay(TimeSpan.FromSeconds(seconds + 2)));
        cancellation.Cancel();
        try { await playback; } catch (OperationCanceledException) { }
        legacyAudio.Stop();
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
        {
            Success = true,
            Mode = mode,
            MeasurementWallSeconds = wall.Elapsed.TotalSeconds,
            TimelineSeconds = time,
            Backends = mode == "legacy" ? "0.2.0 FFmpeg merged rawvideo" : players.Backends,
            players.BufferingCount,
            MaximumObservedSkewMs = players.MaximumObservedSkewSeconds * 1000
        }, JsonOptions));
    }

    private static async Task CheckAsync(MainWindow window, string[] files, string report)
    {
        var checks = new List<string>();
        var vm = window.ViewModel!;
        await vm.LoadFilesAsync(files);
        var replacement = vm.Video1;
        replacement.SetDestination(new(24, 36, 800, 600));
        replacement.StartSeconds = 2;
        replacement.SetRoi(new(2, 2, replacement.RoiWidth - 2, replacement.RoiHeight - 2));
        replacement.FitMode = VideoFitMode.Fill;
        replacement.AspectRatioLocked = false;
        replacement.IncludeAudio = false;
        replacement.VolumePercent = 25;
        var preservedLayer = replacement.ZIndex;
        await vm.LoadVideoAsync(replacement, files.First(path => Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase)));
        Assert(replacement.ToModel().Destination == new PixelRect(24, 36, 800, 600) && replacement.ZIndex == preservedLayer,
            "Video replacement preserves placement and layer");
        Assert(replacement.StartSeconds == 0 && replacement.RoiX == 0 && replacement.RoiY == 0 &&
            replacement.RoiWidth == replacement.Media!.DisplayWidth && replacement.RoiHeight == replacement.Media.DisplayHeight &&
            replacement.FitMode == VideoFitMode.Fit && replacement.AspectRatioLocked &&
            replacement.IncludeAudio == replacement.Media.HasAudio && replacement.VolumePercent == 100,
            "Video replacement resets track-specific editing");
        checks.Add("Video replacement preserves placement/layer and resets edits");
        replacement.SetDestination(new(0, 0, vm.CanvasWidth / 2, vm.CanvasHeight));
        vm.OutputDurationSeconds = 12;
        vm.Video2.StartSeconds = 1.234;
        vm.Audio.StartSeconds = 0.1236;
        Assert(Math.Abs(vm.Audio.StartSeconds - 0.124) < 0.000001, "MP3 1ms snapping");
        checks.Add("MP3 1ms snapping");
        vm.PlayheadSeconds = 3;
        vm.FullPlayheadSeconds = 8;
        Assert(vm.PlayheadSeconds == 3 && vm.FullPlayheadSeconds == 8, "Segment and full playheads are independent");
        vm.SetExportBoundary(true, 3);
        Assert(vm.PlayheadSeconds == 3, "Segment head stays inside the playback range");
        await vm.StartPlaybackAsync();
        Assert(vm.IsPreviewLoading, "Initial loading indication");
        Assert(vm.CanStop && !vm.IsEditingEnabled && vm.CanStartExport && !vm.CanUseFullControls, "Playback control locks");
        await WaitAsync(() => vm.PlayheadSeconds > 3.3, () => vm.ErrorMessage);
        await vm.PausePlaybackAsync();
        Assert(vm.CanUseSegmentControls && vm.CanUseFullControls && vm.FullPlayheadSeconds == 8, "Pause unlocks both bars and preserves the other head");
        var paused = vm.PlayheadSeconds;
        await Task.Delay(180);
        Assert(vm.PlayheadSeconds == paused && !vm.IsPreviewLoading, "Pause does not auto-resume");
        await vm.StepFrameAsync(1);
        Assert(Math.Abs(vm.PlayheadSeconds - paused - 1d / vm.OutputFps) < 0.00001, "Frame step");
        await vm.StopPlaybackAsync();
        Assert(vm.PlayheadSeconds == vm.ExportStartSeconds, "Stop returns to segment start");
        vm.FullPlayheadSeconds = 0;
        await vm.StartFullPlaybackAsync();
        Assert(!vm.CanUseSegmentControls && vm.CanUseFullControls, "Full playback disables segment controls");
        await WaitAsync(() => vm.FullPlayheadSeconds > 0.2, () => vm.ErrorMessage);
        await vm.PauseFullPlaybackAsync();
        Assert(vm.PlayheadSeconds == vm.ExportStartSeconds, "Full playback leaves the segment head unchanged");
        await vm.StopFullPlaybackAsync();
        checks.AddRange(["Loading indication", "Individual playback progresses", "Editing locks / export remains enabled", "Pause", "Frame step", "Segment stop", "Independent full playback"]);

        var before = vm.Video1.StartSeconds;
        vm.BeginTimelineInteraction();
        vm.RequestTimelineDragPreview(vm.Video1, 1.6);
        await Task.Delay(350);
        Assert(vm.Video1.StartSeconds == before, "Drag must not commit");
        vm.CancelTimelineDragPreview();
        await vm.RefreshStillPreviewAsync();
        await vm.CommitTimelineTrackStartAsync(vm.Video1, 1.6);
        Assert(Math.Abs(vm.Video1.StartSeconds - 1.6) < 0.00001, "Drop commit");
        Assert(vm.CurrentPlaybackState == PlaybackState.Stopped, "Drag preserves transport state");
        checks.AddRange(["Staged drag", "Cancel restores preview", "Drop commits once / keeps transport state"]);

        await vm.StartPlaybackAsync();
        await vm.PausePlaybackAsync();
        await Task.Delay(250);
        Assert(!vm.IsPlaying && !vm.IsPreviewLoading, "Cancel during loading must not auto-resume");
        checks.Add("Pause during loading cancels automatic resume");

        await vm.StartPlaybackAsync();
        await WaitAsync(() => vm.PlayheadSeconds > 3.3, () => vm.ErrorMessage);
        var output = Path.ChangeExtension(report, ".mp4");
        var export = vm.ExportAsync(output, overwrite: true);
        Assert(vm.IsPlaying, "Export must not pause playback");
        await vm.PausePlaybackAsync();
        vm.Video1.StartSeconds = 0.5;
        Assert(vm.IsEditingEnabled, "Export must not lock editing");
        vm.CancelExport();
        await export;
        Assert(!File.Exists(output + ".part"), "Cancelled export cleanup");
        checks.AddRange(["Export while playing", "Editing while exporting", "Export cancellation cleanup"]);

        vm.OutputDurationSeconds = 0.4;
        vm.ResetExportRange();
        vm.PlayheadSeconds = 0;
        await vm.StartPlaybackAsync();
        await WaitAsync(() => !vm.IsPlaying, () => vm.ErrorMessage);
        Assert(vm.CurrentPlaybackState == PlaybackState.Paused, "Natural end pauses");
        Assert(Math.Abs(vm.PlayheadSeconds - PlaybackTimeline.LastFrameSeconds(0.4, vm.OutputFps)) < 0.00001, "Natural end frame");
        await vm.RefreshStillPreviewAsync();
        checks.Add("Output end freezes final frame");
        SaveScreenshot(window, Path.ChangeExtension(report, ".png"));
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = true, Checks = checks, Error = vm.ErrorMessage }, JsonOptions));
    }

    private static async Task CheckUiAsync(MainWindow window, string[] files, string report)
    {
        var checks = new List<string>();
        using var bindingOutput = new StringWriter();
        using var listener = new TextWriterTraceListener(bindingOutput);
        var bindingSource = PresentationTraceSources.DataBindingSource;
        var previousLevel = bindingSource.Switch.Level;
        bindingSource.Switch.Level = SourceLevels.Warning;
        bindingSource.Listeners.Add(listener);
        try
        {
            var vm = window.ViewModel!;
            var prefix = Path.Combine(Path.GetDirectoryName(report)!, Path.GetFileNameWithoutExtension(report));
            var layouts = new List<object>();
            async Task CaptureAsync(string name, double width, double height)
            {
                window.Width = width;
                window.Height = height;
                await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                AssertControlsVisible(window);
                Assert(window.PreviewDropHost.ActualWidth > window.ActualWidth * 0.72, "Preview uses the space freed by the media sidebar");
                Assert(window.PreviewDropHost.ActualHeight > window.ActualHeight * 0.43, "Preview is no longer reduced by transport / branding rows");
                Assert(window.InspectorPanel.ActualWidth <= 216, "Compact inspector");
                Assert(window.TimelineRow.ActualHeight >= 223 && window.TimelineRow.ActualHeight <= window.ActualHeight / 2 + 1, "Timeline height stays within resize limits");
                Assert(window.TransportBar.IsAncestorOf(window.PlayButton) && window.TransportBar.IsAncestorOf(window.PlaybackTimeDisplay), "Transport and time belong to the timeline header");
                Assert(!window.PreviewDropHost.IsAncestorOf(window.PlayButton), "No transport inside preview");
                layouts.Add(new { Name = name, PreviewWidth = window.PreviewDropHost.ActualWidth, PreviewHeight = window.PreviewDropHost.ActualHeight, InspectorWidth = window.InspectorPanel.ActualWidth, TimelineHeight = window.IntegratedTimeline.ActualHeight });
                SaveScreenshot(window, prefix + "-" + name + ".png");
                checks.Add(name + " layout / transport bounds");
            }

            Assert(!window.PlayButton.IsEnabled && !window.ExportButton.IsEnabled, "Empty state controls");
            await CaptureAsync("empty", 1440, 900);
            await CaptureAsync("empty-minimum", 1120, 720);
            var drop = new DataObject(DataFormats.FileDrop, files);
            Assert(!window.AllowDrop && window.PreviewDropHost.AllowDrop, "File drops are scoped to preview");
            Assert(window.CanAcceptPreviewDrop(drop), "Preview accepts MP4 / MP3 files");
            Assert(!window.CanAcceptPreviewDrop(new DataObject(DataFormats.FileDrop, new[] { "unsupported.txt" })), "Unsupported drops rejected");
            await window.ImportPreviewDropAsync(drop);
            Assert(vm.HasComposition && vm.Audio.HasMedia, "Preview drop loads both videos and MP3");
            checks.Add("Preview-only file drop / unsupported-file filtering");
            vm.OutputDurationSeconds = 95;
            vm.Video2.StartSeconds = 5;
            vm.Audio.StartSeconds = 0.124;
            vm.PlayheadSeconds = 12;
            Assert(MainViewModel.TryParseDurationSeconds("95", out var secondsOnly) && secondsOnly == 95 &&
                MainViewModel.TryParseDurationSeconds("95.5", out var decimalSeconds) && decimalSeconds == 95.5 &&
                MainViewModel.TryParseDurationSeconds("01:35.000", out var timeCode) && timeCode == 95 &&
                !MainViewModel.TryParseDurationSeconds("0.009", out _), "Output duration accepts seconds and timecode with a 0.01 second minimum");
            Assert(vm.TryApplyOutputDuration("01:35.500") && Math.Abs(vm.OutputDurationSeconds - 95.5) < 0.001, "Output duration applies explicit timecode input");
            vm.OutputDurationSeconds = 95;
            await vm.RefreshStillPreviewAsync();
            await CaptureAsync("loaded", 1440, 900);
            await CaptureAsync("loaded-minimum", 1120, 720);

            foreach (var button in new[] { window.ExportButton, window.CancelExportButton,
                window.PlayButton, window.PauseButton, window.StopButton, window.PreviousFrameButton, window.NextFrameButton,
                window.FullPlayButton, window.FullPauseButton, window.FullStopButton, window.FullPreviousFrameButton, window.FullNextFrameButton,
                window.EditRoiButton, window.ResetRoiButton, window.SendBackwardButton, window.BringForwardButton })
            {
                AssertMaterialIcon(button);
                Assert(button.ActualWidth >= 29.5 || button.Visibility == Visibility.Collapsed, "Icon button hit target");
                var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
                Assert(!string.IsNullOrWhiteSpace(peer.GetName()), "Icon button accessible name");
                Assert(ToolTipService.GetShowOnDisabled(button), "Disabled icon button tooltip");
            }
            checks.Add("Material action icons / accessible names / tooltips / hit targets");

            AssertIconToggle(window.Video1AudioCheckBox, "Icon.VolumeUp", "Icon.VolumeOff");
            AssertIconToggle(window.Mp3AudioCheckBox, "Icon.VolumeUp", "Icon.VolumeOff");
            AssertIconToggle(window.InspectorAudioCheckBox, "Icon.VolumeUp", "Icon.VolumeOff");
            AssertIconToggle(window.AspectLockCheckBox, "Icon.Lock", "Icon.LockOpen");
            Assert(vm.Video1.IncludeAudio && vm.Audio.IncludeAudio && vm.SelectedVideo.IncludeAudio && vm.SelectedVideo.AspectRatioLocked, "Icon toggle bindings restored");
            checks.Add("Audio / mute and aspect-lock icons follow checked state");

            Assert(window.PlayButton.IsEnabled && window.ExportButton.IsEnabled, "Loaded state controls");
            var sliderTrack = (System.Windows.Controls.Primitives.Track)window.PlayheadSlider.Template.FindName("PART_Track", window.PlayheadSlider);
            Assert(sliderTrack.ActualWidth > 0 && sliderTrack.Maximum == vm.OutputDurationSeconds, "Slider template track binding");
            window.PlayheadSlider.Value = 10;
            Assert(vm.PlayheadSeconds == 10, "Slider two-way seek binding");
            window.NextFrameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitAsync(() => vm.PlayheadSeconds > 10, () => vm.ErrorMessage);
            Assert(Math.Abs(vm.PlayheadSeconds - 10 - 1d / vm.OutputFps) < 0.00001, "Frame button wiring");
            checks.AddRange(["Empty / loaded button state", "Slider template / seek binding", "Frame button click"]);

            window.CanvasSettingsTab.IsSelected = true;
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert(window.CanvasSettingsTab.IsSelected && window.FpsInput.IsVisible && window.QualityHighButton.IsVisible, "Canvas tab shows output settings");
            Assert(window.FpsInput.Height == 30 && Math.Abs(window.FpsInput.ActualHeight - 30) < 1, $"FPS input uses the requested height: {window.FpsInput.ActualHeight}");
            Assert(window.OutputDurationLabel.Visibility == Visibility.Visible && window.OutputDurationEditor.Visibility == Visibility.Collapsed, "Duration defaults to a text label");
            window.OutputDurationLabel.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            window.UpdateLayout();
            Assert(window.OutputDurationEditor.Visibility == Visibility.Visible && window.OutputDurationInput.Height == 30 && window.OutputDurationInput.Width == 100 && Math.Abs(window.OutputDurationInput.ActualHeight - 30) < 1, $"Duration editor uses the requested size: {window.OutputDurationInput.ActualWidth}x{window.OutputDurationInput.ActualHeight}");
            Assert(window.OutputDurationInput.Padding == new Thickness(3.6, 2.4, 3.6, 2.4), $"Inputs use compact padding: {window.OutputDurationInput.Padding}");
            window.OutputDurationInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.OutputDurationInput), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert(window.OutputDurationLabel.Visibility == Visibility.Visible && window.OutputDurationEditor.Visibility == Visibility.Collapsed, "Escape cancels inline duration editing");
            Assert(window.Video1AudioCheckBox.Width == 18 && window.Video1AudioCheckBox.Height == 18 && window.Video1AudioCheckBox.ActualHeight <= 18, "Timeline audio icon fits inside its row");
            Assert(window.Video1AudioCheckBox.Template.FindName("Glyph", window.Video1AudioCheckBox) is ContentControl { ActualWidth: > 13, ActualHeight: > 13 }, "Timeline audio glyph scales to show the complete icon");
            Assert(window.PlayButton.Width == 42 && window.PlayButton.Height == 30 && window.FullPlayButton.Width == 42 && window.FullPlayButton.Height == 30, "Both transport bars use 42 by 30 controls");
            Assert(window.ZoomOutButton.Width == 30 && window.ZoomOutButton.Height == 30 && window.FitTimelineButton.Width == 30 && window.FitTimelineButton.Height == 30, "Timeline view controls use 30 by 30 sizing");
            Assert(window.ExportButton.Width == 84 && window.ExportButton.Height == 30 && window.SelectedVideoFileButton.Height == 30 && window.SelectedVideoFileButton.Padding == new Thickness(3.6, 2.4, 3.6, 2.4), "Wide export and file picker controls match the requested sizing");
            Assert(window.FullTransportBar.ColumnDefinitions.Count == 4 && window.FullTransportBar.ColumnDefinitions.All(column => Math.Abs(column.ActualWidth - window.FullTransportBar.ActualWidth / 4) < 1) &&
                Grid.GetColumn(window.FullInfoGroup) == 0 && Grid.GetColumn(window.FullControlsGroup) == 2 && Grid.GetColumn(window.TimelineScaleGroup) == 3 &&
                window.TransportBar.ColumnDefinitions.Count == 4 && window.TransportBar.ColumnDefinitions.All(column => Math.Abs(column.ActualWidth - window.TransportBar.ActualWidth / 4) < 1) &&
                Grid.GetColumn(window.SegmentInfoGroup) == 0 && Grid.GetColumn(window.SegmentPresetGroup) == 1 && Grid.GetColumn(window.SegmentControlsGroup) == 2 && Grid.GetColumn(window.SegmentExportGroup) == 3,
                "Transport rows use four equal zones");
            Assert(window.FullControlsGroup.HorizontalAlignment == HorizontalAlignment.Center && window.TimelineScaleGroup.HorizontalAlignment == HorizontalAlignment.Right &&
                window.SegmentPresetGroup.HorizontalAlignment == HorizontalAlignment.Center && window.SegmentControlsGroup.HorizontalAlignment == HorizontalAlignment.Center &&
                window.SegmentExportGroup.HorizontalAlignment == HorizontalAlignment.Right,
                "Transport actions use the requested center and right alignment");
            Assert(Math.Abs(window.TimelineScaleGroup.TranslatePoint(new Point(window.TimelineScaleGroup.ActualWidth, 0), window.FullTransportBar).X - window.FullTransportBar.ActualWidth) < 1 &&
                Math.Abs(window.SegmentExportGroup.TranslatePoint(new Point(window.SegmentExportGroup.ActualWidth, 0), window.TransportBar).X - window.TransportBar.ActualWidth) < 1,
                "Scale and export actions reach the right edge of their transport bar");
            Assert(window.CanvasSettingsTab.ActualWidth + window.VideoSettingsTab.ActualWidth +
                window.CanvasSettingsTab.Margin.Left + window.CanvasSettingsTab.Margin.Right +
                window.VideoSettingsTab.Margin.Left + window.VideoSettingsTab.Margin.Right <= window.SettingsTabs.ActualWidth + 1,
                "Settings tab headers fit without clipping");
            Assert(window.TransportBar.IsAncestorOf(window.ExportButton) && window.TransportBar.IsAncestorOf(window.CancelExportButton), "Export and cancel actions are in the segment transport bar");
            var beganVideoStartEdit = window.BeginTimelineStartEdit(vm.Video1);
            Assert(beganVideoStartEdit && window.Video1StartEditor.Visibility == Visibility.Visible && window.Video1StartInput.Width == 100 && window.Video1StartEditor.Margin.Left == -4,
                $"Video block double-click editor uses shared inline sizing and offset: began={beganVideoStartEdit}, visibility={window.Video1StartEditor.Visibility}, width={window.Video1StartInput.Width}, margin={window.Video1StartEditor.Margin.Left}");
            window.Video1StartInput.Text = "2.5";
            window.Video1StartInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.Video1StartInput), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await WaitAsync(() => Math.Abs(vm.Video1.StartSeconds - 2.5) < 0.00001, () => vm.ErrorMessage);
            Assert(window.Video1StartLabel.Visibility == Visibility.Visible && window.Video1StartLabel.FontSize == 13 && window.Video1StartLabel.FontWeight == FontWeights.SemiBold, "Block start edit commits and restores the larger centered label");
            await vm.CommitTimelineTrackStartAsync(vm.Video1, 0);
            Assert(window.BeginTimelineStartEdit(vm.Audio) && window.AudioStartEditor.Visibility == Visibility.Visible, "MP3 block supports the same start editor");
            window.AudioStartInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.AudioStartInput), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert(window.AudioStartLabel.Visibility == Visibility.Visible, "MP3 start edit cancels back to its label");
            SaveScreenshot(window, prefix + "-canvas-settings.png");
            window.Video1FileName.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Assert(window.VideoSettingsTab.IsSelected && ReferenceEquals(vm.SelectedVideo, vm.Video1), "Timeline video label selects Video 1 and opens its settings");
            window.Video2FileName.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Assert(window.VideoSettingsTab.IsSelected && ReferenceEquals(vm.SelectedVideo, vm.Video2), "Timeline video label selects Video 2 and opens its settings");
            window.VideoSettingsTab.IsSelected = true;
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert(window.VideoSettingsTab.IsSelected && window.FitModeFitButton.IsVisible && window.FitModeFillButton.IsVisible && window.FitModeStretchButton.IsVisible, "Video tab shows fit mode controls");
            window.FitModeFillButton.BringIntoView();
            window.FitModeFillButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert(!vm.SelectedVideo.AspectRatioLocked && vm.SelectedVideo.FitMode == VideoFitMode.Fill, "Fit mode selection unlocks aspect ratio and updates mode");
            window.AspectLockCheckBox.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            Assert(vm.SelectedVideo.AspectRatioLocked && vm.SelectedVideo.FitMode == VideoFitMode.Fill, "Aspect lock retains the fit mode");
            window.AspectLockCheckBox.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
            Assert(!vm.SelectedVideo.AspectRatioLocked && vm.SelectedVideo.FitMode == VideoFitMode.Fill, "Unlock restores the retained fit mode selection");
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            SaveScreenshot(window, prefix + "-video-modes.png");
            window.InspectorAudioCheckBox.BringIntoView();
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert(window.InspectorAudioCheckBox.IsVisible && window.InspectorVolumeInput.IsVisible && window.InspectorVolumeInput.Height == 30 && Math.Abs(window.InspectorVolumeInput.ActualHeight - 30) < 1, "Video audio controls use the compact icon, slider, and numeric input row");
            var originalVolume = vm.SelectedVideo.VolumePercent;
            window.InspectorVolumeInput.Text = "125";
            window.InspectorVolumeInput.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Assert(Math.Abs(vm.SelectedVideo.VolumePercent - 125) < 0.001, "Video volume numeric input updates the model");
            vm.SelectedVideo.VolumePercent = originalVolume;
            SaveScreenshot(window, prefix + "-video-settings.png");
            checks.Add("FPS input / quality presets / aspect lock and fit mode buttons");
            var originalFps = vm.OutputFps;
            window.Fps24Button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert(vm.OutputFps == 24, "FPS preset updates model");
            window.QualityHighButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert(vm.Quality == OutputQuality.High, "Quality preset updates model");
            vm.OutputFps = originalFps;
            checks.Add("FPS and quality presets update model");

            var png = await vm.GetSourceFramePngAsync(vm.Video1);
            var media = vm.Video1.Media!;
            var roi = new PixelRect(100, 100, 600, 400);
            var editor = new RoiEditorWindow(png, media.DisplayWidth, media.DisplayHeight, roi) { Owner = window };
            try
            {
                editor.Show();
                await editor.Dispatcher.InvokeAsync(editor.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert(editor.SelectedRoi == roi && editor.RoiRectangle.ActualWidth > 0, "ROI layout mapping");
                AssertMaterialIcon(editor.FullFrameButton);
                AssertMaterialIcon(editor.CancelRoiButton);
                AssertMaterialIcon(editor.ApplyRoiButton);
                SaveScreenshot(editor, prefix + "-roi.png");
                editor.Width = 640;
                editor.Height = 480;
                await editor.Dispatcher.InvokeAsync(editor.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert(editor.ImageHost.ActualHeight > 100 && editor.RoiRectangle.ActualWidth > 0, "Minimum ROI layout");
                SaveScreenshot(editor, prefix + "-roi-minimum.png");
                checks.Add("ROI default / minimum layout and crop mapping");
            }
            finally { editor.Close(); }

            await CheckRoiEditsAsync(window, prefix, checks);
            await CheckPreviewGesturesAsync(window, prefix, checks);
            await CheckSpaceShortcutAsync(window, checks);

            await vm.StartPlaybackAsync();
            window.UpdateLayout();
            Assert(window.PreviewLoadingOverlay.Visibility == Visibility.Visible, "Visible loading overlay binding");
            Assert(!window.PlayButton.IsEnabled && window.PauseButton.IsEnabled && window.ExportButton.IsEnabled, "Playing UI bindings");
            Assert(!window.CanAcceptPreviewDrop(drop) && !window.TimelineVideo1.IsEnabled, "Playback locks preview file drops and clip dragging");
            Assert(!window.BeginPreviewRoi(new Point(480, 540)), "Playback locks ROI dragging");
            SaveScreenshot(window, prefix + "-loading.png");
            var playbackFrom = vm.PlayheadSeconds;
            await WaitAsync(() => vm.PlayheadSeconds > playbackFrom + 0.2, () => vm.ErrorMessage);
            await vm.PausePlaybackAsync();
            window.UpdateLayout();
            Assert(window.PreviewLoadingOverlay.Visibility == Visibility.Collapsed, "Loading overlay dismissed");
            checks.Add("Loading visibility / playback button states");

            listener.Flush();
            var errors = bindingOutput.ToString();
            Assert(string.IsNullOrWhiteSpace(errors), "WPF binding warnings: " + errors);
            checks.Add("No WPF binding warnings");
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = true, Checks = checks, Layouts = layouts, BindingWarnings = errors }, JsonOptions));
        }
        finally
        {
            bindingSource.Listeners.Remove(listener);
            bindingSource.Switch.Level = previousLevel;
        }
    }

    private static void AssertControlsVisible(MainWindow window)
    {
        foreach (var control in new FrameworkElement[] { window.PlayButton, window.PauseButton, window.StopButton, window.PreviousFrameButton, window.NextFrameButton, window.FullPlayButton, window.FullPauseButton, window.FullStopButton, window.FullPreviousFrameButton, window.FullNextFrameButton, window.ExportButton, window.PlayheadSlider })
        {
            var bounds = control.TransformToAncestor(window).TransformBounds(new Rect(control.RenderSize));
            Assert(bounds.Left >= 0 && bounds.Right <= window.ActualWidth && bounds.Top >= 0 && bounds.Bottom <= window.ActualHeight,
                control.Name + " must be inside the window");
            Assert(control.ActualHeight >= 28, control.Name + " must retain its hit target");
            if (control.Parent is FrameworkElement parent)
            {
                var local = control.TransformToAncestor(parent).TransformBounds(new Rect(control.RenderSize));
                Assert(local.Left >= -1 && local.Right <= parent.ActualWidth + 1, control.Name + " must fit its parent");
            }
        }
    }

    private static async Task CheckRoiEditsAsync(MainWindow window, string prefix, List<string> checks)
    {
        var vm = window.ViewModel!;
        var track = vm.Video1;
        vm.SelectVideo(track);
        await vm.ApplyRoiAsync(track, PixelRect.FullFrame(track.Media!));
        var imageBefore = PreviewFingerprint(vm.PreviewImage!);
        var atSeconds = vm.PlayheadSeconds;
        var destination = track.ToModel().Destination;
        var notifications = 0;
        void Observe(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName?.StartsWith("Roi", StringComparison.Ordinal) != true) { return; }
            var model = track.ToModel();
            Assert(model.Roi == RoiBounds.Clamp(model.Roi, model.Media.DisplayWidth, model.Media.DisplayHeight), "Every notified ROI must already be valid");
            notifications++;
        }
        track.PropertyChanged += Observe;
        try
        {
            window.RoiXInput.SetCurrentValue(TextBox.TextProperty, "100");
            window.RoiXInput.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert(track.RoiX == 100 && track.RoiWidth == track.Media!.DisplayWidth - 100, "X edit also limits width");
            await WaitAsync(() => PreviewFingerprint(vm.PreviewImage!) != imageBefore, () => vm.ErrorMessage);
            Assert(vm.PlayheadSeconds == atSeconds, "ROI refresh keeps the playhead");
            window.RoiWidthInput.SetCurrentValue(TextBox.TextProperty, int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
            window.RoiWidthInput.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert(track.RoiWidth == track.Media!.DisplayWidth - track.RoiX, "Oversized numeric ROI is clamped");
            window.RoiYInput.SetCurrentValue(TextBox.TextProperty, "-10");
            window.RoiYInput.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert(track.RoiY == 0, "Negative ROI is clamped");
            await vm.ApplyRoiAsync(track, new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue));
            Assert(track.RoiWidth == 2 && track.RoiHeight == 2, "Boundary ROI retains a valid minimum size");
            Assert(CompositionValidator.Validate(vm.BuildComposition()).Count == 0, "Boundary crop validates");
            await vm.ApplyRoiAsync(track, new(120, 80, 640, 360));
            Assert(track.ToModel().Destination == destination && vm.PlayheadSeconds == atSeconds, "ROI apply preserves placement and playhead");
            Assert(notifications > 0, "ROI notification coverage");
        }
        finally { track.PropertyChanged -= Observe; }
        checks.Add("Numeric ROI auto-refresh / atomic bounds / oversized and negative input");

        // Exercise the actual modal Apply button and MainWindow's callback, not only the model.
        var dialogDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var roiWindow = window.OwnedWindows.OfType<RoiEditorWindow>().FirstOrDefault();
            if (roiWindow is null) { return; }
            timer.Stop();
            try
            {
                roiWindow.UpdateLayout();
                roiWindow.SetRoi(new(200, 100, 500, 300));
                roiWindow.ApplyRoiButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                dialogDone.SetResult();
            }
            catch (Exception exception) { roiWindow.Close(); dialogDone.SetException(exception); }
        };
        timer.Start();
        try
        {
            imageBefore = PreviewFingerprint(vm.PreviewImage!);
            window.EditRoiButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await dialogDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitAsync(() => track.ToModel().Roi == new PixelRect(200, 100, 500, 300) && PreviewFingerprint(vm.PreviewImage!) != imageBefore, () => vm.ErrorMessage);
        }
        finally { timer.Stop(); }
        SaveScreenshot(window, prefix + "-roi-applied.png");
        checks.Add("ROI modal Apply updates the actual preview without moving the playhead");

        // Other invalid settings must also fail locally instead of escaping an async UI handler.
        var originalX = track.DestinationX;
        track.DestinationX = int.MaxValue;
        await vm.StartPlaybackAsync();
        Assert(!vm.IsPlaying && !string.IsNullOrWhiteSpace(vm.ErrorMessage), "Invalid configuration does not crash Play");
        var rejectedOutput = prefix + "-invalid.mp4";
        await vm.ExportAsync(rejectedOutput, overwrite: true);
        Assert(!vm.IsExporting && !File.Exists(rejectedOutput), "Invalid configuration does not start export");
        track.DestinationX = originalX;
        await vm.RefreshStillPreviewAsync();
        Assert(string.IsNullOrEmpty(vm.ErrorMessage), "Valid edit recovers from validation error");
        checks.Add("Invalid playback / export settings are reported without terminating the app");
    }

    private static string PreviewFingerprint(ImageSource source)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) { drawing.DrawImage(source, new Rect(0, 0, 160, 90)); }
        var bitmap = new RenderTargetBitmap(160, 90, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[160 * 90 * 4];
        bitmap.CopyPixels(pixels, 160 * 4, 0);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));
    }

    private static async Task CheckPreviewGesturesAsync(MainWindow window, string prefix, List<string> checks)
    {
        var vm = window.ViewModel!;
        await vm.ApplyRoiAsync(vm.Video1, PixelRect.FullFrame(vm.Video1.Media!));
        await vm.ApplyRoiAsync(vm.Video2, PixelRect.FullFrame(vm.Video2.Media!));
        var playhead = vm.PlayheadSeconds;
        foreach (var (track, overlay) in new[] { (vm.Video1, window.Video1Overlay), (vm.Video2, window.Video2Overlay) })
        {
            track.FitMode = VideoFitMode.Fit;
            // Exercise the same left-click handlers as the actual player, before the right-drag gesture.
            var placementBeforeSyntheticClick = track.ToModel().Destination;
            overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            track.SetDestination(placementBeforeSyntheticClick);
            await vm.RefreshStillPreviewAsync();
            Assert(ReferenceEquals(vm.SelectedVideo, track), "Player click activates the intended video");
            var other = ReferenceEquals(track, vm.Video1) ? vm.Video2 : vm.Video1;
            var otherBefore = other.ToModel();
            var before = track.ToModel();
            var clip = PreviewLayout.From(before).Clip;
            var from = new Point(clip.X + clip.Width / 4, clip.Y + clip.Height / 4);
            var to = new Point(clip.X + clip.Width * 3 / 4, clip.Y + clip.Height * 3 / 4);
            Assert(!window.BeginPreviewRoi(new Point(other.DestinationX + other.DestinationWidth / 2d, other.DestinationY + other.DestinationHeight / 2d)), "Other player cannot become the ROI target through a right-click");
            Assert(!window.BeginPreviewRoi(new Point(before.Destination.X + 10, before.Destination.Y + 5)), "Fit letterbox cannot start an ROI");
            Assert(window.BeginPreviewRoi(from), "Begin ROI on selected player");
            window.UpdatePreviewRoi(to);
            Assert(track.ToModel() == before && other.ToModel() == otherBefore, "Dragging only shows a rectangle; no model updates");
            Assert(window.PreviewRoiSelection.Visibility == Visibility.Visible && window.PreviewRoiSelection.Width > 0, "ROI rectangle is visible");
            await window.HandleSpacePressAsync(false);
            Assert(!vm.IsPlaying, "Space is ignored during ROI drag");
            RaiseKey(window, Key.Space, keyUp: true);
            SaveScreenshot(window, prefix + "-direct-roi-" + (ReferenceEquals(track, vm.Video1) ? "1" : "2") + ".png");
            Assert(window.IsRoiDragging && window.PreviewRoiSelection.ActualWidth > 0 && window.PreviewRoiSelection.ActualHeight > 0, "ROI overlay is laid out and visible before release");
            await window.CompletePreviewRoiAsync(to);
            var expectedRoi = PreviewRoiSelection.Map(before, from.X, from.Y, to.X, to.Y);
            Assert(track.ToModel().Roi == expectedRoi, $"Drop maps the selected rectangle to the original source: {track.ToModel().Roi} != {expectedRoi}; {vm.Status}");
            Assert(track.ToModel().Destination == before.Destination && track.FitMode == before.FitMode, "Cropping preserves player placement / fit mode");
            Assert(other.ToModel() == otherBefore && vm.PlayheadSeconds == playhead, "Only the active video changes; playhead is preserved");
            Assert(!window.IsRoiDragging && !window.PreviewLogicalCanvas.IsMouseCaptured, "Drop releases the pointer");
            var cropped = track.ToModel();
            var repeatClip = PreviewLayout.From(cropped).Clip;
            from = new Point(repeatClip.X + repeatClip.Width / 4, repeatClip.Y + repeatClip.Height / 4);
            to = new Point(repeatClip.X + repeatClip.Width * 3 / 4, repeatClip.Y + repeatClip.Height * 3 / 4);
            Assert(window.BeginPreviewRoi(from), "Repeated crop starts on the current picture");
            await window.CompletePreviewRoiAsync(to);
            Assert(track.ToModel().Roi == PreviewRoiSelection.Map(cropped, from.X, from.Y, to.X, to.Y), "Repeated crop uses the existing ROI as its source transform");
            window.ResetRoiButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitAsync(() => track.ToModel().Roi == PixelRect.FullFrame(track.Media!), () => vm.ErrorMessage);
            await vm.RefreshStillPreviewAsync();
        }
        checks.Add("Player activation / active-only ROI / staged rectangle / repeated crop / reset");

        vm.SelectVideo(vm.Video1);
        var original = vm.Video1.ToModel();
        var imageBefore = PreviewFingerprint(vm.PreviewImage!);
        Assert(window.BeginPreviewRoi(new Point(480, 540)), "Boundary drag begins");
        window.UpdatePreviewRoi(new Point(5000, -5000));
        Assert(Canvas.GetLeft(window.PreviewRoiSelection) + window.PreviewRoiSelection.Width <= 960 && Canvas.GetTop(window.PreviewRoiSelection) >= 270, "Rectangle clamps to the active displayed picture");
        RaiseKey(window, Key.Escape);
        Assert(!window.IsRoiDragging && vm.Video1.ToModel() == original && PreviewFingerprint(vm.PreviewImage!) == imageBefore, "Escape cancels without changing ROI / image");
        Assert(window.BeginPreviewRoi(new Point(480, 540)), "Tiny drag begins");
        await window.CompletePreviewRoiAsync(new Point(481, 541));
        Assert(vm.Video1.ToModel() == original, "Accidental right-click / tiny drag is ignored");
        Assert(window.BeginPreviewRoi(new Point(480, 540)), "Capture-loss drag begins");
        window.PreviewLogicalCanvas.ReleaseMouseCapture();
        Assert(!window.IsRoiDragging && vm.Video1.ToModel() == original, "Lost pointer capture cancels the drag");
        Assert(window.BeginPreviewRoi(new Point(480, 540)), "Resize drag begins");
        var width = window.Width;
        window.Width += 20;
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Assert(!window.IsRoiDragging && vm.Video1.ToModel() == original, "Resize cancels coordinates captured at the old scale");
        window.Width = width;
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        checks.Add("ROI boundary clamp / Escape / tiny drag / capture loss / resize cancellation");

        var x2 = vm.Video2.DestinationX;
        vm.Video2.DestinationX = 0;
        await vm.RefreshStillPreviewAsync();
        var compositionBefore = vm.BuildComposition();
        imageBefore = PreviewFingerprint(vm.PreviewImage!);
        Assert(window.BeginPreviewRoi(new Point(480, 540)), "Occluded active player accepts ROI drag");
        window.UpdatePreviewRoi(new Point(700, 700));
        Assert(PreviewFingerprint(vm.PreviewImage!) != imageBefore, "Occluded active player is temporarily displayed in front");
        Assert(vm.BuildComposition() == compositionBefore, "Temporary front display does not affect the export snapshot");
        Assert(Panel.GetZIndex(window.Video1Overlay) == int.MaxValue, "Active player outline follows display focus");
        SaveScreenshot(window, prefix + "-direct-roi-overlap.png");
        window.CancelPreviewRoi();
        Assert(PreviewFingerprint(vm.PreviewImage!) == imageBefore && Panel.GetZIndex(window.Video1Overlay) == vm.Video1.ZIndex, "Cancel restores display layer order and binding");
        Assert(window.BeginPreviewRoi(new Point(480, 540)), "Overlapped crop restart");
        await window.CompletePreviewRoiAsync(new Point(700, 700));
        Assert(vm.Video1.ZIndex == compositionBefore.Video1.ZIndex && vm.Video2.ZIndex == compositionBefore.Video2.ZIndex, "Commit preserves both export layer indices");
        Assert(PreviewFingerprint(vm.PreviewImage!) == imageBefore, "Commit restores the original front player too");
        vm.Video2.DestinationX = x2;
        await vm.ApplyRoiAsync(vm.Video1, PixelRect.FullFrame(vm.Video1.Media!));
        checks.Add("Overlapped active player focus / export snapshot isolation / cancel and commit restore order");
    }

    private static KeyEventArgs RaiseKey(MainWindow window, Key key, bool keyUp = false)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key)
        {
            RoutedEvent = keyUp ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent
        };
        window.RaiseEvent(args);
        return args;
    }

    private static async Task CheckSpaceShortcutAsync(MainWindow window, List<string> checks)
    {
        var vm = window.ViewModel!;
        window.PreviewLogicalCanvas.Focus();
        var from = vm.PlayheadSeconds;
        Assert(RaiseKey(window, Key.Space).Handled && vm.IsPlaying && vm.IsPreviewLoading, "Space starts playback through the window key handler");
        RaiseKey(window, Key.Space); // A held key must never alternate between Play and Pause.
        Assert(vm.IsPlaying, "Held space does not toggle twice");
        RaiseKey(window, Key.Space, keyUp: true);
        RaiseKey(window, Key.Space);
        Assert(!vm.IsPlaying && !vm.IsPreviewLoading, "Space cancels playback preparation");
        RaiseKey(window, Key.Space, keyUp: true);
        await vm.RefreshStillPreviewAsync();
        RaiseKey(window, Key.Space);
        RaiseKey(window, Key.Space, keyUp: true);
        await WaitAsync(() => vm.PlayheadSeconds > from + 0.2, () => vm.ErrorMessage);
        RaiseKey(window, Key.Space);
        RaiseKey(window, Key.Space, keyUp: true);
        var paused = vm.PlayheadSeconds;
        await Task.Delay(100);
        Assert(!vm.IsPlaying && vm.PlayheadSeconds == paused, "Space pauses at the current playhead");
        RaiseKey(window, Key.Space);
        RaiseKey(window, Key.Space, keyUp: true);
        await WaitAsync(() => vm.PlayheadSeconds > paused + 0.2, () => vm.ErrorMessage);
        await vm.PausePlaybackAsync();
        checks.Add("Space start / pause / resume / loading cancellation / one toggle per press");

        window.RoiXInput.BringIntoView();
        window.RoiXInput.Focus();
        Assert(!RaiseKey(window, Key.Space).Handled && !vm.IsPlaying, "Space stays in numeric/text inputs");
        window.FpsInput.Focus();
        Assert(!RaiseKey(window, Key.Space).Handled && !vm.IsPlaying, "Space stays in FPS input");
        Assert(MainWindow.IsTextEntry(new PasswordBox()) && MainWindow.IsTextEntry(new RichTextBox()), "Text-entry guard includes password / rich text controls");
        window.PreviewLogicalCanvas.Focus();
        await window.HandleSpacePressAsync(true);
        Assert(!vm.IsPlaying, "An OS auto-repeat without a fresh press is ignored");
        var clip = window.TimelineVideo1.Children.OfType<Border>().Single();
        clip.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        RaiseKey(window, Key.Space);
        RaiseKey(window, Key.Space, keyUp: true);
        Assert(!vm.IsPlaying && clip.IsMouseCaptured, "Space does not interrupt a timeline drag");
        RaiseKey(window, Key.Escape);
        Assert(!clip.IsMouseCaptured, "Escape releases the timeline drag");
        await vm.RefreshStillPreviewAsync();
        checks.Add("Space ignores editors / dropdowns / drag gestures / OS repeats");
    }

    private static void AssertMaterialIcon(ContentControl control)
    {
        control.ApplyTemplate();
        Assert(control.Content is Geometry geometry && !geometry.Bounds.IsEmpty && geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0,
            control.Name + " must contain a Material vector icon");
        Assert(!string.IsNullOrWhiteSpace(control.ToolTip as string), control.Name + " needs a text tooltip");
        Assert(!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(control)), control.Name + " needs an accessible name");
    }

    private static void AssertIconToggle(CheckBox toggle, string checkedKey, string uncheckedKey)
    {
        toggle.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
        toggle.UpdateLayout();
        Assert(ReferenceEquals(toggle.Content, Application.Current.FindResource(uncheckedKey)), "Unchecked icon");
        AssertMaterialIcon(toggle);
        toggle.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        toggle.UpdateLayout();
        Assert(ReferenceEquals(toggle.Content, Application.Current.FindResource(checkedKey)), "Checked icon");
        AssertMaterialIcon(toggle);
    }

    private static async Task WaitAsync(Func<bool> condition, Func<string> error)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (!string.IsNullOrEmpty(error())) { throw new InvalidOperationException(error()); }
            if (timer.Elapsed.TotalSeconds > 30) { throw new TimeoutException("Playback check timed out."); }
            await Task.Delay(20);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }

    private static void SaveScreenshot(Window window, string path)
    {
        window.UpdateLayout();
        // Render the window directly: VisualBrush auto-bounds can include off-screen
        // scroll content and distort the screenshot after resizing a window.
        var size = new Size(Math.Ceiling(window.ActualWidth), Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(size));
        }
        bitmap.Render(visual);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
