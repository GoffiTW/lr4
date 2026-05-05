// Converters/ZeroToMessageConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace LumaChat.Converters
{
    public class ZeroToMessageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = value is int i ? i : 0;
            return count == 0 ? "Контакты не найдены" : "";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}