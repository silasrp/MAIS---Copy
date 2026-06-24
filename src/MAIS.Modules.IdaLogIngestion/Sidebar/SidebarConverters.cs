using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MAIS.Modules.IdaLogIngestion.Sidebar;

/// <summary>Converts bool to its logical inverse — used to disable buttons while processing.</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class NegatingConverter : IValueConverter
{
    public static readonly NegatingConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b;
}

/// <summary>Converts a non-empty string to Visible, empty/null to Collapsed.</summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class StringToVisibilityConverter : IValueConverter
{
    public static readonly StringToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
