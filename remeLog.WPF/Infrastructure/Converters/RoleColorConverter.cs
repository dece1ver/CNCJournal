using remeLog.Infrastructure.Types;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace remeLog.Infrastructure.Converters
{
    public class RoleColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is User currentRole && parameter is string roleParameter)
        {
            if (Enum.TryParse(roleParameter, out User comparedRole))
            {
                return currentRole == comparedRole
                    ? ThemeBrushes.Text
                    : ThemeBrushes.Text3;
            }
        }
        return ThemeBrushes.Text3;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
}
