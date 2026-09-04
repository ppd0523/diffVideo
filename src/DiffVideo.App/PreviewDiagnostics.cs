using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiffVideo.App.Services;
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
        Window window = mode is "checks" or "ui" or "features" or "roi" ? new MainWindow() : new Window
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
        vm.OutputDurationSeconds = 12;
        vm.Video2.StartSeconds = 1.234;
        vm.Audio.StartSeconds = 0.1236;
        Assert(Math.Abs(vm.Audio.StartSeconds - 0.124) < 0.000001, "MP3 1ms snapping");
        checks.Add("MP3 1ms snapping");
        vm.PlayheadSeconds = 3;
        await vm.SetPlaybackStartAsync();
        Assert(vm.CurrentPlaybackState == PlaybackState.Stopped && vm.PlaybackStartSeconds == 3, "Start marker");
        await vm.StartPlaybackAsync();
        Assert(vm.IsPreviewLoading, "Initial loading indication");
        Assert(vm.CanStop && !vm.IsEditingEnabled && vm.CanStartExport, "Playback control locks");
        await WaitAsync(() => vm.PlayheadSeconds > 3.3, () => vm.ErrorMessage);
        await vm.PausePlaybackAsync();
        var paused = vm.PlayheadSeconds;
        await Task.Delay(180);
        Assert(vm.PlayheadSeconds == paused && !vm.IsPreviewLoading, "Pause does not auto-resume");
        await vm.StepFrameAsync(1);
        Assert(Math.Abs(vm.PlayheadSeconds - paused - 1d / vm.OutputFps) < 0.00001, "Frame step");
        await vm.StopPlaybackAsync();
        Assert(vm.PlayheadSeconds == 3, "Stop returns to marker");
        checks.AddRange(["Loading indication", "Individual playback progresses", "Editing locks / export remains enabled", "Pause", "Frame step", "Stop marker"]);

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
        vm.PlayheadSeconds = 0;
        await vm.SetPlaybackStartAsync();
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
            await vm.SetPlaybackStartAsync();
            await vm.RefreshStillPreviewAsync();
            await CaptureAsync("loaded", 1440, 900);
            await CaptureAsync("loaded-minimum", 1120, 720);

            foreach (var button in new[] { window.AboutButton, window.OpenFilesButton, window.ExportButton, window.CancelExportButton,
                window.PlayButton, window.PauseButton, window.StopButton, window.PreviousFrameButton, window.NextFrameButton,
                window.SetStartButton, window.EditRoiButton, window.ResetRoiButton, window.SendBackwardButton, window.BringForwardButton })
            {
                AssertMaterialIcon(button);
                Assert(button.ActualWidth >= 40 || button.Visibility == Visibility.Collapsed, "Icon button hit target");
                var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
                Assert(!string.IsNullOrWhiteSpace(peer.GetName()), "Icon button accessible name");
                Assert(ToolTipService.GetShowOnDisabled(button), "Disabled icon button tooltip");
            }
            checks.Add("Material action icons / accessible names / tooltips / hit targets");

            AssertIconToggle(window.Video1AudioCheckBox, "Icon.VolumeUp", "Icon.VolumeOff");
            AssertIconToggle(window.Mp3AudioCheckBox, "Icon.VolumeUp", "Icon.VolumeOff");
            AssertIconToggle(window.AspectLockCheckBox, "Icon.Lock", "Icon.LockOpen");
            Assert(vm.Video1.IncludeAudio && vm.Audio.IncludeAudio && vm.SelectedVideo.AspectRatioLocked, "Icon toggle bindings restored");
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

            foreach (var combo in new[] { window.FpsCombo, window.QualityCombo, window.FitCombo })
            {
                combo.BringIntoView();
                combo.IsDropDownOpen = true;
                await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var popup = (System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup", combo);
                Assert(popup.IsOpen && popup.Child is FrameworkElement { ActualWidth: > 0 }, "Combo popup template");
                var item = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0);
                Assert(item is not null && item.ActualHeight >= 28, "Combo items rendered");
                combo.IsDropDownOpen = false;
            }
            checks.Add("FPS / quality / fit popup templates");
            var originalFps = vm.OutputFps;
            window.FpsCombo.SelectedIndex = 0;
            Assert(Equals(window.FpsCombo.SelectedValue, vm.OutputFps), "FPS selection binding");
            vm.OutputFps = originalFps;
            checks.Add("FPS selection updates model");

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
        foreach (var control in new FrameworkElement[] { window.PlayButton, window.PauseButton, window.StopButton, window.PreviousFrameButton, window.NextFrameButton, window.SetStartButton, window.ExportButton, window.PlayheadSlider })
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
            // Exercise the same left-click handlers as the actual player, before the right-drag gesture.
            overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
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
            Assert(track.ToModel().Roi == PreviewRoiSelection.Map(before, from.X, from.Y, to.X, to.Y), "Drop maps the selected rectangle to the original source");
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
        window.FpsCombo.Focus();
        Assert(!RaiseKey(window, Key.Space).Handled && !vm.IsPlaying, "Space stays in dropdowns");
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
