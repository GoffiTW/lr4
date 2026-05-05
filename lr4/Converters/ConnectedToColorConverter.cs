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
            return (value is bool b && b)
                ? new SolidColorBrush(Color.FromRgb(45, 226, 176))   // зелёный
                : new SolidColorBrush(Color.FromRgb(255, 104, 121)); // красный
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}