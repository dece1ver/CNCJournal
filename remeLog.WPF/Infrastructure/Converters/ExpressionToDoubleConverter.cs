using NCalc;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    class ExpressionToDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && new Expression(s).Evaluate() is double d) return d;
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is string s && new Expression(s.Replace(",", ".")).Evaluate() is { } v)
                {
                    return v switch
                    {
                        double => v,
                        _ => System.Convert.ToDouble(v),
                    };
                }
                return value;

            }
            catch
            {
                return value;
            }
        }
    }
}
