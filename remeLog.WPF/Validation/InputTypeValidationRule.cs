using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace remeLog.Validation
{
    public class InputTypeValidationRule : ValidationRule
    {
        public Type? ExpectedType { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            string input = value?.ToString() ?? string.Empty;

            if (ExpectedType == null || string.IsNullOrWhiteSpace(input))
                return ValidationResult.ValidResult;

            try
            {
                TypeConverter converter = TypeDescriptor.GetConverter(ExpectedType);
                if (converter != null && converter.IsValid(input))
                    return ValidationResult.ValidResult;
            }
            catch { }

            return new ValidationResult(false, $"Ожидается значение типа: {ExpectedType.Name}");
        }
    }
}
