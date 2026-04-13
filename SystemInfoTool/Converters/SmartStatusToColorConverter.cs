using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

using SystemInfoTool.Models;

namespace SystemInfoTool.Converters;

/// <summary>
/// Converts a <see cref="SmartStatus"/> value to a colour-coded <see cref="Brush"/>:
/// Good → green, Caution → amber, Bad → red, Unavailable → grey.
/// </summary>
[ValueConversion(typeof(SmartStatus), typeof(System.Windows.Media.Brush))]
public sealed class SmartStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is SmartStatus status
            ? status switch
            {
                SmartStatus.Good        => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0x7C, 0x10)),  // green
                SmartStatus.Caution     => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCA, 0x50, 0x10)),  // amber
                SmartStatus.Bad         => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C)),  // red
                SmartStatus.Unavailable => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),  // grey
                _                       => new SolidColorBrush(System.Windows.Media.Colors.Gray)
            }
            : (object)new SolidColorBrush(System.Windows.Media.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
