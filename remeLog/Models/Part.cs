using libeLog;
using libeLog.Base;
using libeLog.Extensions;
using remeLog.Infrastructure;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace remeLog.Models
{
    public class Part : ViewModel, IDataErrorInfo
    {
        public Part(
            Guid guid,
            string machine,
            string shift,
            DateTime shiftDate,
            string @operator,
            string partName,
            string order,
            int setup,
            double finishedCount,
            int totalCount,
            DateTime startSetupTime,
            DateTime startMachiningTime,
            double setupTimeFact,
            DateTime endMachiningTime,
            double setupTimePlan,
            double setupTimePlanForReport,
            double singleProductionTimePlan,
            double productionTimeFact, TimeSpan machiningTime,
            double setupDowntimes,
            double machiningDowntimes,
            double partialSetupTime,
            double createNcProgramTime,
            double maintenanceTime,
            double toolSearchingTime,
            double toolChangingTime,
            double mentoringTime,
            double contactingDepartmentsTime,
            double fixtureMakingTime,
            double hardwareFailureTime,
            string operatorComment,
            string masterSetupComment = "",
            string masterMachiningComment = "",
            string specifiedDowntimesComment = "",
            string unspecifiedDowntimesComment = "",
            string masterComment = "",
            double fixedSetupTimePlan = 0,
            double fixedMachineTimePlan = 0,
            string engineerComment = "",
            bool excludeFromReports = false,
            string longSetupReasonComment = "",
            string longSetupFixComment = "",
            string longSetupEngeneerComment = "",
            double excludedOperationsTime = 0,
            string increaseReason = ""
            )
        {
            _Guid = guid;
            _Machine = machine;
            _Shift = shift;
            _ShiftDate = shiftDate;
            _Operator = @operator;
            _PartName = partName;
            _Order = order;
            _Setup = setup;
            _FinishedCount = finishedCount;
            _TotalCount = totalCount;
            _StartSetupTime = startSetupTime;
            _StartMachiningTime = startMachiningTime;
            _EndMachiningTime = endMachiningTime;
            _SetupTimePlan = setupTimePlan;
            _SetupTimePlanForReport = setupTimePlanForReport;
            _SingleProductionTimePlan = singleProductionTimePlan;
            _MachiningTime = machiningTime;
            _SetupDowntimes = setupDowntimes;
            _MachiningDowntimes = machiningDowntimes;
            _PartialSetupTime = partialSetupTime;
            _CreateNcProgramTime = createNcProgramTime;
            _MaintenanceTime = maintenanceTime;
            _ToolSearchingTime = toolSearchingTime;
            _ToolChangingTime = toolChangingTime;
            _MentoringTime = mentoringTime;
            _ContactingDepartmentsTime = contactingDepartmentsTime;
            _FixtureMakingTime = fixtureMakingTime;
            _HardwareFailureTime = hardwareFailureTime;
            _OperatorComment = operatorComment;
            _MasterSetupComment = masterSetupComment;
            _MasterMachiningComment = masterMachiningComment;
            _SpecifiedDowntimesComment = specifiedDowntimesComment;
            _UnspecifiedDowntimesComment = unspecifiedDowntimesComment;
            _MasterComment = masterComment;
            _FixedSetupTimePlan = fixedSetupTimePlan;
            _FixedProductionTimePlan = fixedMachineTimePlan;
            _EngineerComment = engineerComment;
            _ExcludeFromReports = excludeFromReports;
            NeedUpdate = false;
            _LongSetupReasonComment = longSetupReasonComment;
            _LongSetupFixComment = longSetupFixComment;
            _LongSetupEngeneerComment = longSetupEngeneerComment;
            _ExcludedOperationsTime = excludedOperationsTime;
            _IncreaseReason = increaseReason;
        }

        public Part(Part part)
        {
            _Guid = part.Guid;
            _Machine = part.Machine;
            _Shift = part.Shift;
            _ShiftDate = part.ShiftDate;
            _Operator = part.Operator;
            _PartName = part.PartName;
            _Order = part.Order;
            _Setup = part.Setup;
            _FinishedCount = part.FinishedCount;
            _TotalCount = part.TotalCount;
            _StartSetupTime = part.StartSetupTime;
            _StartMachiningTime = part.StartMachiningTime;
            _EndMachiningTime = part.EndMachiningTime;
            _SetupTimePlan = part.SetupTimePlan;
            _SetupTimePlanForReport = part.SetupTimePlanForReport;
            _SingleProductionTimePlan = part.SingleProductionTimePlan;
            _MachiningTime = part.MachiningTime;
            _SetupDowntimes = part.SetupDowntimes;
            _MachiningDowntimes = part.MachiningDowntimes;
            _PartialSetupTime = part.PartialSetupTime;
            _CreateNcProgramTime = part.CreateNcProgramTime;
            _MaintenanceTime = part.MaintenanceTime;
            _ToolSearchingTime = part.ToolSearchingTime;
            _ToolChangingTime = part.ToolChangingTime;
            _MentoringTime = part.MentoringTime;
            _ContactingDepartmentsTime = part.ContactingDepartmentsTime;
            _FixtureMakingTime = part.FixtureMakingTime;
            _HardwareFailureTime = part.HardwareFailureTime;
            _OperatorComment = part.OperatorComment;
            _MasterSetupComment = part.MasterSetupComment;
            _MasterMachiningComment = part.MasterMachiningComment;
            _SpecifiedDowntimesComment = part.SpecifiedDowntimesComment;
            _UnspecifiedDowntimesComment = part.UnspecifiedDowntimesComment;
            _MasterComment = part.MasterComment;
            _FixedSetupTimePlan = part.FixedSetupTimePlan;
            _FixedProductionTimePlan = part.FixedProductionTimePlan;
            _EngineerComment = part.EngineerComment;
            NeedUpdate = false;
            _LongSetupReasonComment = part.LongSetupReasonComment;
            _LongSetupFixComment = part.LongSetupFixComment;
            _LongSetupEngeneerComment = part.LongSetupEngeneerComment;
            _ExcludedOperationsTime = part.ExcludedOperationsTime;
            _IncreaseReason = part.IncreaseReason;
        }

        private Guid _Guid;
        /// <summary> GUID </summary>
        public Guid Guid
        {
            get => _Guid;
            set {
                if (Set(ref _Guid, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private string _Machine;
        /// <summary> Станок </summary>
        public string Machine
        {
            get => _Machine;
            set
            {
                if (Set(ref _Machine, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private DateTime _ShiftDate;
        /// <summary> Дата смены </summary>
        public DateTime ShiftDate
        {
            get => _ShiftDate;
            set
            {
                if (Set(ref _ShiftDate, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private string _Shift;
        /// <summary> Смена </summary>
        public string Shift
        {
            get => _Shift;
            set
            {
                if (Set(ref _Shift, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private string _Operator;
        /// <summary> Оператор </summary>
        public string Operator
        {
            get => _Operator;
            set
            {
                if (Set(ref _Operator, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private string _PartName;
        /// <summary> Название детали </summary>
        public string PartName
        {
            get => _PartName;
            set
            {
                if (Set(ref _PartName, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private string _Order;
        /// <summary> Номер маршрутного листа </summary>
        public string Order
        {
            get => _Order;
            set {
                if (Set(ref _Order, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private int _Setup;
        /// <summary> Номер установки </summary>
        public int Setup
        {
            get => _Setup;
            set {
                if (Set(ref _Setup, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private double _FinishedCount;
        /// <summary> Изготовлено </summary>
        public double FinishedCount
        {
            get => _FinishedCount;
            set {
                if (Set(ref _FinishedCount, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        /// <summary> Изготовлено по факту с учетом наладок </summary>
        public double FinishedCountFact
        {
            get
            {
                return StartSetupTime != StartMachiningTime && FinishedCount != 0 ? FinishedCount - 1 : FinishedCount;
            }
        }

        private int _TotalCount;
        /// <summary> Всего партия </summary>
        public int TotalCount
        {
            get => _TotalCount;
            set {
                if (Set(ref _TotalCount, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        /// <summary> Присутствует ли деталь в списке серийных </summary>
        public bool IsSerial
        {
            get
            {
                // делать ли вхождение имен вместо полного совпадения?
                return AppSettings.SerialParts.Contains(PartName.NormalizedPartNameWithoutComments());
            }
        }

        /// <summary>
        /// Разрешено ли редактировать нормативы детали - true если деталь не серийная, либо если серийная и редактирование разблокировано вручную.
        /// </summary>
        public bool IsEditEnabled => !IsSerial || IsUnlocked;

        private bool _IsUnlocked;
        /// <summary> Разлокировано ли редактирование (участвует только если деталь серийная) </summary>
        public bool IsUnlocked
        {
            get => _IsUnlocked;
            set
            {
                if (Set(ref _IsUnlocked, value))
                {
                    OnPropertyChanged(nameof(IsEditEnabled));
                }
            }
        }

        private DateTime _StartSetupTime;
        /// <summary> Начало наладки </summary>
        public DateTime StartSetupTime
        {
            get => _StartSetupTime;
            set {
                if (Set(ref _StartSetupTime, FixedDate(value)))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private DateTime _StartMachiningTime;
        /// <summary> Завершение наладки / начало изготовления </summary>
        public DateTime StartMachiningTime
        {
            get => _StartMachiningTime;
            set {
                if (Set(ref _StartMachiningTime, FixedDate(value)))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        public double SetupTimeFact => (StartMachiningTime - StartSetupTime - DateTimes.GetBreaksBetween(StartSetupTime, StartMachiningTime)).TotalMinutes - SetupDowntimes - PartialSetupTime;
        public double SetupTimeFactIncludePartial => (StartMachiningTime - StartSetupTime - DateTimes.GetBreaksBetween(StartSetupTime, StartMachiningTime)).TotalMinutes - SetupDowntimes;
        public double SetupTimeFactIncludePartialAndDowntimes => (StartMachiningTime - StartSetupTime - DateTimes.GetBreaksBetween(StartSetupTime, StartMachiningTime)).TotalMinutes;
        public double SetupTimeFactFull => (StartMachiningTime - StartSetupTime).TotalMinutes;
        public double ProductionTimeFact => (EndMachiningTime - StartMachiningTime - DateTimes.GetBreaksBetween(StartMachiningTime, EndMachiningTime)).TotalMinutes - MachiningDowntimes;

        private DateTime _EndMachiningTime;
        /// <summary> Завершение изготовления </summary>
        public DateTime EndMachiningTime
        {
            get => _EndMachiningTime;
            set {
                if (Set(ref _EndMachiningTime, FixedDate(value)))
                {
                    NeedUpdate = true;
                    //ProductionTimeFact = (EndMachiningTime - StartMachiningTime - DateTimes.GetBreaksBetween(StartMachiningTime, EndMachiningTime)).TotalMinutes;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private double _SetupTimePlan;
        /// <summary> Норматив наладки </summary>
        public double SetupTimePlan
        {
            get => _SetupTimePlan;
            set
            {
                if(Set(ref _SetupTimePlan, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private double _SetupTimePlanForReport;
        /// <summary> Норматив наладки для отчета </summary>
        public double SetupTimePlanForReport
        {
            get => _SetupTimePlanForReport;
            set {
                if (Set(ref _SetupTimePlanForReport, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private double _SingleProductionTimePlan;
        /// <summary> Штучный норматив </summary>
        public double SingleProductionTimePlan
        {
            get => _SingleProductionTimePlan;
            set {
                if (Set(ref _SingleProductionTimePlan, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private TimeSpan _MachiningTime;
        /// <summary> Машинное время </summary>
        public TimeSpan MachiningTime
        {
            get => _MachiningTime;
            set {
                if (Set(ref _MachiningTime, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private double _SetupDowntimes;
        /// <summary> Время простоев в наладке </summary>
        public double SetupDowntimes
        {
            get => _SetupDowntimes;
            set {
                if (Set(ref _SetupDowntimes, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        /// <summary>
        /// Простои в наладке с добавленным превышением норматива частичной наладкой
        /// </summary>
        public double SetupDowntimesWithPartialExcess
        {
            get
            {
                double partialExcess = 0;
                if (PartialSetupTime > SetupTimePlanForReport) partialExcess = PartialSetupTime - SetupTimePlanForReport;
                return SetupDowntimes + partialExcess;
            }
        }


        private double _MachiningDowntimes;
        /// <summary> Время простоев в изготовлении </summary>
        public double MachiningDowntimes
        {
            get => _MachiningDowntimes;
            set {
                if (Set(ref _MachiningDowntimes, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private double _PartialSetupTime;
        /// <summary> Время простоя "Частичная наладка" </summary>
        public double PartialSetupTime
        {
            get => _PartialSetupTime;
            set {
                if (Set(ref _PartialSetupTime, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private double _CreateNcProgramTime;
        /// <summary> Время простоя "Написание УП" </summary>
        public double CreateNcProgramTime
        {
            get => _CreateNcProgramTime;
            set
            {
                if (Set(ref _CreateNcProgramTime, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private double _MaintenanceTime;
        /// <summary> Время простоя "Обслуживание" </summary>
        public double MaintenanceTime
        {
            get => _MaintenanceTime;
            set {
                if (Set(ref _MaintenanceTime, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private double _ToolSearchingTime;
        /// <summary> Время простоя "Поиск и получение инструмента" </summary>
        public double ToolSearchingTime
        {
            get => _ToolSearchingTime;
            set {
                if (Set(ref _ToolSearchingTime, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private double _ToolChangingTime;
        /// <summary> Время простоя "Замена инструмента" </summary>
        public double ToolChangingTime
        {
            get => _ToolChangingTime;
            set
            {
                if (Set(ref _ToolChangingTime, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private double _MentoringTime;
        /// <summary> Время простоя "Помощь / обучение" </summary>
        public double MentoringTime
        {
            get => _MentoringTime;
            set {
                if (Set(ref _MentoringTime, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private double _ContactingDepartmentsTime;
        /// <summary> Время простоя "Обращение в другие службы" </summary>
        public double ContactingDepartmentsTime
        {
            get => _ContactingDepartmentsTime;
            set {
                if (Set(ref _ContactingDepartmentsTime, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private double _FixtureMakingTime;
        /// <summary> Время простоя "Изготовление оснастки и калибров" </summary>
        public double FixtureMakingTime
        {
            get => _FixtureMakingTime;
            set {
                if (Set(ref _FixtureMakingTime, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private double _HardwareFailureTime;
        /// <summary> Время простоя "Отказ оборудования" </summary>
        public double HardwareFailureTime
        {
            get => _HardwareFailureTime;
            set {
                if (Set(ref _HardwareFailureTime, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private string _OperatorComment;
        /// <summary> Комментарий оператора </summary>
        public string OperatorComment
        {
            get => _OperatorComment;
            set {
                if (Set(ref _OperatorComment, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private string _MasterSetupComment;
        /// <summary> Комментарий к отклонениям от нормативов в наладке </summary>
        public string MasterSetupComment
        {
            get => _MasterSetupComment;
            set {
                if (Set(ref _MasterSetupComment, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private string _MasterMachiningComment;
        /// <summary> Комментарий к отклонениям от нормативов в изготовлении </summary>
        public string MasterMachiningComment
        {
            get => _MasterMachiningComment;
            set {
                if (Set(ref _MasterMachiningComment, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private string _SpecifiedDowntimesComment;
        /// <summary> Комментарий к зарегистрированным простоям </summary>
        public string SpecifiedDowntimesComment
        {
            get => _SpecifiedDowntimesComment;
            set {
                if (Set(ref _SpecifiedDowntimesComment, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private string _UnspecifiedDowntimesComment;
        /// <summary> Комментарий к незарегистрированным простоям </summary>
        public string UnspecifiedDowntimesComment
        {
            get => _UnspecifiedDowntimesComment;
            set {
                if (Set(ref _UnspecifiedDowntimesComment, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private string _MasterComment;
        /// <summary> Комментарий мастера </summary>
        public string MasterComment
        {
            get => _MasterComment;
            set {
                if (Set(ref _MasterComment, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(SpecifiedDowntimesComment));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private double _FixedSetupTimePlan;
        /// <summary> Исправленный норматив наладки </summary>
        public double FixedSetupTimePlan
        {
            get => _FixedSetupTimePlan;
            set
            {
                NeedUpdate = true;
                Set(ref _FixedSetupTimePlan, value);
            }
        }


        private double _FixedProductionTimePlan;
        /// <summary> Исправленный норматив на изготовление </summary>
        public double FixedProductionTimePlan
        {
            get => _FixedProductionTimePlan;
            set
            {
                NeedUpdate = true;
                Set(ref _FixedProductionTimePlan, value);
            }
        }


        private static bool _CalcFixed;
        /// <summary> Описание </summary>
        public static bool CalcFixed
        {
            get => _CalcFixed;
            set 
            {
                _CalcFixed = value;
            }
        }


        public double SetupTimePlanForCalc => FixedSetupTimePlan > 0 && CalcFixed ? FixedSetupTimePlan : SetupTimePlan;

        public double ProductionTimePlanForCalc => FixedProductionTimePlan > 0 && CalcFixed ? FixedProductionTimePlan : SingleProductionTimePlan;

        private string _EngineerComment;
        /// <summary> Комментарий техотдела </summary>
        public string EngineerComment
        {
            get => _EngineerComment;
            set {
                if (Set(ref _EngineerComment, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private double _ExcludedOperationsTime;
        /// <summary> Суммарное время исключённых операций при изменении техпроцесса </summary>
        public double ExcludedOperationsTime
        {
            get => _ExcludedOperationsTime;
            set
            {
                if (Set(ref _ExcludedOperationsTime, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private string _IncreaseReason;
        /// <summary> Причина увеличения норматива </summary>
        public string IncreaseReason
        {
            get => _IncreaseReason;
            set
            {
                if (Set(ref _IncreaseReason, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }


        private bool _ExcludeFromReports;
        /// <summary> Исключать ли деталь из расчетов в отчетах </summary>
        public bool ExcludeFromReports
        {
            get => _ExcludeFromReports;
            set
            {
                if (Set(ref _ExcludeFromReports, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private bool _NeedUpdate;
        /// <summary> Описание </summary>
        public bool NeedUpdate
        {
            get => _NeedUpdate;
            set { 
                if (Set(ref _NeedUpdate, value))
                {
                    OnPropertyChanged(nameof(SetupTimeFact));
                    OnPropertyChanged(nameof(ProductionTimeFact));
                    OnPropertyChanged(nameof(SetupRatio));
                    OnPropertyChanged(nameof(SetupRatioTitle));
                    OnPropertyChanged(nameof(ProductionRatio));
                    OnPropertyChanged(nameof(ProductionRatioTitle));
                    OnPropertyChanged(nameof(SingleProductionTime));
                    OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                    OnPropertyChanged(nameof(MasterSetupComment));
                    OnPropertyChanged(nameof(MasterMachiningComment));
                    OnPropertyChanged(nameof(MasterComment));
                    OnPropertyChanged(nameof(Error));
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private string _LongSetupReasonComment;
        public string LongSetupReasonComment
        {
            get => _LongSetupReasonComment;
            set
            {
                if (Set(ref _LongSetupReasonComment, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private string _LongSetupFixComment;
        public string LongSetupFixComment
        {
            get => _LongSetupFixComment;
            set
            {
                if (Set(ref _LongSetupFixComment, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        private string _LongSetupEngeneerComment;
        public string LongSetupEngeneerComment
        {
            get => _LongSetupEngeneerComment;
            set
            {
                if (Set(ref _LongSetupEngeneerComment, value))
                {
                    NeedUpdate = true;
                    OnPropertyChanged(nameof(NeedUpdate));
                }
            }
        }

        public double SingleProductionTime { get 
            {
                var partsCount = StartSetupTime != StartMachiningTime && FinishedCount > 1 ? FinishedCount - 1 : FinishedCount;
                return ProductionTimeFact / partsCount;
            } }

        //public double SetupRatio => PartialSetupTime == 0 
        //    ? SetupTimePlanForCalc / SetupTimeFact 
        //    : SetupTimePlanForCalc * AppSettings.MaxSetupLimits.GetValueOrDefault(Machine, AppSettings.FallbackMaxSetupLimitValue) < PartialSetupTime
        //        ? SetupTimePlanForCalc / PartialSetupTime 
        //        : 0;

        public double SetupRatio => SetupTimePlanForCalc / SetupTimeFact;

        public double SetupRatioIncludeDowntimes => SetupTimeFact > 0 ? SetupTimePlanForCalc / (SetupTimeFact + SetupDowntimes) : 0;
        public string SetupRatioTitle => SetupRatio is double.NaN or double.PositiveInfinity 
            ? "б/н" 
            : SetupRatio > AppSettings.MaxSetupLimits.GetValueOrDefault(Machine, AppSettings.FallbackMaxSetupLimitValue) 
                ? $"{SetupRatio:0%}\n({AppSettings.MaxSetupLimits.GetValueOrDefault(Machine, AppSettings.FallbackMaxSetupLimitValue):0%})" 
                : $"{SetupRatio:0%}";
        public double ProductionRatio => FinishedCountFact * ProductionTimePlanForCalc / ProductionTimeFact;
        public string ProductionRatioTitle => ProductionRatio is double.NaN or double.PositiveInfinity or double.NegativeInfinity ? "б/и" : $"{ProductionRatio:0%}";
        public double SpecifiedDowntimesRatio => (SetupDowntimes + MachiningDowntimes) / (EndMachiningTime - StartSetupTime).TotalMinutes;
        public double PartReplacementTime => SingleProductionTime == 0 ? 0 : SingleProductionTime - MachiningTime.TotalMinutes;
        public double PlanForBatch { get {
                var partsCount = StartSetupTime != StartMachiningTime && FinishedCount > 1 ? FinishedCount - 1 : FinishedCount;
                return partsCount * ProductionTimePlanForCalc;
            } }
        private DateTime FixedDate(DateTime dateTime)
        {
            var year = ShiftDate.Year;
            var month = ShiftDate.Month;
            var day = ShiftDate.Day;
            var hour = dateTime.Hour;
            var minute = dateTime.Minute;
            var fixedDateTime = new DateTime(year, month, day, hour, minute, 0);
            var diff = (fixedDateTime - ShiftDate.AddHours(8)).TotalMinutes;
            if (diff <= 0 && Shift == "Ночь") fixedDateTime = fixedDateTime.AddDays(1);
            return fixedDateTime;
        }

        

        public Dictionary<string, bool> SetupReasonsRequireComment =>
            AppSettings.Instance.SetupReasons.ToDictionary(x => x.Reason, x => x.RequireComment);
        public Dictionary<string, bool> MachiningReasonsRequireComment =>
            AppSettings.Instance.MachiningReasons.ToDictionary(x => x.Reason, x => x.RequireComment);

        private static bool RequiresComment(string? comment, Dictionary<string, bool> reasonsDict)
        {
            return !string.IsNullOrWhiteSpace(comment) &&
                   reasonsDict.TryGetValue(comment, out bool requiresComment) &&
                   requiresComment;
        }

        public string Error
        {
            get
            {
                if (string.IsNullOrWhiteSpace(this[nameof(SpecifiedDowntimesComment)]) &&
                    string.IsNullOrWhiteSpace(this[nameof(MasterSetupComment)]) &&
                    string.IsNullOrWhiteSpace(this[nameof(MasterMachiningComment)]) &&
                    string.IsNullOrWhiteSpace(this[nameof(MasterComment)]))
                {
                    return null!;
                }
                else
                {
                    return "Ошибка валидации";
                }
            }
        }

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(MasterSetupComment) when string.IsNullOrWhiteSpace(MasterSetupComment) && SetupRatio == 0 && SetupTimeFact > 0:
                        return "Необходимо указать причину отсутствия номатива наладки.";
                    case nameof(MasterSetupComment) when string.IsNullOrWhiteSpace(MasterSetupComment) && (SetupRatio < 0.695 || SetupRatio > AppSettings.MaxSetupLimit) && SetupTimeFact > 0:
                        return "Необходимо указать причину невыполнения номатива наладки.";

                    case nameof(MasterMachiningComment) when string.IsNullOrWhiteSpace(MasterMachiningComment) && ProductionRatio == 0:
                        return "Необходимо указать причину отсутствия номатива изготовления.";
                    case nameof(MasterMachiningComment) when string.IsNullOrWhiteSpace(MasterMachiningComment) && ProductionRatio is < 0.695 or > 1.2:
                        return "Необходимо указать причину невыполнения номатива изготовления.";

                    case nameof(MasterComment) when string.IsNullOrWhiteSpace(MasterComment) &&
                    (RequiresComment(MasterSetupComment, SetupReasonsRequireComment) ||
                     RequiresComment(MasterMachiningComment, MachiningReasonsRequireComment)):
                        return "Требуется указать дополнительный комментарий для выбранной причины.";
                    case nameof(SpecifiedDowntimesComment) when string.IsNullOrWhiteSpace(SpecifiedDowntimesComment) && SpecifiedDowntimesRatio > 0.5:
                        return "Необходимо дать комментарий т.к. простой более 50%.";
                    default:
                        return null!;
                }
            }
        }
    }
}
