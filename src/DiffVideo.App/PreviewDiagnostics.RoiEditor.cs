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
    private static async Task CheckRoiEditorAsync(MainWindow window, string[] files, string report)
    {
        var vm = window.ViewModel!;
        var checks = new List<string>();
        Assert(window.Title == "DiffVideo" && typeof(MainWindow).Assembly.GetName().Name == "DiffVideo", "Renamed window and assembly");
        await vm.LoadFilesAsync(files);
        var snapshot = vm.BuildComposition();
        var png = await vm.GetSourceFramePngAsync(vm.Videos[0]);
        var media = vm.Videos[0].Media!;
        var original = new PixelRect(100, 100, 600, 400);
        var editor = new RoiEditorWindow(png, media.DisplayWidth, media.DisplayHeight, original) { Owner = window };
        editor.Show(); editor.UpdateLayout();
        try
        {
            Assert(editor.GridVisible && editor.PixelGrid.Visibility == Visibility.Visible, "Grid defaults on in a new process");
            AssertMaterialIcon(editor.GridToggleButton);
            Assert(editor.HandleCanvas.Children.Count == 8, "Eight visible resize handles");
            Assert(editor.XInput.Text == "100" && editor.WidthInput.Text == "600", "Numeric fields reflect source pixels");
            checks.Add("DiffVideo branding / default grid / Material toggle / eight handles / numeric initialization");

            var cursor = editor.FromSource(720, 500);
            editor.UpdateCoordinateTip(cursor);
            Assert(editor.CrosshairOverlay.Visibility == Visibility.Visible, "Visible crosshair");
            var mapped = editor.ImageHost.TranslatePoint(cursor, editor.CrosshairOverlay);
            Assert(Math.Abs(editor.VerticalGuide.X1 - mapped.X) < 0.001 && editor.VerticalGuide.Y1 == 0, "Vertical guide reaches top ruler");
            Assert(Math.Abs(editor.HorizontalGuide.Y1 - mapped.Y) < 0.001 && editor.HorizontalGuide.X1 == 0, "Horizontal guide reaches left ruler");
            SaveScreenshot(editor, Path.ChangeExtension(report, ".grid-on.png"));
            editor.GridToggleButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(!editor.GridVisible && editor.PixelGrid.Visibility == Visibility.Collapsed && editor.SelectedRoi == original, "Grid toggle changes display only");
            SaveScreenshot(editor, Path.ChangeExtension(report, ".grid-off.png"));
            editor.UpdateCoordinateTip(new Point(-1, -1));
            Assert(editor.CrosshairOverlay.Visibility == Visibility.Collapsed && editor.SourceCoordinateTip.Coordinates is null, "No guides outside image");
            checks.Add("100px source grid / crosshair-to-ruler alignment / outside-image hiding / display-only toggle");

            var center = editor.FromSource(400, 300);
            Assert(editor.BeginRoiDrag(center), "Begin move");
            editor.UpdateRoiDrag(editor.FromSource(500, 350));
            Assert(editor.SelectedRoi == new PixelRect(200, 150, 600, 400) && editor.XInput.Text == "200", "Move and numeric live sync");
            editor.CompleteRoiDrag(editor.FromSource(500, 350));
            Assert(!editor.IsDragging && !editor.ImageHost.IsMouseCaptured, "Drop releases capture");
            Assert(vm.BuildComposition() == snapshot, "Popup does not modify main composition before Apply");
            checks.Add("ROI interior move / source-pixel precision / live numeric sync / drop / staged main composition");

            (RoiEdges Edges, double X, double Y)[] handles =
            [
                (RoiEdges.Left, 100, 300), (RoiEdges.Right, 700, 300), (RoiEdges.Top, 400, 100), (RoiEdges.Bottom, 400, 500),
                (RoiEdges.Left | RoiEdges.Top, 100, 100), (RoiEdges.Right | RoiEdges.Top, 700, 100),
                (RoiEdges.Left | RoiEdges.Bottom, 100, 500), (RoiEdges.Right | RoiEdges.Bottom, 700, 500)
            ];
            foreach (var handle in handles)
            {
                editor.SetRoi(original);
                Assert(editor.BeginRoiDrag(editor.FromSource(handle.X, handle.Y)), "Begin resize");
                editor.CompleteRoiDrag(editor.FromSource(handle.X + 40, handle.Y + 30));
                Assert(editor.SelectedRoi == RoiEditing.Resize(original, handle.Edges, 40, 30, media.DisplayWidth, media.DisplayHeight), "Eight-way resize: " + handle.Edges);
            }
            checks.Add("All four edge and four corner resize operations preserve opposite anchors");

            editor.SetRoi(original);
            Assert(editor.BeginRoiDrag(editor.FromSource(1700, 900)), "Begin new ROI outside old rectangle");
            editor.CompleteRoiDrag(editor.FromSource(1400, 700));
            Assert(editor.SelectedRoi == new PixelRect(1400, 700, 300, 200), "Reverse drawing replaces previous ROI");
            var beforeTiny = editor.SelectedRoi;
            Assert(editor.BeginRoiDrag(editor.FromSource(800, 300)), "Tiny outside click");
            editor.CompleteRoiDrag(editor.FromSource(800, 300));
            Assert(editor.SelectedRoi == beforeTiny, "Outside click does not destroy previous ROI");
            Assert(!editor.BeginRoiDrag(new Point(-1, 10)), "Outside image cannot draw");
            checks.Add("Outside-ROI redraw / reverse draw / tiny-click preservation / letterbox rejection");

            editor.SetRoi(original);
            Assert(editor.BeginRoiDrag(editor.FromSource(400, 300)), "Escape move begins");
            editor.UpdateRoiDrag(editor.FromSource(800, 500));
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            editor.RaiseEvent(escape);
            Assert(escape.Handled && editor.IsVisible && editor.SelectedRoi == original && !editor.IsDragging, "Esc restores drag without closing dialog");
            Assert(editor.BeginRoiDrag(editor.FromSource(100, 100)), "Capture-loss resize begins");
            editor.UpdateRoiDrag(editor.FromSource(150, 150));
            editor.ImageHost.ReleaseMouseCapture();
            Assert(editor.SelectedRoi == original && !editor.IsDragging, "Capture loss restores original ROI");
            Assert(editor.BeginRoiDrag(editor.FromSource(400, 300)), "Resize-window gesture begins");
            editor.UpdateRoiDrag(editor.FromSource(450, 350));
            editor.Width = 880;
            await editor.Dispatcher.InvokeAsync(editor.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert(editor.SelectedRoi == original && !editor.IsDragging, "Window resizing cancels old-coordinate gesture");
            checks.Add("Escape / mouse capture loss / window resize roll back the current gesture");

            editor.XInput.Focus(); editor.XInput.Text = "250";
            Assert(editor.SelectedRoi.X == 100, "Typing is not committed before Enter or focus loss");
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            editor.XInput.RaiseEvent(enter);
            Assert(enter.Handled && editor.SelectedRoi.X == 250 && editor.IsVisible, "Enter commits numeric field without applying dialog");
            editor.YInput.Focus(); editor.YInput.Text = "300"; editor.WidthInput.Focus();
            Assert(editor.SelectedRoi.Y == 300 && editor.SelectedRoi.Width == 600, "Focus loss commits position without changing dimensions");
            foreach (var invalid in new[] { "", "not-a-number", "1.5" })
            {
                editor.WidthInput.Text = invalid; editor.CommitNumber(editor.WidthInput);
                Assert(editor.SelectedRoi.Width == 600 && editor.WidthInput.Text == "600", "Invalid numeric input restores prior value");
            }
            editor.XInput.Text = "999999999999999999999"; editor.CommitNumber(editor.XInput);
            Assert(editor.SelectedRoi.X == media.DisplayWidth - 600 && editor.SelectedRoi.Width == 600, "Overflow X clamps, preserving width");
            editor.WidthInput.Text = "-10"; editor.CommitNumber(editor.WidthInput);
            Assert(editor.SelectedRoi.Width == 2, "Minimum width enforced");
            checks.Add("Enter / focus-loss commit / no partial typing / invalid input recovery / boundary clamp / 2px minimum");

            editor.FullFrameButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert(editor.SelectedRoi == PixelRect.FullFrame(media), "Full-frame reset");
            Assert(editor.BeginRoiDrag(editor.FromSource(0, 0)), "Full-frame ROI can be resized without outside space");
            editor.CompleteRoiDrag(editor.FromSource(300, 200));
            Assert(editor.SelectedRoi.X == 300 && editor.SelectedRoi.Y == 200, "Full-frame edge resizing creates outside space");
            editor.Width = 640; editor.Height = 480;
            await editor.Dispatcher.InvokeAsync(editor.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            foreach (var input in new[] { editor.XInput, editor.YInput, editor.WidthInput, editor.HeightInput })
            {
                var bounds = input.TransformToAncestor(editor).TransformBounds(new Rect(input.RenderSize));
                Assert(input.ActualWidth > 60 && input.ActualHeight >= 28 && bounds.Bottom < editor.ActualHeight, "Minimum-window input bounds");
            }
            Assert(editor.ImageHost.ActualHeight > 100, "Minimum-window image remains usable");
            SaveScreenshot(editor, Path.ChangeExtension(report, ".minimum.png"));
            checks.Add("Full-frame recovery / numeric and image layout at 640x480");
        }
        finally { editor.Close(); }

        var reopened = new RoiEditorWindow(png, media.DisplayWidth, media.DisplayHeight, original) { Owner = window };
        reopened.Show(); reopened.UpdateLayout();
        Assert(!reopened.GridVisible && reopened.PixelGrid.Visibility == Visibility.Collapsed, "Grid preference survives reopening");
        reopened.GridToggleButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        reopened.Close();
        Assert(vm.BuildComposition() == snapshot, "Closing popup without Apply preserves main ROI");
        checks.Add("Grid preference retained for app session / unconfirmed ROI never changes main composition");
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Success = true, Checks = checks }, JsonOptions));
    }
}
