using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MAIS.Core.Models;

namespace MAIS.Sidebar.Converters;

/// <summary>Converts a <see cref="ModuleStatus"/> to its indicator <see cref="Brush"/>.</summary>
[ValueConversion(typeof(ModuleStatus), typeof(System.Windows.Media.Brush))]
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ModuleStatus status) return System.Windows.Media.Brushes.Gray;

        return status switch
        {
            ModuleStatus.Running   => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99)), // green
            ModuleStatus.Degraded  => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24)), // amber
            ModuleStatus.Faulted   => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71)), // red
            ModuleStatus.Starting  => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0xA5, 0xFA)), // blue
            ModuleStatus.Stopping  => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0xA5, 0xFA)), // blue
            ModuleStatus.Stopped   => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x55, 0x68)), // slate
            _                      => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x80))  // gray
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a <see cref="ModuleStatus"/> to a pulsing animation visibility —
/// shown only for transient states (Starting, Stopping).
/// </summary>
[ValueConversion(typeof(ModuleStatus), typeof(Visibility))]
public sealed class StatusToPulseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ModuleStatus status &&
            (status == ModuleStatus.Starting || status == ModuleStatus.Stopping))
            return Visibility.Visible;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a boolean connection state to a status dot <see cref="Brush"/>.</summary>
[ValueConversion(typeof(bool), typeof(System.Windows.Media.Brush))]
public sealed class ConnectionToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool connected)
            return connected
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99))  // green
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71));  // red

        return System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Inverts a boolean value.</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>Returns <see cref="Visibility.Visible"/> when value is true, else Collapsed.</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns <see cref="Visibility.Collapsed"/> when value is true (inverse).</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns <see cref="Visibility.Collapsed"/> when the string value is null or empty.</summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class NullOrEmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
