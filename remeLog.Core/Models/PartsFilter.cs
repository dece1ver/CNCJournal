using remeLog.Infrastructure.Types;
using System;

namespace remeLog.Models
{
    /// <summary>
    /// Пункт фильтра деталей по серийности с отображаемым названием — по аналогии с <see cref="Shift"/>,
    /// чтобы биндиться в ComboBox через DisplayMemberPath="Name".
    /// </summary>
    public struct PartsFilter
    {
        public PartsFilter(PartsFilterType type)
        {
            Type = type;
        }

        public PartsFilterType Type { get; set; }

        public readonly string Name => Type switch
        {
            PartsFilterType.All => "Все",
            PartsFilterType.Serial => "Серийные",
            PartsFilterType.NonSerial => "Не серийные",
            _ => throw new ArgumentException("Некорректный тип фильтра серийности."),
        };
    }
}
