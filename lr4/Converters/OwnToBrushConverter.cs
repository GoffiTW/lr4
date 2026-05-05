// Converters/OwnToBrushConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LumaChat.Converters
{
    public class OwnToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool own = value is bool b && b;
            return own ? new SolidColorBrush(Color.FromRgb(28, 151, 132)) : new SolidColorBrush(Color.FromRgb(25, 39, 64));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}