// Converters/OwnToStripConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LumaChat.Converters
{
    public class OwnToStripConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool own = value is bool b && b;
            return own ? new SolidColorBrush(Color.FromRgb(143, 255, 226)) : new SolidColorBrush(Color.FromRgb(117, 165, 255));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}