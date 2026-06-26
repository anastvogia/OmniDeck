using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Sliders.Converters;

/// <summary>
/// Converts a boolean to a status dot color (green = true, gray = false).
/// </summary>
public class BoolToStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isActive = value is true;
        return isActive
            ? new SolidColorBrush(Color.FromRgb(16, 185, 129))   // Emerald green
            : new SolidColorBrush(Color.FromRgb(100, 116, 139));  // Slate gray
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a float (0.0–1.0) to a proportional width.
/// Parameter should be the max width (e.g., "300").
/// </summary>
public class ValueToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float v && parameter is string maxStr && double.TryParse(maxStr, CultureInfo.InvariantCulture, out double max))
            return (double)(v * max);
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a float volume level to a solid accent color brush.
/// </summary>
public class ValueToBarBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var accentColor = Color.FromRgb(37, 99, 235);   // Blue 600
        return new SolidColorBrush(accentColor);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// bool → Visibility. True = Visible, False = Collapsed.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Inverted bool → Visibility. True = Collapsed, False = Visible.
/// </summary>
public class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// bool → "Window Active" / "Window Inactive" text.
/// </summary>
public class BoolToActiveTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Window Active" : "Window Inactive";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns Visibility.Collapsed for null/empty strings, Visible otherwise.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
