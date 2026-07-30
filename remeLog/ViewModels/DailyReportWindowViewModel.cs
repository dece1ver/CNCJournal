using libeLog;
using libeLog.Base;
using remeLog.Infrastructure;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows;
using Part = remeLog.Models.Part;
using System.ComponentModel;
using libeLog.Views;

namespace remeLog.ViewModels
{
    public class DailyReportWindowViewModel : ViewModel, IDataErrorInfo
    {
        public DailyReportWindowViewModel((ICollection<Part> parts, DateTime date, string machine) shiftInfo)
        {
            _Status = string.Empty;
            _Parts = shiftInfo.parts.ToList();
            _ShiftDate = shiftInfo.date;
            _Machine = shiftInfo.machine;
            _DayDowntimesReason = string.Empty;
            _NightDowntimesReason = string.Empty;
            _DayMasterComment = string.Empty;
            _NightMasterComment = string.Empty;
            _CurrentMaster = string.Empty;
            _Masters = new List<string>();
            var mastersResult = Database.ReadMasters();
            if (!mastersResult.IsOk)
            {
                MessageBoxWindow.Show("Не удалось получить список мастеров.");
            }
            else
            {
                _Masters = mastersResult.Value!;
            }
            
            _Title = $"Суточный отчет за {_ShiftDate:dd.MM.yyyy} по станку {_Machine} (новый)";

            var readDayDbResult = Database.ReadShiftInfo(new ShiftInfo(ShiftDate, ShiftType.Day, _Machine));
            if (!readDayDbResult.IsOk)
            {
                MessageBoxWindow.Show("Не удалось получить доступ к дневным сменам в базе данных, внесение новой информации может привести к потере изначальных данных.", 
                    "Сообщите разработчику.", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning); ;
            }
            var dbDayShfts = readDayDbResult.Value;
            if (dbDayShfts is { Count: 1})
            {
                var shift = dbDayShfts.First();
                _CurrentMaster = shift.Master;
                _DayDowntimesReason = shift.DowntimesComment;
                _DayMasterComment = shift.CommonComment;
                _DayGiverWorkplaceCleaned = shift.GiverWorkplaceCleaned;
                _DayGiverFailures = shift.GiverFailures;
                _DayGiverExtraneousNoises = shift.GiverExtraneousNoises;
                _DayGiverLiquidLeaks = shift.GiverLiquidLeaks;
                _DayGiverToolBreakage = shift.GiverToolBreakage;
                _DayGiverCoolantConcentration = shift.GiverСoolantСoncentration;
                _DayRecieverWorkplaceCleaned = shift.RecieverWorkplaceCleaned;
                _DayRecieverFailures = shift.RecieverFailures;
                _DayRecieverExtraneousNoises = shift.RecieverExtraneousNoises;
                _DayRecieverLiquidLeaks = shift.RecieverLiquidLeaks;
                _DayRecieverToolBreakage = shift.RecieverToolBreakage;
                _DayRecieverCoolantConcentration = shift.RecieverСoolantСoncentration;
                
                if (UnspecifiedDayDowntimes != shift.UnspecifiedDowntimes && shift.Master != "")
                {
                    MessageBoxWindow.Show("Время неотмеченных простоев изменилось с момента последнего сохранения отчета!\n\nПодробная информация будет добавлена в комментарий мастера.", 
                        "Внимание.",
                        MessageBoxButton.OK, 
                        MessageBoxImage.Warning);
                    _DayMasterComment += "\n\n===\n" +
                        $"Прошлое значение: {shift.UnspecifiedDowntimes:0.##}";
                }
                Title = Title.Replace("(новый)","(редактирование)");
            }
            var readNightDbResult = Database.ReadShiftInfo(new ShiftInfo(ShiftDate, ShiftType.Night, _Machine));
            if (!readNightDbResult.IsOk)
            {
                MessageBoxWindow.Show("Не удалось получить доступ к ночным сменам в базе данных, внесение новой информации может привести к потере изначальных данных.", 
                    "Сообщите разработчику.", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning); ;
            }
            var dbNightShfts = readNightDbResult.Value;
            if (dbNightShfts is { Count: 1 })
            {
                var shift = dbNightShfts.First();
                _CurrentMaster = shift.Master;
                _NightDowntimesReason = shift.DowntimesComment;
                _NightMasterComment = shift.CommonComment;
                _IsChecked = shift.IsChecked;
                _NightGiverWorkplaceCleaned = shift.GiverWorkplaceCleaned;
                _NightGiverFailures = shift.GiverFailures;
                _NightGiverExtraneousNoises = shift.GiverExtraneousNoises;
                _NightGiverLiquidLeaks = shift.GiverLiquidLeaks;
                _NightGiverToolBreakage = shift.GiverToolBreakage;
                _NightGiverCoolantConcentration = shift.GiverСoolantСoncentration;
                _NightRecieverWorkplaceCleaned = shift.RecieverWorkplaceCleaned;
                _NightRecieverFailures = shift.RecieverFailures;
                _NightRecieverExtraneousNoises = shift.RecieverExtraneousNoises;
                _NightRecieverLiquidLeaks = shift.RecieverLiquidLeaks;
                _NightRecieverToolBreakage = shift.RecieverToolBreakage;
                _NightRecieverCoolantConcentration = shift.RecieverСoolantСoncentration;
                if (UnspecifiedNightDowntimes != shift.UnspecifiedDowntimes && shift.Master != "")
                {
                    MessageBoxWindow.Show("Время неотмеченных простоев изменилось с момента последнего сохранения отчета!\n\nПрошлое значение будет добавлено в комментарий мастера.",
                        "Внимание.",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    _NightMasterComment += "\n\n===\n" +
                        $"Прошлое значение: {shift.UnspecifiedDowntimes:0.##}";
                }
            }
            if (!string.IsNullOrWhiteSpace(_CurrentMaster) && !_Masters.Contains(_CurrentMaster))
            {
                _Masters.Insert(0, _CurrentMaster);
            }
            UpdateShiftInfoCommand = new LambdaCommand(OnUpdateShiftInfoCommandExecuted, CanUpdateShiftInfoCommandExecute);
        }

        private DateTime _ShiftDate;
        /// <summary> Дата смены </summary>
        public DateTime ShiftDate
        {
            get => _ShiftDate;
            set => Set(ref _ShiftDate, value);
        }

        private List<Part> _Parts;
        /// <summary> Детали </summary>
        public List<Part> Parts
        {
            get => _Parts;
            set => Set(ref _Parts, value);
        }


        private string _Machine;
        /// <summary> Описание </summary>
        public string Machine
        {
            get => _Machine;
            set => Set(ref _Machine, value);
        }


        private List<string> _Masters;
        /// <summary> Мастера </summary>
        public List<string> Masters
        {
            get => _Masters;
            set => Set(ref _Masters, value);
        }


        private string _CurrentMaster;
        /// <summary> Текущий мастер </summary>
        public string CurrentMaster
        {
            get => _CurrentMaster;
            set => Set(ref _CurrentMaster, value);
        }

        private string _Status;
        /// <summary> Статус </summary>
        public string Status
        {
            get => _Status;
            set => Set(ref _Status, value);
        }


        private string _DayDowntimesReason;
        /// <summary> Описание </summary>
        public string DayDowntimesReason
        {
            get => _DayDowntimesReason;
            set => Set(ref _DayDowntimesReason, value);
        }

        private string _NightDowntimesReason;
        /// <summary> Описание </summary>
        public string NightDowntimesReason
        {
            get => _NightDowntimesReason;
            set => Set(ref _NightDowntimesReason, value);
        }


        private string _DayMasterComment;
        /// <summary> Описание </summary>
        public string DayMasterComment
        {
            get => _DayMasterComment;
            set => Set(ref _DayMasterComment, value);
        }

        private string _NightMasterComment;
        /// <summary> Описание </summary>
        public string NightMasterComment
        {
            get => _NightMasterComment;
            set => Set(ref _NightMasterComment, value);
        }

        private string _Title;
        /// <summary> Описание </summary>
        public string Title
        {
            get => _Title;
            set => Set(ref _Title, value);
        }


        private bool _IsChecked;
        /// <summary> Описание </summary>
        public bool IsChecked
        {
            get => _IsChecked;
            set => Set(ref _IsChecked, value);
        }


        public List<string> DowntimeReasons => AppSettings.Instance.UnspecifiedDowntimesReasons;

        public bool UnspecifiedDayDowntimesNeedAttention => UnspecifiedDayDowntimesRatio is > 0.1 or < -0.1;
        public bool UnspecifiedNightDowntimesNeedAttention => UnspecifiedNightDowntimesRatio is > 0.1 or < -0.1;
        public bool DayPartialSetupNeedAttention => DayPartialSetupRatio is > 0.3;
        public bool NightPartialSetupNeedAttention => NightPartialSetupRatio is > 0.3;
        public bool SpecifiedDayDowntimesNeedAttention => SpecifiedDayDowntimesRatio is > 0.1 or < -0.1;
        public bool SpecifiedNightDowntimesNeedAttention => SpecifiedNightDowntimesRatio is > 0.1 or < -0.1;
        
        public List<Part> DayParts => Parts.Where(p => p.Shift == "День").ToList();
        public List<Part> NightParts => Parts.Where(p => p.Shift == "Ночь").ToList();
        public int DayOrders => Parts.Where(p => p.Shift == new Shift(ShiftType.Day).Name).GroupBy(p => p.Order).Count();
        public int NightOrders => Parts.Where(p => p.Shift == new Shift(ShiftType.Night).Name).GroupBy(p => p.Order).Count();
        public double DayFinishedCount => Parts.Where(p => p.Shift == new Shift(ShiftType.Day).Name).Sum(p => p.FinishedCount);
        public double NightFinishedCount => Parts.Where(p => p.Shift == new Shift(ShiftType.Night).Name).Sum(p => p.FinishedCount);
        public int DaySetups => Parts.Count(p => p.StartSetupTime != p.StartMachiningTime && p.Shift == new Shift(ShiftType.Day).Name);
        public int NightSetups => Parts.Count(p => p.StartSetupTime != p.StartMachiningTime && p.Shift == new Shift(ShiftType.Night).Name);
        public string DayOperator => DayParts.Count > 0 ? DayParts.First().Operator : "Нет оператора.";
        public string NightOperator => NightParts.Count > 0 ? NightParts.First().Operator : "Нет оператора.";

        public string DayWorkInfo => $"М/Л: {DayOrders}, Наладок: {DaySetups}, Деталей: {DayFinishedCount}";
        public string NightWorkInfo => $"М/Л: {NightOrders}, Наладок: {NightSetups}, Деталей: {NightFinishedCount}";

        public double UnspecifiedDayDowntimes => Parts.UnspecifiedDowntimes(ShiftDate, ShiftDate, ShiftType.Day);
        public double UnspecifiedNightDowntimes => Parts.UnspecifiedDowntimes(ShiftDate, ShiftDate, ShiftType.Night);
        public double SpecifiedDayDowntimes => Parts.SpecifiedDowntimes(ShiftType.Day);
        public double SpecifiedNightDowntimes => Parts.SpecifiedDowntimes(ShiftType.Night);
        public double UnspecifiedDayDowntimesRatio => Parts.UnspecifiedDowntimesRatio(ShiftDate, ShiftDate, ShiftType.Day);
        public double UnspecifiedNightDowntimesRatio => Parts.UnspecifiedDowntimesRatio(ShiftDate, ShiftDate, ShiftType.Night);
        public double SpecifiedDayDowntimesRatio => Parts.SpecifiedDowntimesRatio(ShiftType.Day);
        public double DayPartialSetup => Parts.PartialSetup(ShiftDate, ShiftDate, ShiftType.Day);
        public double NightPartialSetup => Parts.PartialSetup(ShiftDate, ShiftDate, ShiftType.Night);
        public double DayPartialSetupRatio => Parts.PartialSetupRatio(ShiftDate, ShiftDate, ShiftType.Day);
        public double NightPartialSetupRatio => Parts.PartialSetupRatio(ShiftDate, ShiftDate, ShiftType.Night);

        public double SpecifiedNightDowntimesRatio => Parts.SpecifiedDowntimesRatio(ShiftType.Night);
        public double TotalWorkedDayTime => (int)ShiftType.Day - Parts.UnspecifiedDowntimes(ShiftDate, ShiftDate, ShiftType.Day);
        public double TotalWorkedNightTime => (int)ShiftType.Night - Parts.UnspecifiedDowntimes(ShiftDate, ShiftDate, ShiftType.Night);

        private bool? _DayGiverWorkplaceCleaned;
        public bool? DayGiverWorkplaceCleaned
        {
            get => _DayGiverWorkplaceCleaned;
            set => Set(ref _DayGiverWorkplaceCleaned, value);
        }

        private bool? _DayGiverFailures;
        public bool? DayGiverFailures
        {
            get => _DayGiverFailures;
            set => Set(ref _DayGiverFailures, value);
        }

        private bool? _DayGiverExtraneousNoises;
        public bool? DayGiverExtraneousNoises
        {
            get => _DayGiverExtraneousNoises;
            set => Set(ref _DayGiverExtraneousNoises, value);
        }

        private bool? _DayGiverLiquidLeaks;
        public bool? DayGiverLiquidLeaks
        {
            get => _DayGiverLiquidLeaks;
            set => Set(ref _DayGiverLiquidLeaks, value);
        }

        private bool? _DayGiverToolBreakage;
        public bool? DayGiverToolBreakage
        {
            get => _DayGiverToolBreakage;
            set => Set(ref _DayGiverToolBreakage, value);
        }

        private double? _DayGiverCoolantConcentration;
        public double? DayGiverCoolantConcentration
        {
            get => _DayGiverCoolantConcentration;
            set => Set(ref _DayGiverCoolantConcentration, value);
        }

        private bool? _DayRecieverWorkplaceCleaned;
        public bool? DayRecieverWorkplaceCleaned
        {
            get => _DayRecieverWorkplaceCleaned;
            set => Set(ref _DayRecieverWorkplaceCleaned, value);
        }

        private bool? _DayRecieverFailures;
        public bool? DayRecieverFailures
        {
            get => _DayRecieverFailures;
            set => Set(ref _DayRecieverFailures, value);
        }

        private bool? _DayRecieverExtraneousNoises;
        public bool? DayRecieverExtraneousNoises
        {
            get => _DayRecieverExtraneousNoises;
            set => Set(ref _DayRecieverExtraneousNoises, value);
        }

        private bool? _DayRecieverLiquidLeaks;
        public bool? DayRecieverLiquidLeaks
        {
            get => _DayRecieverLiquidLeaks;
            set => Set(ref _DayRecieverLiquidLeaks, value);
        }

        private bool? _DayRecieverToolBreakage;
        public bool? DayRecieverToolBreakage
        {
            get => _DayRecieverToolBreakage;
            set => Set(ref _DayRecieverToolBreakage, value);
        }

        private double? _DayRecieverCoolantConcentration;
        public double? DayRecieverCoolantConcentration
        {
            get => _DayRecieverCoolantConcentration;
            set => Set(ref _DayRecieverCoolantConcentration, value);
        }

        private bool? _NightGiverWorkplaceCleaned;
        public bool? NightGiverWorkplaceCleaned
        {
            get => _NightGiverWorkplaceCleaned;
            set => Set(ref _NightGiverWorkplaceCleaned, value);
        }

        private bool? _NightGiverFailures;
        public bool? NightGiverFailures
        {
            get => _NightGiverFailures;
            set => Set(ref _NightGiverFailures, value);
        }

        private bool? _NightGiverExtraneousNoises;
        public bool? NightGiverExtraneousNoises
        {
            get => _NightGiverExtraneousNoises;
            set => Set(ref _NightGiverExtraneousNoises, value);
        }

        private bool? _NightGiverLiquidLeaks;
        public bool? NightGiverLiquidLeaks
        {
            get => _NightGiverLiquidLeaks;
            set => Set(ref _NightGiverLiquidLeaks, value);
        }

        private bool? _NightGiverToolBreakage;
        public bool? NightGiverToolBreakage
        {
            get => _NightGiverToolBreakage;
            set => Set(ref _NightGiverToolBreakage, value);
        }

        private double? _NightGiverCoolantConcentration;
        public double? NightGiverCoolantConcentration
        {
            get => _NightGiverCoolantConcentration;
            set => Set(ref _NightGiverCoolantConcentration, value);
        }

        private bool? _NightRecieverWorkplaceCleaned;
        public bool? NightRecieverWorkplaceCleaned
        {
            get => _NightRecieverWorkplaceCleaned;
            set => Set(ref _NightRecieverWorkplaceCleaned, value);
        }

        private bool? _NightRecieverFailures;
        public bool? NightRecieverFailures
        {
            get => _NightRecieverFailures;
            set => Set(ref _NightRecieverFailures, value);
        }

        private bool? _NightRecieverExtraneousNoises;
        public bool? NightRecieverExtraneousNoises
        {
            get => _NightRecieverExtraneousNoises;
            set => Set(ref _NightRecieverExtraneousNoises, value);
        }

        private bool? _NightRecieverLiquidLeaks;
        public bool? NightRecieverLiquidLeaks
        {
            get => _NightRecieverLiquidLeaks;
            set => Set(ref _NightRecieverLiquidLeaks, value);
        }

        private bool? _NightRecieverToolBreakage;
        public bool? NightRecieverToolBreakage
        {
            get => _NightRecieverToolBreakage;
            set => Set(ref _NightRecieverToolBreakage, value);
        }

        private double? _NightRecieverCoolantConcentration;
        public double? NightRecieverCoolantConcentration
        {
            get => _NightRecieverCoolantConcentration;
            set => Set(ref _NightRecieverCoolantConcentration, value);
        }


        private bool _AdvancedMode;
        /// <summary> Расширенный режим </summary>
        public bool AdvancedMode
        {
            get => _AdvancedMode;
            set => Set(ref _AdvancedMode, value);
        }


        #region UpdateShiftInfo
        public ICommand UpdateShiftInfoCommand { get; }

        public string Error
        {
            get
            {
                if (string.IsNullOrWhiteSpace(this[nameof(CurrentMaster)]) &&
                    string.IsNullOrWhiteSpace(this[nameof(DayDowntimesReason)]) &&
                    string.IsNullOrWhiteSpace(this[nameof(NightDowntimesReason)]))
                {
                    return null!;
                }
                else
                {
                    return "Некорректное заполнение.";
                }
            }
        }

        public string this[string columnName] { get
            {
                switch (columnName)
                {
                    case nameof(CurrentMaster) when string.IsNullOrWhiteSpace(CurrentMaster):
                        return "Не выбран мастер";
                    case nameof(DayDowntimesReason) when UnspecifiedDayDowntimesNeedAttention && string.IsNullOrEmpty(DayDowntimesReason):
                        return "Не указана причина дневных простоев";
                    case nameof(NightDowntimesReason) when UnspecifiedNightDowntimesNeedAttention && string.IsNullOrEmpty(NightDowntimesReason):
                        return "Не указана причина ночных простоев";
                    default:
                        return null!;
                }
            } }

        private void OnUpdateShiftInfoCommandExecuted(object p)
        {
            if (!string.IsNullOrEmpty(Error))
            {
                MessageBoxWindow.Show("Некорректное заполнение.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (MessageBoxWindow.Show("Обновить информацию?", "Вы точно уверены?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxDefaultButton.No) is MessageBoxResult.No) 
                return;
            var dayShift = new ShiftInfo(null, ShiftDate, ShiftType.Day, Machine, CurrentMaster, UnspecifiedDayDowntimes, DayDowntimesReason, DayMasterComment, IsChecked, DayGiverWorkplaceCleaned, DayGiverFailures, DayGiverExtraneousNoises, DayGiverLiquidLeaks, DayGiverToolBreakage, DayGiverCoolantConcentration, DayRecieverWorkplaceCleaned, DayRecieverFailures, DayRecieverExtraneousNoises, DayRecieverLiquidLeaks, DayRecieverToolBreakage, DayRecieverCoolantConcentration);
            var nightShift = new ShiftInfo(null, ShiftDate, ShiftType.Night, Machine, CurrentMaster, UnspecifiedNightDowntimes, NightDowntimesReason, NightMasterComment, IsChecked, NightGiverWorkplaceCleaned, NightGiverFailures, NightGiverExtraneousNoises, NightGiverLiquidLeaks, NightGiverToolBreakage, NightGiverCoolantConcentration, NightRecieverWorkplaceCleaned, NightRecieverFailures, NightRecieverExtraneousNoises, NightRecieverLiquidLeaks, NightRecieverToolBreakage, NightRecieverCoolantConcentration);
            var dayWriteResult = Database.WriteShiftInfo(dayShift);
            switch (dayWriteResult.Status)
            {
                case remeLog.Core.Db.DbResult.Ok:
                    Status = "Информация о дневной смене записана.";
                    break;
                case remeLog.Core.Db.DbResult.AuthError:
                    Status = "Ошибка авторизации.";
                    break;
                case remeLog.Core.Db.DbResult.Error:
                    Status = "Ошибка авторизации.";
                    break;
                case remeLog.Core.Db.DbResult.NoConnection:
                    Status = "Нет соединения с БД.";
                    break;
            }
            var nightWriteResult = Database.WriteShiftInfo(nightShift);
            switch (nightWriteResult.Status)
            {
                case remeLog.Core.Db.DbResult.Ok when dayWriteResult.Status is remeLog.Core.Db.DbResult.Ok:
                    Status = "Информация о сменах записана.";
                    break;
                case remeLog.Core.Db.DbResult.Ok:
                    Status = "Информация о ночной смене записана.";
                    break;
                case remeLog.Core.Db.DbResult.AuthError:
                    Status = "Ошибка авторизации.";
                    break;
                case remeLog.Core.Db.DbResult.Error:
                    Status = "Ошибка БД.";
                    break;
                case remeLog.Core.Db.DbResult.NoConnection:
                    Status = "Нет соединения с БД.";
                    break;
            }
        }
        private static bool CanUpdateShiftInfoCommandExecute(object p) => true;
        #endregion

    }
}
