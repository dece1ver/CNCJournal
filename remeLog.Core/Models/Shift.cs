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
            Type = name.ToLower() switch
            {
                "день" => ShiftType.Day,
                "ночь" => ShiftType.Night,
                "все смены" => ShiftType.All,
                _ => throw new ArgumentException("Некорректный тип смены."),
            };
        }

        public ShiftType Type { get; set; }

        public readonly string Name => Type switch
        {
            ShiftType.All => "Все смены",
            ShiftType.Day => "День",
            ShiftType.Night => "Ночь",
            _ => throw new ArgumentException("Некорректный тип смены."),
        };

        public readonly string FilterText => Type switch
        {
            ShiftType.All => "",
            ShiftType.Day => "День",
            ShiftType.Night => "Ночь",
            _ => throw new ArgumentException("Некорректный тип смены."),
        };

        public readonly int Minutes => Type switch
        {
            ShiftType.All => 1290,
            ShiftType.Day => 660,
            ShiftType.Night => 630,
            _ => throw new ArgumentException("Некорректный тип смены."),
        };

    }
}
