using remeLog.Infrastructure.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Models
{
    public struct Shift
    {
        public Shift(ShiftType type)
        {
            Type = type;
        }

        public Shift(string name)
        {
            Type = name switch
            {
                _ when name.Equals(Shifts.Day, StringComparison.OrdinalIgnoreCase) => ShiftType.Day,
                _ when name.Equals(Shifts.Night, StringComparison.OrdinalIgnoreCase) => ShiftType.Night,
                _ when name.Equals(Shifts.All, StringComparison.OrdinalIgnoreCase) => ShiftType.All,
                _ => throw new ArgumentException("Некорректный тип смены."),
            };
        }

        public ShiftType Type { get; set; }

        public readonly string Name => Type switch
        {
            ShiftType.All => Shifts.All,
            ShiftType.Day => Shifts.Day,
            ShiftType.Night => Shifts.Night,
            _ => throw new ArgumentException("Некорректный тип смены."),
        };

        public readonly string FilterText => Type switch
        {
            ShiftType.All => "",
            ShiftType.Day => Shifts.Day,
            ShiftType.Night => Shifts.Night,
            _ => throw new ArgumentException("Некорректный тип смены."),
        };

        public readonly int Minutes => Type switch
        {
            ShiftType.All => Shifts.AllMinutes,
            ShiftType.Day => Shifts.DayMinutes,
            ShiftType.Night => Shifts.NightMinutes,
            _ => throw new ArgumentException("Некорректный тип смены."),
        };

    }
}
