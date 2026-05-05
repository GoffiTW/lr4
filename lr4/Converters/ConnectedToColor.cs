// Converters/ConnectedToColor.cs
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LumaChat.Converters
{
    public class ConnectedToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool connected = value is bool && (bool)value;
            return connected ? new SolidColorBrush(Color.FromRgb(45, 226, 176)) : new SolidColorBrush(Color.FromRgb(255, 104, 121));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}