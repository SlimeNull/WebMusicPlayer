using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WebMusicPlayer.ValueConverters
{
    public class ValueEqualsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is null ||
                values.Length < 2)
            {
                return false;
            }

            var firstValue = values[0];
            for (int i = 1; i < values.Length; i++)
            {
                if (!Equals(firstValue, values[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
