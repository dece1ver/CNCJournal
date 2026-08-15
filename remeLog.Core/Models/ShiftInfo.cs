using remeLog.Infrastructure.Types;
using System;

namespace remeLog.Models
{
    public class ShiftInfo
    {
        public ShiftInfo(int? id, DateTime shiftDate, ShiftType shiftType, string machine, string master, double unspecifiedDowntimes, string downtimesComment, string commonComment, bool isChecked, bool? giverWorkplaceCleaned, bool? giverFailures, bool? giverExtraneousNoises, bool? giverLiquidLeaks, bool? giverToolBreakage, double? giverСoolantСoncentration, bool? recieverWorkplaceCleaned, bool? recieverFailures, bool? recieverExtraneousNoises, bool? recieverLiquidLeaks, bool? recieverToolBreakage, double? recieverСoolantСoncentration)
        {
            Id = id;
            ShiftDate = shiftDate;
            Shift = new Shift(shiftType).Name;
            Machine = machine;
            Master = master;
            UnspecifiedDowntimes = unspecifiedDowntimes;
            DowntimesComment = downtimesComment;
            CommonComment = commonComment;
            IsChecked = isChecked;
            GiverWorkplaceCleaned = giverWorkplaceCleaned;
            GiverFailures = giverFailures;
            GiverExtraneousNoises = giverExtraneousNoises;
            GiverLiquidLeaks = giverLiquidLeaks;
            GiverToolBreakage = giverToolBreakage;
            GiverСoolantСoncentration = giverСoolantСoncentration;
            RecieverWorkplaceCleaned = recieverWorkplaceCleaned;
            RecieverFailures = recieverFailures;
            RecieverExtraneousNoises = recieverExtraneousNoises;
            RecieverLiquidLeaks = recieverLiquidLeaks;
            RecieverToolBreakage = recieverToolBreakage;
            RecieverСoolantСoncentration = recieverСoolantСoncentration;
        }
        public ShiftInfo(int? id, DateTime shiftDate, string shiftType, string machine, string master, double unspecifiedDowntimes, string downtimesComment, string commonComment, bool isChecked, bool? giverWorkplaceCleaned, bool? giverFailures, bool? giverExtraneousNoises, bool? giverLiquidLeaks, bool? giverToolBreakage, double? giverСoolantСoncentration, bool? recieverWorkplaceCleaned, bool? recieverFailures, bool? recieverExtraneousNoises, bool? recieverLiquidLeaks, bool? recieverToolBreakage, double? recieverСoolantСoncentration)
        {
            Id = id;
            ShiftDate = shiftDate;
            Shift = shiftType;
            Machine = machine;
            Master = master;
            UnspecifiedDowntimes = unspecifiedDowntimes;
            DowntimesComment = downtimesComment;
            CommonComment = commonComment;
            IsChecked = isChecked;
            GiverWorkplaceCleaned = giverWorkplaceCleaned;
            GiverFailures = giverFailures;
            GiverExtraneousNoises = giverExtraneousNoises;
            GiverLiquidLeaks = giverLiquidLeaks;
            GiverToolBreakage = giverToolBreakage;
            GiverСoolantСoncentration = giverСoolantСoncentration;
            RecieverWorkplaceCleaned = recieverWorkplaceCleaned;
            RecieverFailures = recieverFailures;
            RecieverExtraneousNoises = recieverExtraneousNoises;
            RecieverLiquidLeaks = recieverLiquidLeaks;
            RecieverToolBreakage = recieverToolBreakage;
            RecieverСoolantСoncentration = recieverСoolantСoncentration;
        }
        public ShiftInfo(DateTime shiftDate, ShiftType shiftType, string machine)
        {
            ShiftDate = shiftDate;
            Shift = new Shift(shiftType).Name;
            Machine = machine;
            Master = "";
            UnspecifiedDowntimes = 0;
            DowntimesComment = "";
            CommonComment = "";
        }
        public int? Id { get; set; }
        public DateTime ShiftDate { get; set; }
        public string Shift { get; set; }
        public string Machine { get; set; }
        public string Master { get; set; }
        public double UnspecifiedDowntimes { get; set; }
        public string DowntimesComment { get; set; }
        public string CommonComment { get; set; }
        public bool IsChecked { get; set; }
        public bool? GiverWorkplaceCleaned { get; set; }
        public bool? GiverFailures { get; set; }
        public bool? GiverExtraneousNoises { get; set; }
        public bool? GiverLiquidLeaks { get; set; }
        public bool? GiverToolBreakage { get; set; }
        public double? GiverСoolantСoncentration { get; set; }
        public bool? RecieverWorkplaceCleaned { get; set; }
        public bool? RecieverFailures { get; set; }
        public bool? RecieverExtraneousNoises { get; set; }
        public bool? RecieverLiquidLeaks { get; set; }
        public bool? RecieverToolBreakage { get; set; }
        public double? RecieverСoolantСoncentration { get; set; }

        /// <summary>
        /// Смена простояла целиком: неуказанные простои равны её полной длительности.
        /// Такие смены не считаются отработанными, а <see cref="DowntimesComment"/>
        /// раскладывает их по причинам в сводных отчётах.
        /// </summary>
        public bool IsIdleWholeShift =>
            (Shift == Shifts.Day && UnspecifiedDowntimes == Shifts.DayMinutes)
            || (Shift == Shifts.Night && UnspecifiedDowntimes == Shifts.NightMinutes);
    }
}
