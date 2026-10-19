using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Models
{
    public class VerevkinPart
    {
        public VerevkinPart(string partName, string order, double finishedCount)
        {
            PartName = partName;
            Order = order;
            FinishedCount = finishedCount;
        }

        public string PartName { get; set; }
        public string Order { get; set; }
        public double FinishedCount { get; set; }
        public TimeSpan MachineTime1 { get; set; } = TimeSpan.Zero;
        public TimeSpan MachineTime2 { get; set; } = TimeSpan.Zero;
        public TimeSpan MachineTime3 { get; set; } = TimeSpan.Zero;
        public TimeSpan MachineTime4 { get; set; } = TimeSpan.Zero;
        public TimeSpan MachineTime5 { get; set; } = TimeSpan.Zero;
    }
}
