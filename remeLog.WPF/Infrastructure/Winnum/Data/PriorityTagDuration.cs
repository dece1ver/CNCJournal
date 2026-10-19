using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static remeLog.Infrastructure.Winnum.Types;

namespace remeLog.Infrastructure.Winnum.Data
{
    public class PriorityTagDuration
    {
        public string SerialNumber { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string Model { get; set; } = default!;
        public string Tag { get; set; } = default!;
        public string? Program { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public double Duration { get; set; }
        public TagId TagOid { get; set; } = default!;
        public string TimeDataRaw { get; set; } = default!;

        public (long startTicks, long endTicks) TimeData
        {
            get
            {
                var parts = TimeDataRaw.Split(',');
                return (long.Parse(parts[0]), long.Parse(parts[1]));
            }
        }
    }
}
