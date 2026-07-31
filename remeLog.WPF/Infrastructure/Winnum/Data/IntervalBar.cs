using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Infrastructure.Winnum.Data
{
    public class IntervalBar
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        public double Yi => 0;
        public double Yj => 1;

        public string Tooltip =>
            $"С {Start:HH:mm:ss} по {End:HH:mm:ss}\n" +
            $"Длительность: {(End - Start):hh\\:mm\\:ss}";
    }
}
