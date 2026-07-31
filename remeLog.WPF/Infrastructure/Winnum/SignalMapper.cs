using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Infrastructure.Winnum
{
    public static class SignalMappers
    {
        public static readonly Func<string, string> Bool =
            v => v == "1" ? "Да" : "Нет";

        public static readonly Func<string, string> NcModeFanuc =
            v => v switch
            {
                "0" => "MDI",
                "1" => "MEMORY",
                "3" => "EDIT",
                "4" => "HANDLE",
                "5" => "JOG",
                "33" => "REMOTE",
                "133" => "REF",
                _ => v
            };

        public static readonly Func<string, string> NcModeSiemens =
            v => v switch
            {
                "0" => "JOG",
                "1" => "MDA",
                "2" => "AUTO",
                _ => v
            };

        public static Func<string, string>? GetNcModeMapper(string machineType) =>
            machineType switch
            {
                "Fanuc" => NcModeFanuc,
                "Siemens" => NcModeSiemens,
                _ => null
            };
    }
}
