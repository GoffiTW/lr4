// Converters/OwnToMarginConverter.cs
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LumaChat.Converters
{
    public class OwnToMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool own = value is bool b && b;
            return own ? new Thickness(60, 4, 12, 8) : new Thickness(12, 4, 60, 8);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}