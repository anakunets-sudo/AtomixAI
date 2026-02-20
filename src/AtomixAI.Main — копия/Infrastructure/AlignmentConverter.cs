using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AtomixAI.Main.ViewModels;

namespace AtomixAI.Main.Infrastructure
{
    public class AlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MessageType type)
            {
                // Пользователь (справа), АИ (слева)
                return type == MessageType.User ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
            }
            return System.Windows.HorizontalAlignment.Left;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
