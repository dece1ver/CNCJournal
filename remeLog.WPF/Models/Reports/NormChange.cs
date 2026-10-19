using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Models.Reports
{
    public readonly struct NormChange
    {
        public string PartName { get; }
        public string Machine { get; }
        public int Setup { get; }
        public string ChangeType { get; }
        public double OldValue { get; }
        public double ExcludedOperationsTime { get; }
        public double NewValue { get; }
        public DateTime Date { get; }
        public bool IsInTotalUnique { get; }
        public bool IsInSerialList { get; }
        public string IncreaseReason { get; }

        public NormChange(string partName, string machine, int setup, string changeType,
                         double oldValue, double excludedOperationsTime, double newValue,
                         DateTime date, bool isInTotalUnique, bool isInSerialList, string increaseReason)
        {
            PartName = partName;
            Machine = machine;
            Setup = setup;
            ChangeType = changeType;
            OldValue = oldValue;
            ExcludedOperationsTime = excludedOperationsTime;
            NewValue = newValue;
            Date = date;
            IsInTotalUnique = isInTotalUnique;
            IsInSerialList = isInSerialList;
            IncreaseReason = increaseReason;
        }
    }

}
