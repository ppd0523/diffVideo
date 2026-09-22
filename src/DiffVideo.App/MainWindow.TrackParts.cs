using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DiffVideo.App;

/// <summary>
/// Locates the per-track controls that used to have fixed names. Tracks are generated from a
/// collection now, so they are addressed by row index instead.
/// </summary>
public partial class MainWindow
{
    /// <summary>Row index of the audio track: it always sits below every video row.</summary>
    internal int AudioRowIndex => ViewModel?.Videos.Count ?? 0;

    /// <summary>The generated container, which carries Canvas.Left/Top and Panel.ZIndex.</summary>
    private static FrameworkElement? ContainerAt(ItemsControl items, int index)
    {
        if (index < 0 || index >= items.Items.Count) { return null; }
        items.UpdateLayout();
        return items.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
    }

    /// <summary>
    /// The generated container for one preview overlay. Panel.ZIndex belongs here, on the direct
    /// child of the canvas, not on the border inside the template.
    /// </summary>
    internal FrameworkElement? TrackOverlayContainer(int index) => ContainerAt(PreviewOverlays, index);

    /// <summary>The bordered rectangle drawn over one track in the preview.</summary>
    internal Border? TrackOverlayBorder(int index) => Descendant<Border>(ContainerAt(PreviewOverlays, index));

    internal FrameworkElement? TrackNameRow(int index) => ContainerAt(TimelineTrackNames, index);

    internal Canvas? TrackClipCanvas(int index) => Descendant<Canvas>(ContainerAt(TimelineTrackClips, index));

    internal CheckBox? TrackAudioToggle(int index) => Descendant<CheckBox>(TrackNameRow(index));

    internal TextBlock? TrackFileName(int index) =>
        Descendant<TextBlock>(TrackNameRow(index), text => text.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path == "DisplayName");

    internal TextBlock? TrackStartLabel(int index) =>
        Descendant<TextBlock>(ContainerAt(TimelineTrackClips, index), text => Equals(text.Tag, "StartLabel"));

    internal StackPanel? TrackStartEditor(int index) =>
        Descendant<StackPanel>(ContainerAt(TimelineTrackClips, index), panel => panel.Children.Count > 0 && panel.Children[0] is TextBox);

    internal TextBox? TrackStartInput(int index) => Descendant<TextBox>(ContainerAt(TimelineTrackClips, index));

    private static T? Descendant<T>(DependencyObject? root, Func<T, bool>? match = null) where T : DependencyObject
    {
        if (root is null) { return null; }
        for (var child = 0; child < VisualTreeHelper.GetChildrenCount(root); child++)
        {
            var node = VisualTreeHelper.GetChild(root, child);
            if (node is T candidate && (match is null || match(candidate))) { return candidate; }
            if (Descendant(node, match) is { } found) { return found; }
        }

        return null;
    }
}
