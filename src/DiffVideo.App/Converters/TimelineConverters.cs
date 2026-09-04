using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiffVideo.App.Converters;

public sealed class ClipHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Math.Max(26, (double)value - 4);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => DependencyProperty.UnsetValue;
}

public sealed class TimelinePositionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryValues(values, out var seconds, out var duration, out var width) || duration <= 0)
        {
            return 0d;
        }

        return Math.Clamp(seconds / duration * width, 0, width);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => DependencyProperty.UnsetValue).ToArray();

    private static bool TryValues(object[] values, out double seconds, out double duration, out double width)
    {
        seconds = duration = width = 0;
        if (values.Length < 3)
        {
            return false;
        }

        seconds = System.Convert.ToDouble(values[0], CultureInfo.InvariantCulture);
        duration = System.Convert.ToDouble(values[1], CultureInfo.InvariantCulture);
        width = System.Convert.ToDouble(values[2], CultureInfo.InvariantCulture);
        return !double.IsNaN(width) && width > 0;
    }
}

public sealed class TimelineWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3)
        {
            return 12d;
        }

        var mediaDuration = System.Convert.ToDouble(values[0], CultureInfo.InvariantCulture);
        var outputDuration = System.Convert.ToDouble(values[1], CultureInfo.InvariantCulture);
        var width = System.Convert.ToDouble(values[2], CultureInfo.InvariantCulture);
        if (outputDuration <= 0 || double.IsNaN(width))
        {
            return 12d;
        }

        return Math.Max(12, mediaDuration / outputDuration * width);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => DependencyProperty.UnsetValue).ToArray();
}
