using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Markup;

namespace remeLog.Infrastructure.Extensions
{
    public class EnumBindingSourceExtension : MarkupExtension
    {
        private Type? _enumType;
        public Type EnumType
        {
            get { return this._enumType!; }
            set
            {
                if (value != this._enumType)
                {
                    if (null != value)
                    {
                        Type enumType = Nullable.GetUnderlyingType(value) ?? value;
                        if (!enumType.IsEnum)
                            throw new ArgumentException("Type must be an Enum.");
                    }
                    this._enumType = value!;
                }
            }
        }

        public object ExcludeValue { get; set; } = new();

        public EnumBindingSourceExtension() { }

        public EnumBindingSourceExtension(Type enumType)
        {
            this.EnumType = enumType;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (null == this._enumType)
                throw new InvalidOperationException("The EnumType must be specified.");

            var actualEnumType = Nullable.GetUnderlyingType(this._enumType) ?? this._enumType;

            // Преобразуем ExcludeValue, если это строка
            object? excludeValue = ExcludeValue;
            if (ExcludeValue is string excludeString)
            {
                excludeValue = Enum.Parse(actualEnumType, excludeString);
            }

            var enumValues = Enum.GetValues(actualEnumType)
                                 .Cast<object>()
                                 .Where(e => !Equals(e, excludeValue))
                                 .Select(enumValue => new EnumValueDescription
                                 {
                                     Value = enumValue,
                                     Display = GetDescription(enumValue)
                                 })
                                 .ToArray();

            return enumValues;
        }

        private static string GetDescription(object enumValue)
        {
            return enumValue.ToString() switch
            {
                "Viewer" => "Зритель",
                "Master" => "Мастер",
                "Engineer" => "Технолог",
                _ => enumValue.ToString() ?? string.Empty,
            };
        }
    }
}
