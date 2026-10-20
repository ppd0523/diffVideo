using System.Windows;
using System.Windows.Controls;

namespace DiffVideo.App.Controls;

/// <summary>Stacks children in equal-height rows from the top, each row no taller than MaxRowHeight.</summary>
public sealed class CappedRowsPanel : Panel
{
    public static readonly DependencyProperty MaxRowHeightProperty = DependencyProperty.Register(
        nameof(MaxRowHeight), typeof(double), typeof(CappedRowsPanel),
        new FrameworkPropertyMetadata(40.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MaxRowHeight
    {
        get => (double)GetValue(MaxRowHeightProperty);
        set => SetValue(MaxRowHeightProperty, value);
    }

    private double RowHeight(double available) =>
        InternalChildren.Count == 0 ? 0 : Math.Min(MaxRowHeight, available / InternalChildren.Count);

    protected override Size MeasureOverride(Size availableSize)
    {
        var rowHeight = RowHeight(availableSize.Height);
        var width = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(availableSize.Width, rowHeight));
            width = Math.Max(width, child.DesiredSize.Width);
        }
        return new Size(width, rowHeight * InternalChildren.Count);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rowHeight = RowHeight(finalSize.Height);
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Arrange(new Rect(0, i * rowHeight, finalSize.Width, rowHeight));
        }
        return finalSize;
    }
}
