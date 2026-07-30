using remeLog.Core;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace remeLog.Models
{
    public class CombinedParts : ObservableObject
    {
        public enum ReportState
        {
            Exist, NotExist, Partial
        }

        public CombinedParts(string machine)
        {
            _Machine = machine;
        }

        public CombinedParts(string machine, DateTime fromDate, DateTime toDate)
        {
            _Machine = machine;
            _FromDate = fromDate;
            _ToDate = toDate;
        }

        public CombinedParts(CombinedParts cp)
        {
            _Machine = cp.Machine;
            _FromDate = cp.FromDate;
            _ToDate = cp.ToDate;
            _ToDate = cp.ToDate;
            _Parts = new();
            foreach (var part in cp.Parts)
            {
                _Parts.Add(new Part(part));
            }
        }


        private string _Machine;
        public string Machine
        {
            get => _Machine;
            set => Set(ref _Machine, value);
        }



        private DateTime _FromDate;
        public DateTime FromDate
        {
            get => _FromDate;
            set => Set(ref _FromDate, value);
        }

        private DateTime _ToDate;
        public DateTime ToDate
        {
            get => _ToDate;
            set => Set(ref _ToDate, value);
        }

        private ReportState _IsReportExist = ReportState.NotExist;
        /// <summary> Существует ли отчет за смену </summary>
        public ReportState IsReportExist
        {
            get => _IsReportExist;
            set => Set(ref _IsReportExist, value);
        }


        private bool _IsReportChecked;
        /// <summary> Проверен ли отчет </summary>
        public bool IsReportChecked
        {
            get => _IsReportChecked;
            set => Set(ref _IsReportChecked, value);
        }


        private ObservableCollection<Part> _Parts = new();
        public ObservableCollection<Part> Parts
        {
            get => _Parts;
            set 
            {
                if (Set(ref _Parts, value))
                {
                    OnPropertyChanged(nameof(TotalShifts));
                    OnPropertyChanged(nameof(WorkedShifts));
                    OnPropertyChanged(nameof(AverageSetupRatio));
                    OnPropertyChanged(nameof(AverageProductionRatio));
                    OnPropertyChanged(nameof(SetupTimeRatio));
                    OnPropertyChanged(nameof(ProductionTimeRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(UnspecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatioNeedAttention));
                    OnPropertyChanged(nameof(UnspecifiedDowntimesRatioNeedAttention));
                    OnPropertyChanged(nameof(AverageSetupRatioNeedAttention));
                    OnPropertyChanged(nameof(SetupTimeRatioNeedAttention));
                    OnPropertyChanged(nameof(AverageProductionRatioNeedAttention));
                    OnPropertyChanged(nameof(ProductionTimeRatioNeedAttention));
                    OnPropertyChanged(nameof(FinishedCount));
                    OnPropertyChanged(nameof(AllFinishedCount));
                    OnPropertyChanged(nameof(Orders));
                    OnPropertyChanged(nameof(ShiftsInfo));
                }
            }
        }

        public int TotalShifts => DomainSettings.GetWorkDaysBetween(FromDate, ToDate) * 2;
        public int WorkedShifts => Parts.Where(part => part.ShiftDate >= FromDate && part.ShiftDate <= ToDate)
                                        .Select(part => new { part.ShiftDate.Date, part.Shift })
                                        .Distinct().Count();

        public double ShiftsRatio => (double)WorkedShifts / TotalShifts * 100;

        public bool IsSingleShift => FromDate == ToDate;
        public bool IsSingleWorkingShift => IsSingleShift && !DomainSettings.Holidays.Contains(ToDate);
        public int Orders => Parts.GroupBy(p => p.Order).Count();
        public double AllFinishedCount => Parts.Sum(p => p.FinishedCount);
        public double FinishedCount => Parts.Where(p => p.Setup == 1).Sum(p => p.FinishedCount);
        public double AverageSetupRatio => Parts.AverageSetupRatio();
        public double AverageProductionRatio => Parts.AverageProductionRatio();
        public double SetupTimeRatio => Parts.SetupRatio();
        public double ProductionTimeRatio => Parts.ProductionRatio();
        public double SpecifiedDowntimesRatio => Parts.SpecifiedDowntimesRatio(new Shift(ShiftType.All));
        public double UnspecifiedDowntimesRatio => Parts.UnspecifiedDowntimesRatio(FromDate, ToDate, new Shift(ShiftType.All));

        public bool SpecifiedDowntimesRatioNeedAttention => SpecifiedDowntimesRatio is > 0.1 or < -0.1;
        public bool UnspecifiedDowntimesRatioNeedAttention => UnspecifiedDowntimesRatio is > 0.1 or < -0.1;

        public bool AverageSetupRatioNeedAttention => AverageSetupRatio is < 0.695 or > 1.2;
        public bool SetupTimeRatioNeedAttention => SetupTimeRatio is < 0.695 or > 1.2;
        public bool AverageProductionRatioNeedAttention => AverageProductionRatio is < 0.695 or > 1.2;
        public bool ProductionTimeRatioNeedAttention => ProductionTimeRatio is < 0.695 or > 1.2;

        public string ShiftsInfo { get
            {
                if (!IsSingleShift) return "";
                var d = Parts.Any(p => p.ShiftDate == ToDate && p.Shift == "День");
                var n = Parts.Any(p => p.ShiftDate == ToDate && p.Shift == "Ночь");
                switch ((d, n))
                {
                    case (true, true):
                        return "Работал день и ночь";
                    case (true, false):
                        return "Работал только день";
                    case (false, true):
                        return "Работал только ночь";
                    case (false, false):
                        return "Не работал";
                }
            }
        }
    }
}
