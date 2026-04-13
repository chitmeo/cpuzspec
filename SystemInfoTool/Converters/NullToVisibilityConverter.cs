using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SystemInfoTool.Converters;

/// <summary>
/// Converts a nullable reference to <see cref="Visibility"/>.
/// Non-null → <see cref="Visibility.Visible"/>;
/// null → <see cref="Visibility.Collapsed"/> (or Visible when Invert=true).
/// Used to show/hide error panels bound to nullable <c>ErrorMessage</c> strings.
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>When <c>true</c>, null→Visible and non-null→Collapsed.</summary>
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasValue = value is not null && (value is not string s || s.Length > 0);
        if (Invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
