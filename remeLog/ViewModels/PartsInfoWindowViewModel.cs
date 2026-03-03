using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Office2019.Excel.RichData2;
using libeLog;
using libeLog.Base;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Models;
using remeLog.Infrastructure;
using remeLog.Infrastructure.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Infrastructure.Winnum;
using remeLog.Infrastructure.Winnum.Data;
using remeLog.Models;
using remeLog.Views;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static remeLog.Infrastructure.Winnum.Types;
using Database = remeLog.Infrastructure.Database;
using Part = remeLog.Models.Part;

namespace remeLog.ViewModels
{

    public class PartsInfoWindowViewModel : ViewModel, IDataErrorInfo
    {
        private static readonly SemaphoreSlim semaphoreSlim = new(1, 1);
        private CancellationTokenSource _cancellationTokenSource = new();
        private CancellationTokenSource _wncCancellationTokenSource = new();
        private Dictionary<string, MultiValueEditorWindow> _editors = new();
        private static bool lockUpdate;

        public PartsInfoWindowViewModel(CombinedParts parts)
        {
            lockUpdate = true;
            ChangeCalcFixedCommand = new LambdaCommand(OnChangeCalcFixedCommandExecuted, CanChangeCalcFixedCommandExecute);
            ChangeCompactViewCommand = new LambdaCommand(OnChangeCompactViewCommandExecuted, CanChangeCompactViewCommandExecute);
            ChangeRoleCommand = new LambdaCommand(OnChangeRoleCommandExecuted, CanChangeRoleCommandExecute);
            ChangeShowUncheckedCommand = new LambdaCommand(OnChangeShowUncheckedCommandExecuted, CanChangeShowUncheckedCommandExecute);
            CheckAssignmentWithFactCommand = new LambdaCommand(OnCheckAssignmentWithFactCommandExecuted, CanCheckAssignmentWithFactCommandExecute);
            ClearContentCommand = new LambdaCommand(OnClearContentCommandExecuted, CanClearContentCommandExecute);
            DecreaseDateCommand = new LambdaCommand(OnDecreaseDateCommandExecuted, CanDecreaseDateCommandExecute);
            DecreaseSetupCommand = new LambdaCommand(OnDecreaseSetupCommandExecuted, CanDecreaseSetupCommandExecute);
            DeleteFilterCommand = new LambdaCommand(OnDeleteFilterCommandExecuted, CanDeleteFilterCommandExecute);
            DeletePartCommand = new LambdaCommand(OnDeletePartCommandExecuted, CanDeletePartCommandExecute);
            ChangeSerialPartNormativesCommand = new LambdaCommand(OnChangeSerialPartNormativesCommandExecuted, CanChangeSerialPartNormativesCommandExecute);
            ExportHistoryToExcelCommand = new LambdaCommand(OnExportHistoryToExcelCommandExecuted, CanExportHistoryToExcelCommandExecute);
            ExportLongSetupsCommand = new LambdaCommand(OnExportLongSetupsCommandExecuted, CanExportLongSetupsCommandExecute);
            ExportPartsReportToExcelCommand = new LambdaCommand(OnExportPartsReportToExcelCommandExecuted, CanExportPartsReportToExcelCommandExecute);
            ExportReportForPeriodToExcelCommand = new LambdaCommand(OnExportReportForPeriodToExcelCommandExecuted, CanExportReportForPeriodToExcelCommandExecute);
            ExportNewReportForPeriodToExcelCommand = new LambdaCommand(OnExportNewReportForPeriodToExcelCommandExecuted, CanExportNewReportForPeriodToExcelCommandExecute);
            ExportShiftsInfoReportCommand = new LambdaCommand(OnExportShiftsInfoReportCommandExecuted, CanExportShiftsInfoReportCommandExecute);
            ExportToExcelCommand = new LambdaCommand(OnExportToExcelCommandExecuted, CanExportToExcelCommandExecute);
            ExportToolSearchCasesToExcelCommand = new LambdaCommand(OnExportToolSearchCasesToExcelCommandExecuted, CanExportToolSearchCasesToExcelCommandExecute);
            ExportVerevkinReportCommand = new LambdaCommand(OnExportVerevkinReportCommandExecuted, CanExportVerevkinReportCommandExecute);
            HideAllMachinesCommand = new LambdaCommand(OnHideAllMachinesCommandExecuted, CanHideAllMachinesCommandExecute);
            IncreaseDateCommand = new LambdaCommand(OnIncreaseDateCommandExecuted, CanIncreaseDateCommandExecute);
            IncreaseSetupCommand = new LambdaCommand(OnIncreaseSetupCommandExecuted, CanIncreaseSetupCommandExecute);
            InvertMachinesCommand = new LambdaCommand(OnInvertMachinesCommandExecuted, CanInvertMachinesCommandExecute);
            NormsAndWorkloadAnalysisCommand = new LambdaCommand(OnNormsAndWorkloadAnalysisCommandExecuted, CanNormsAndWorkloadAnalysisCommandExecute);
            OpenDailyReportWindowCommand = new LambdaCommand(OnOpenDailyReportWindowCommandExecuted, CanOpenDailyReportWindowCommandExecute);
            OperatorReportToExcelCommand = new LambdaCommand(OnOperatorReportToExcelCommandExecuted, CanOperatorReportToExcelCommandExecute);
            OperatorsShiftsReportToExcelCommand = new LambdaCommand(OnOperatorsShiftsReportToExcelCommandExecuted, CanOperatorsShiftsReportToExcelCommandExecute);
            RefreshPartsCommand = new LambdaCommand(OnRefreshPartsCommandExecuted, CanRefreshPartsCommandExecute);
            SearchInWindchillCommand = new LambdaCommand(OnSearchInWindchillCommandExecuted, CanSearchInWindchillCommandExecute);
            SearchInWinnumCommand = new LambdaCommand(OnSearchInWinnumCommandExecuted, CanSearchInWinnumCommandExecute);
            SetAllDateCommand = new LambdaCommand(OnSetAllDateCommandExecuted, CanSetAllDateCommandExecute);
            SetHorMillMachinesCommand = new LambdaCommand(OnSetHorMillMachinesCommandExecuted, CanSetHorMillMachinesCommandExecute);
            SetLatheMachinesCommand = new LambdaCommand(OnSetLatheMachinesCommandExecuted, CanSetLatheMachinesCommandExecute);
            SetMillMachinesCommand = new LambdaCommand(OnSetMillMachinesCommandExecuted, CanSetMillMachinesCommandExecute);
            SetMonthDateCommand = new LambdaCommand(OnSetMonthDateCommandExecuted, CanSetMonthDateCommandExecute);
            SetWeekDateCommand = new LambdaCommand(OnSetWeekDateCommandExecuted, CanSetWeekDateCommandExecute);
            SetVerMillMachinesCommand = new LambdaCommand(OnSetVerMillMachinesCommandExecuted, CanSetVerMillMachinesCommandExecute);
            SetYearDateCommand = new LambdaCommand(OnSetYearDateCommandExecuted, CanSetYearDateCommandExecute);
            SetYesterdayDateCommand = new LambdaCommand(OnSetYesterdayDateCommandExecuted, CanSetYesterdayDateCommandExecute);
            ShowAllMachinesCommand = new LambdaCommand(OnShowAllMachinesCommandExecuted, CanShowAllMachinesCommandExecute);
            ShowArchiveTableCommand = new LambdaCommand(OnShowArchiveTableCommandExecuted, CanShowArchiveTableCommandExecute);
            ShowInfoCommand = new LambdaCommand(OnShowInfoCommandExecuted, CanShowInfoCommandExecute);
            UnlockSerialPartNormativesCommand = new LambdaCommand(OnUnlockSerialPartNormativesCommandExecuted, CanUnlockSerialPartNormativesCommandExecute);
            OpenMultiValueEditorCommand = new LambdaCommand(OnOpenMultiValueEditorCommandExecuted, CanOpenMultiValueEditorCommandExecute);
            UpdatePartsCommand = new LambdaCommand(OnUpdatePartsCommandExecutedAsync, CanUpdatePartsCommandExecute);

            CalcFixed = Part.CalcFixed;
            PartsInfo = parts;
            ShiftFilterItems = new Shift[3] { new(ShiftType.All), new(ShiftType.Day), new(ShiftType.Night) };
            _ShiftFilter = ShiftFilterItems.FirstOrDefault();
            _OperatorFilter = "";
            _FinishedCountFilter = "";
            _Parts = new();
            _OrderFilter = "";
            _PartNameFilter = "";
            _EngineerCommentFilter = "";
            _TotalCountFilter = "";
            _FromDate = PartsInfo.FromDate;
            _ToDate = PartsInfo.ToDate;
            _MachineFilters = new();
            _MachineFilters.CollectionChanged += MachineFiltersSource_CollectionChanged!;
            _SerialParts = new();
            _Parts.CollectionChanged += _Parts_CollectionChanged;
            _ViewMode = AppSettings.Instance.User ??= User.Viewer;
            foreach (Part part in _Parts)
            {
                PropertyChanged += Part_PropertyChanged!;
            }
            UpdateHasErrors();
            OnPropertyChanged(nameof(HasErrors));
            _ = Init();
            
        }

        private void _Parts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (Part item in e.NewItems)
                    item.PropertyChanged += Part_PropertyChanged!;

            if (e.OldItems != null)
                foreach (Part item in e.OldItems)
                    item.PropertyChanged -= Part_PropertyChanged!;
        }

        async Task Init()
        {
            await _MachineFilters.ReadMachines();
            
            foreach (var machineFilter in MachineFilters)
            {
                machineFilter.Filter = machineFilter.Machine == PartsInfo.Machine || PartsInfo.Machine == "Все станки";
            }

            SerialParts = await libeLog.Infrastructure.Database.GetSerialPartsAsync(AppSettings.Instance.ConnectionString!);

            lockUpdate = false;

            await LoadPartsAsync();
        }

        private void Part_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            UpdateHasErrors();
        }

        private void MachineFiltersSource_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (MachineFilter item in e.NewItems)
                    item.PropertyChanged += MachineFilters_PropertyChanged!;

            if (e.OldItems != null)
                foreach (MachineFilter item in e.OldItems)
                    item.PropertyChanged -= MachineFilters_PropertyChanged!;
        }

        private async void MachineFilters_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(nameof(MachineVisibility));
            if (e.PropertyName == "Filter")
                await LoadPartsAsync();
        }


        private bool _HasErrors;
        /// <summary> Есть ли ошибки валидации </summary>
        public bool HasErrors
        {
            get => _HasErrors;
            set 
            {
                if (Set(ref _HasErrors, value))
                {
                    OnPropertyChanged(nameof(HasErrors));
                }
            }
        }

        public Shift[] ShiftFilterItems { get; set; }

        private ObservableCollection<Part> _Parts;
        /// <summary> Описание </summary>
        public ObservableCollection<Part> Parts
        {
            get => _Parts;
            set
            {
                Set(ref _Parts, value);
                OnPropertyChanged(nameof(SetupTimeRatio));
                OnPropertyChanged(nameof(AverageSetupRatio));
                OnPropertyChanged(nameof(ProductionTimeRatio));
                OnPropertyChanged(nameof(AverageProductionRatio));
                OnPropertyChanged(nameof(SpecifiedDowntimesRatio));
                OnPropertyChanged(nameof(UnspecifiedDowntimesRatio));
            }
        }


        private DateTime _FromDate;
        /// <summary> Начальная дата </summary>
        public DateTime FromDate
        {
            get => _FromDate;
            set
            {
                if (!CanBeChanged()) return;
                if (Set(ref _FromDate, value))
                {
                    _ = LoadPartsAsync();
                }
            }
        }

        private DateTime _ToDate;
        /// <summary> Конечная дата </summary>
        public DateTime ToDate
        {
            get => _ToDate;
            set
            {
                if (!CanBeChanged()) return;
                if (Set(ref _ToDate, value))
                {
                    _ = LoadPartsAsync();
                }
            }
        }
        #region Фильтры

        private Shift _ShiftFilter;
        /// <summary> Фильтр смены </summary>
        public Shift ShiftFilter
        {
            get => _ShiftFilter;
            set
            {
                if (!CanBeChanged()) return;
                if (Set(ref _ShiftFilter, value))
                {
                    _ = LoadPartsAsync();
                }
            }
        }


        private string _OperatorFilter;
        /// <summary> Фильтр оператора </summary>
        public string OperatorFilter
        {
            get => _OperatorFilter;
            set
            {
                if (!CanBeChanged()) return;
                if (Set(ref _OperatorFilter, value))
                {
                    _ = LoadPartsAsync();
                }
            }
        }

        private string _PartNameFilter;
        /// <summary> Фильтр названия детали </summary>
        public string PartNameFilter
        {
            get => _PartNameFilter;
            set
            {
                if (!CanBeChanged()) return;
                if (Set(ref _PartNameFilter, value))
                {
                    _ = LoadPartsAsync();
                }
            }
        }

        private string _OrderFilter;
        /// <summary> Фильтр названия детали </summary>
        public string OrderFilter
        {
            get => _OrderFilter;
            set
            {
                if (!CanBeChanged()) return;
                if (Set(ref _OrderFilter, value))
                {
                    _ = LoadPartsAsync();
                }
            }
        }


        private int? _SetupFilter;
        /// <summary> Описание </summary>
        public int? SetupFilter
        {
            get => _SetupFilter;
            set
            {
                if (!CanBeChanged()) return;
                if (Set(ref _SetupFilter, value))
                {
                    _ = LoadPartsAsync();
                }
            }
        }


        private string _FinishedCountFilter;
        /// <summary> фильтр по завершенным деталям </summary>
        public string FinishedCountFilter
        {
            get => _FinishedCountFilter;
            set
            {
                if (!CanBeChanged()) return;
                if (Set(ref _FinishedCountFilter, value))
                {
                    _ = LoadPartsAsync();
                }
            }
        }


        private string _TotalCountFilter;
        /// <summary> фильтр по количеству деталей в маршрутном листе </summary>
        public string TotalCountFilter
        {
            get => _TotalCountFilter;
            set
            {
                if (!CanBeChanged()) return;
                if (Set(ref _TotalCountFilter, value))
                {
                    _ = LoadPartsAsync();
                }
            }
        }


        private string _EngineerCommentFilter;
        /// <summary> Фильтр по комментарию техотдела </summary>
        public string EngineerCommentFilter
        {
            get => _EngineerCommentFilter;
            set
            {
                if (!CanBeChanged()) return;
                if (Set(ref _EngineerCommentFilter, value))
                {
                    _ = LoadPartsAsync();
                }
            }
        }

        private bool _OnlySerialPartsFilter;
        /// <summary> Фильтр только по серийным деталям </summary>
        public bool OnlySerialPartsFilter
        {
            get => _OnlySerialPartsFilter;

            set 
            {
                if (!CanBeChanged()) return;
                if (Set(ref _OnlySerialPartsFilter, value))
                {
                    _ = LoadPartsAsync();
                }
            } 
        }


        private ObservableCollection<MachineFilter> _MachineFilters;
        /// <summary> Список станков с необходимостью фильтрации </summary>
        public ObservableCollection<MachineFilter> MachineFilters
        {
            get => _MachineFilters;
            set
            {
                if (!CanBeChanged()) return;
                if (Set(ref _MachineFilters, value))
                {
                    _ = LoadPartsAsync();
                }
            }
        }
        #endregion

        private Part? _SelectedPart;
        /// <summary> Выбранная деталь </summary>
        public Part? SelectedPart
        {
            get => _SelectedPart;
            set => Set(ref _SelectedPart, value);
        }


        private List<SerialPart> _SerialParts;
        /// <summary> Серийные детали </summary>
        public List<SerialPart> SerialParts
        {
            get => _SerialParts;
            set => Set(ref _SerialParts, value);
        }


        public CombinedParts PartsInfo { get; set; }


        private bool _InProgress;
        /// <summary> Загрузка информации </summary>
        public bool InProgress
        {
            get => _InProgress;
            set => Set(ref _InProgress, value);
        }


        private bool _WncSearchInProgress;
        /// <summary> Поиск по Windchill в процессе </summary>
        public bool WncSearchInProgress
        {
            get => _WncSearchInProgress;
            set => Set(ref _WncSearchInProgress, value);
        }


        private string _Status = string.Empty;
        /// <summary> Статус </summary>
        public string Status
        {
            get => _Status;
            set => Set(ref _Status, value);
        }


        private bool _CompactView = false;
        /// <summary> Компактный режим (на самом деле уже нет, это для sfGridView) </summary>
        public bool CompactView
        {
            get => _CompactView;
            set => Set(ref _CompactView, value);
        }


        private User _ViewMode;
        /// <summary> Режим просмотра </summary>
        public User ViewMode
        {
            get => _ViewMode;
            set => Set(ref _ViewMode, value);
        }


        private bool _ViewUnchecked;
        /// <summary> Описание </summary>
        public bool ViewUnchecked
        {
            get => _ViewUnchecked;
            set => Set(ref _ViewUnchecked, value);
        }

        private Overlay _Overlay = new(false);

        public Overlay Overlay
        {
            get => _Overlay;
            set => Set(ref _Overlay, value);
        }

        public List<string> SetupReasons => AppSettings.Instance.SetupReasons.Select(x => x.Reason).ToList();
        public Dictionary<string, bool> SetupReasonsRequireComment =>
            AppSettings.Instance.SetupReasons.ToDictionary(x => x.Reason, x => x.RequireComment);
        public List<string> MachiningReasons => AppSettings.Instance.MachiningReasons.Select(x => x.Reason).ToList();
        public Dictionary<string, bool> MachiningReasonsRequireComment =>
            AppSettings.Instance.MachiningReasons.ToDictionary(x => x.Reason, x => x.RequireComment);

        public double AverageSetupRatio =>
            MachineFilters.Any(f => f.Filter)
            ? Parts.AverageSetupRatio()
            : double.NaN;
        public double AverageProductionRatio =>
            MachineFilters.Any(f => f.Filter)
            ? Parts.AverageProductionRatio()
            : double.NaN;
        public double SetupTimeRatio =>
            MachineFilters.Any(f => f.Filter)
            ? Parts.SetupRatio()
            : double.NaN;
        public double ProductionTimeRatio =>
            MachineFilters.Any(f => f.Filter)
            ? Parts.ProductionRatio()
            : double.NaN;
        public double SpecifiedDowntimesRatio =>
            MachineFilters.Any(f => f.Filter)
            ? Parts.SpecifiedDowntimesRatio(ShiftFilter)
            : double.NaN;
        public double UnspecifiedDowntimesRatio =>
            MachineFilters.Any(f => f.Filter)
            ? Parts.UnspecifiedDowntimesRatio(FromDate, ToDate, ShiftFilter)
            : double.NaN;


        private TimeOnly _EndDateForCalc;
        /// <summary> Конечная дата для расчета </summary>
        public TimeOnly EndDateForCalc
        {
            get => _EndDateForCalc;
            set
            {
                if (Set(ref _EndDateForCalc, value))
                {
                    OnPropertyChanged(nameof(CalculatedTimeDifference));
                    OnPropertyChanged(nameof(CalculatedTimeDifferenceMinutes));
                }
            }
        }

        private TimeOnly _StartDateForCalc;
        /// <summary> Начальная дата для расчета </summary>
        public TimeOnly StartDateForCalc
        {
            get => _StartDateForCalc;
            set
            {
                if (Set(ref _StartDateForCalc, value))
                {
                    OnPropertyChanged(nameof(CalculatedTimeDifference));
                    OnPropertyChanged(nameof(CalculatedTimeDifferenceMinutes));
                }
            }
        }

        public TimeSpan CalculatedTimeDifference
        {
            get
            {
                if (StartDateForCalc == TimeOnly.MinValue || EndDateForCalc == TimeOnly.MaxValue) return TimeSpan.Zero;
                var breakfasts = DescreaseTimes ? TimeOnlys.GetBreaksBetween(StartDateForCalc, EndDateForCalc) : TimeSpan.Zero;
                return EndDateForCalc - StartDateForCalc - breakfasts - new TimeSpan(0, AdditionalDescreaseValue ?? 0, 0);
            }
        }

        public double CalculatedTimeDifferenceMinutes
            => CalculatedTimeDifference.TotalMinutes;


        private bool _CalcFixed;
        /// <summary> Описание </summary>
        public bool CalcFixed
        {
            get => _CalcFixed;
            set => Set(ref _CalcFixed, value);
        }


        private bool _DescreaseTimes;
        /// <summary> Описание </summary>
        public bool DescreaseTimes
        {
            get => _DescreaseTimes;
            set
            {
                if (Set(ref _DescreaseTimes, value))
                {
                    OnPropertyChanged(nameof(CalculatedTimeDifference));
                    OnPropertyChanged(nameof(CalculatedTimeDifferenceMinutes));
                }
            }
        }


        private int? _AdditionalDescreaseValue = null;
        /// <summary> Время для дополнительного вычитания в калькуляторе времени, также используется как разделитель для серийности в отчёте за период </summary>
        public int? AdditionalDescreaseValue
        {
            get => _AdditionalDescreaseValue;
            set
            {
                if (Set(ref _AdditionalDescreaseValue, value))
                {
                    OnPropertyChanged(nameof(CalculatedTimeDifference));
                    OnPropertyChanged(nameof(CalculatedTimeDifferenceMinutes));
                }
            }
        }


        private bool _UseMockData = false;
        /// <summary>
        /// Фиктивные данные для отладки
        /// </summary>
        public bool UseMockData
        {
            get => _UseMockData;
            set => Set(ref _UseMockData, value);
        }


        public bool MachineVisibility => MachineFilters.Count(m => m.Filter == true) == 1;

        #region IncreaseDateCommand
        public ICommand IncreaseDateCommand { get; }
        private void OnIncreaseDateCommandExecuted(object p)
        {
            LockUpdate();
            FromDate = FromDate.AddDays(1);
            ToDate = ToDate.AddDays(1);
            UnlockUpdate();
        }
        private bool CanIncreaseDateCommandExecute(object p) => true;
        #endregion

        #region DecreaseDateCommand
        public ICommand DecreaseDateCommand { get; }
        private void OnDecreaseDateCommandExecuted(object p)
        {
            LockUpdate();
            FromDate = FromDate.AddDays(-1);
            ToDate = ToDate.AddDays(-1);
            UnlockUpdate();
        }
        private bool CanDecreaseDateCommandExecute(object p) => true;
        #endregion

        #region SetYesterdayDateCommand
        public ICommand SetYesterdayDateCommand { get; }
        private void OnSetYesterdayDateCommandExecuted(object p)
        {
            LockUpdate();
            FromDate = DateTime.Today.AddDays(-1);
            ToDate = FromDate;
            UnlockUpdate();
        }
        private bool CanSetYesterdayDateCommandExecute(object p) => true;
        #endregion

        #region SetWeekDateCommand
        public ICommand SetWeekDateCommand { get; }
        private void OnSetWeekDateCommandExecuted(object p)
        {
            FromDate = ToDate.AddDays(-7);
        }
        private bool CanSetWeekDateCommandExecute(object p) => true;
        #endregion

        #region SetMonthDateCommand
        public ICommand SetMonthDateCommand { get; }
        private void OnSetMonthDateCommandExecuted(object p)
        {
            LockUpdate();
            if (FromDate == ToDate.AddDays(-30))
            {
                FromDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                ToDate = DateTime.Today.AddDays(-1);
            }
            else
            {
                FromDate = ToDate.AddDays(-30);
            }
            UnlockUpdate();
        }
        private bool CanSetMonthDateCommandExecute(object p) => true;
        #endregion

        #region SetYearDateCommand
        public ICommand SetYearDateCommand { get; }
        private void OnSetYearDateCommandExecuted(object p)
        {
            LockUpdate();
            FromDate = new DateTime(2024, 01, 01);
            ToDate = DateTime.Today;
            UnlockUpdate();
        }
        private bool CanSetYearDateCommandExecute(object p) => true;
        #endregion

        #region SetAllDateCommand
        public ICommand SetAllDateCommand { get; }
        private void OnSetAllDateCommandExecuted(object p)
        {
            LockUpdate();
            FromDate = new DateTime(2023, 01, 01, 00, 00, 00);
            ToDate = DateTime.Today;
            UnlockUpdate();
        }
        private bool CanSetAllDateCommandExecute(object p) => true;
        #endregion

        #region IncreaseSetupCommand
        public ICommand IncreaseSetupCommand { get; }
        private void OnIncreaseSetupCommandExecuted(object p)
        {
            if (SetupFilter is null)
            {
                SetupFilter = 1;
            }
            else if (SetupFilter is int i && i < int.MaxValue)
            {
                SetupFilter++;
            }
        }
        private bool CanIncreaseSetupCommandExecute(object p) => true;
        #endregion

        #region DecreaseSetupCommand
        public ICommand DecreaseSetupCommand { get; }
        private void OnDecreaseSetupCommandExecuted(object p)
        {
            if (SetupFilter is int i && i > 0)
            {
                if (SetupFilter == 1)
                {
                    SetupFilter = null;
                }
                else
                {
                    SetupFilter--;
                }
            }
        }
        private bool CanDecreaseSetupCommandExecute(object p) => true;
        #endregion

        #region ClearContent
        public ICommand ClearContentCommand { get; }
        private void OnClearContentCommandExecuted(object p)
        {
            if (p is TextBox textBox)
            {
                textBox.Text = "";
            }
        }
        private static bool CanClearContentCommandExecute(object p) => true;
        #endregion

        #region SearchInWindchill
        public ICommand SearchInWindchillCommand { get; }
        private async void OnSearchInWindchillCommandExecuted(object p)
        {
            if (p is PartsInfoWindow w)
            {
                _wncCancellationTokenSource?.Cancel();
                _wncCancellationTokenSource?.Dispose();
                _wncCancellationTokenSource = new CancellationTokenSource();

                _wncCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(30));
                var cancellationToken = _wncCancellationTokenSource.Token;

                try
                {
                    WncSearchInProgress = true;
                    int stableObjectCount = 0;
                    int previousObjectCount = 0;
                    List<WncObject> wncObjects = new();
                    Status = "Поиск в Windchill...";

                    while (stableObjectCount < 3)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var searchResult = await Util.SearchInWindchill(PartNameFilter, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        wncObjects = Util.ExtractWncObjects(searchResult);
                        if (wncObjects.Count == previousObjectCount)
                        {
                            stableObjectCount++;
                        }
                        else
                        {
                            stableObjectCount = 0;
                            previousObjectCount = wncObjects.Count;
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Delay(200, cancellationToken);
                    }

                    Util.Debug(wncObjects);
                    if (wncObjects.Any())
                    {
                        var wncObjectsWindow = new WncObjectsWindow(wncObjects.ToObservableCollection()) { Owner = w };
                        wncObjectsWindow.Show();
                    }
                    else
                    {
                        MessageBox.Show("Ничего не найдено :с", ":c", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (TaskCanceledException)
                {
                    
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    WncSearchInProgress = false;
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Status = "Отменено";
                        await Task.Delay(2000);
                    }
                    Status = "";
                }
            }

        }
        private bool CanSearchInWindchillCommandExecute(object p) => !string.IsNullOrWhiteSpace(PartNameFilter) && p is PartsInfoWindow;
        #endregion

        #region SearchInWinnum
        public ICommand SearchInWinnumCommand { get; }
        private async void OnSearchInWinnumCommandExecuted(object p)
        {
            if (p is Part part && part == SelectedPart)
            {
                IProgress<string> progress = new Progress<string>(m => Status = m);
                try
                {
                    if (SelectedPart == null || string.IsNullOrEmpty(AppSettings.Instance.ConnectionString)) return;
                    progress.Report("Получение данных о станках");
                    var machines = await Database.GetMachinesAsync(progress);
                    var machine = machines.FirstOrDefault(m => m.Name == SelectedPart.Machine);
                    if (machine == null)
                    {
                        progress.Report("Не удалось сопоставить станок указанный при изготовлении со станком в Winnum");
                        await Task.Delay(3000);
                        return;
                    }
                    progress.Report("Чтение конфигурации Winnum");
                    var winnumConfig = await libeLog.Infrastructure.Database.GetWinnumConfigAsync(AppSettings.Instance.ConnectionString);
                    var winnumClient = new ApiClient(winnumConfig.BaseUri, winnumConfig.User, winnumConfig.Pass);
                    var operation = new Operation(winnumClient, machine);
                    var signal = new Signal(winnumClient, machine);
                    progress.Report("Чтение данных из Winnum");

                    var startTime = SelectedPart.StartMachiningTime;

                    var platformDateTimeTask = operation.GetPlatformDateTimeAsync(progress);
                    var cloudDateTimeTask = operation.GetCloudDateTimeAsync(progress);
                    var priorityTagDurationTask = operation.GetPriorityTagDurationAsync(AppId.CNC_MONITORING, startTime, SelectedPart.EndMachiningTime, progress);
                    var tagIntervalCalculationTask = operation.GetTagIntervalCalculationAsync(AppId.CNC_MONITORING, TagId.NC_PROGRAM_RUN, startTime, SelectedPart.EndMachiningTime, progress, base_shift: false);
                    var simpleTagIntervalCalculationTask = operation.GetSimpleTagIntervalCalculationAsync(AppId.CNC_MONITORING, TagId.NC_PROGRAM_RUN, startTime, SelectedPart.EndMachiningTime, progress);
                    var operationsSummaryTask = operation.GetOperationSummaryAsync(AppId.CNC_MONITORING, startTime, SelectedPart.EndMachiningTime, progress);
                    var completedQtyTask = operation.GetCompletedQtyAsync(AppId.CNC_MONITORING, startTime, SelectedPart.EndMachiningTime, progress);
                    var startCountTask = signal.GetSignalAsync(machine.WnCounterSignal, SignalType.ByTime, Ordering.Asc, startTime.AddMinutes(-5), SelectedPart.EndMachiningTime.AddMinutes(5), progress: progress) ;
                    var endCountTask = signal.GetSignalAsync(machine.WnCounterSignal, SignalType.ByTime, Ordering.Asc, startTime.AddMinutes(-5), SelectedPart.EndMachiningTime.AddMinutes(5), progress: progress);
                    var programNamesTask = signal.GetUniqSignalsAsync(machine.WnNcProgramNameSignal, Ordering.Asc, startTime.AddMinutes(-5), SelectedPart.EndMachiningTime.AddMinutes(5), progress: progress);
                    var machineInfoTask = operation.GetMachineInfo(AppId.CNC_MONITORING, progress);

                    await Task.WhenAll(
                        platformDateTimeTask, 
                        cloudDateTimeTask, 
                        priorityTagDurationTask, 
                        tagIntervalCalculationTask, 
                        simpleTagIntervalCalculationTask, 
                        operationsSummaryTask,
                        completedQtyTask, 
                        startCountTask, 
                        endCountTask, 
                        machineInfoTask);

                    progress.Report("Разбор полученных данных и вычисления");

                    var platformDateTime = await platformDateTimeTask;
                    var cloudDateTime = await cloudDateTimeTask;
                    var priorityTagDuration = await priorityTagDurationTask;
                    var tagIntervalCalculation = await tagIntervalCalculationTask;
                    var simpleTagIntervalCalculation = await simpleTagIntervalCalculationTask;
                    var operationsSummary = await operationsSummaryTask;
                    var completedQty = await completedQtyTask;
                    var startCount = await startCountTask;
                    var endCount = await endCountTask;
                    var programNamesRaw = await programNamesTask;
                    var machineInfoRaw = await machineInfoTask;

                    
                    var tagIntervalCalculations = Parser.ParseTimeIntervals(Parser.ParseXmlItems(tagIntervalCalculation), true);
                    var orderedTagIntervalCalculations = tagIntervalCalculations.OrderBy(x => x.Start);
                    var priorityTagDurations = Parser.ParsePriorityTagDurations(priorityTagDuration, startTime, SelectedPart.EndMachiningTime);
                    var programNames = Parser.ParseXmlItems(programNamesRaw).Select(x => x["value"]).OrderBy(x => x).ToList();
                    var machineInfo = Parser.ParseXmlItems(machineInfoRaw).First();
                    var serialNumber = machineInfo["SerialNumber"];

                    var h1 = double.Parse(Parser.ParseXmlItems(tagIntervalCalculation).First()["hours"]);
                    var h2 = double.Parse(Parser.ParseXmlItems(simpleTagIntervalCalculation).First()["hours"]);
                    var h3 = TimeSpan.FromHours(priorityTagDurations.Where(p => p.Tag == "Программа выполняется").Sum(x => x.Duration)).TotalHours;
                    double? completed = null;
                    if (Parser.ParseXmlItems(startCount) is { Count: > 0} sc && Parser.ParseXmlItems(endCount) is { Count: > 0} fc)
                    {
                        completed = double.Parse(fc.Last()["value"]) - int.Parse(sc.First()["value"]);
                    }
                    if (completed.HasValue && completed.Value == 0) completed = SelectedPart.FinishedCount;

                    var m1 = h1 * 60 / SelectedPart.FinishedCount;
                    var m2 = h2 * 60 / SelectedPart.FinishedCount;
                    var m3 = h3 * 60 / SelectedPart.FinishedCount;

                    var intervals = orderedTagIntervalCalculations.Select(interval => new TimeInterval(interval.Start, interval.End)).ToList();
                    var sb = new StringBuilder();

                    var start = startTime.AddMinutes(-5);
                    var end = SelectedPart.EndMachiningTime.AddMinutes(5);
                    var signalNames = Enumerable.Range(0, 2000).Select(i => $"A{i}").ToList();
                    var signals = new ConcurrentDictionary<string, ConcurrentBag<string>>();

                    if (Parser.TryParseXmlItems(completedQty, out var cqty))
                    {
                        foreach (var item in cqty)
                        {
                            sb.Append("УП: ");
                            sb.Append(item["program_name"]);
                            sb.Append(" | Завершена раз: ");
                            sb.Append(item["count_completed"]);
                            sb.Append(" | Прервана раз: ");
                            sb.Append(item["count_incompleted"]);
                            sb.Append(" | Счётчик: ");
                            sb.AppendLine(item["count_parts"]);
                        }
                    }
                    
                    progress.Report("Подготовка окна");
                    sb.AppendLine($"Файлы УП: {string.Join(", ", programNames)}");
                    var completedInfo = sb.ToString();

                    progress.Report("Построение сводной таблицы");

                    var timelineSources = new List<TimelineSource>();
                    var tagsTimelineSource = TimelineSource.FromPriorityTag(
                        displayName: "Тэг",
                        fetchAsync: () => operation.GetPriorityTagDurationAsync(
                            AppId.CNC_MONITORING_WITH_STOPS_AND_MANUAL,
                            start, end),
                        filterStart: start,
                        filterEnd: end);
                    timelineSources.Add(tagsTimelineSource);
                    timelineSources.AddRange(BuildMachineTimelineSources(signal, machine, start, end));

                    var timeline = await TimelineBuilder.BuildAsync(timelineSources, TimeSpan.FromSeconds(5), progress);

                    var win = new WinnumInfoWindow($"" +
                        $"Локальное время:      {DateTime.Now:g} │ М/В Вар.1:   {(double.IsFinite(m1) ? $"{TimeSpan.FromMinutes(m1):hh\\:mm\\:ss}" : "00:00:00")} │\n" +
                        $"Время на платформе:   {platformDateTime:g} │ М/В Вар.2:   {(double.IsFinite(m2) ? $"{TimeSpan.FromMinutes(m2):hh\\:mm\\:ss}" : "00:00:00")} │\n" +
                        $"Время на облаке:      {cloudDateTime:g} │ М/В Вар.3:   {(double.IsFinite(m3) ? $"{TimeSpan.FromMinutes(m3):hh\\:mm\\:ss}" : "00:00:00")} │\n" +
                        $"───────────────────────────────────────┴───────────────────────┘\n" +
                        $"Выполнено по счётчику Winnum: {(completed.HasValue ? completed.Value : $"{SelectedPart.FinishedCount} (не удалось получить из Winnum, отображается количество из ЭЖ)")}\n\n" +
                        $"Информация по операциям за период " +
                        $"{new DateTime(startTime.Year, startTime.Month, startTime.Day):d} - " +
                        $"{new DateTime(SelectedPart.EndMachiningTime.Year, SelectedPart.EndMachiningTime.Month, SelectedPart.EndMachiningTime.Day):d}\n" +
                        $"{completedInfo}" +
                        $"", Path.Combine(winnumConfig.NcProgramFolder, serialNumber), priorityTagDurations, intervals, timeline);
                    win.Show();
                }
                catch (Exception ex)
                {
                    Util.WriteLog(ex);
                    progress.Report(ex.Message);
                    await Task.Delay(3000);
                }
            }
        }
        private bool CanSearchInWinnumCommandExecute(object p) => SelectedPart is { };
        #endregion

        #region ShowInfo
        public ICommand ShowInfoCommand { get; }
        private void OnShowInfoCommandExecuted(object p)
        {
            MessageBox.Show("Тут будет информация по выборке", "Заглушка", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private static bool CanShowInfoCommandExecute(object p) => true;
        #endregion

        #region OperatorReportToExcel
        public ICommand OperatorReportToExcelCommand { get; }
        private async void OnOperatorReportToExcelCommandExecuted(object p)
        {
            try
            {
                using (Overlay = new())
                {
                    var dlg = new ExportOperatorReportDialogWindow();
                    if (p is PartsInfoWindow w) dlg.Owner = w;
                    if (dlg.ShowDialog() == false)
                    {
                        Status = "Отмена";
                        return;
                    }
                    var path = Util.GetXlsxPath();
                    if (string.IsNullOrEmpty(path))
                    {
                        Status = "Выбор файла отменён";
                        return;
                    }
                    if (dlg.DataContext is ExportOperatorDailogWindowViewModel dx)
                    {
                        var serialParts = dx.OnlySerialParts ? (await libeLog.Infrastructure.Database.GetSerialPartsAsync(AppSettings.Instance.ConnectionString!)).PartNamesHashSet(EnumerableExtensions.PartNameNormalizeOption.NormalizeAndRemoveParentheses) : null;
                        var includeExcludedParts = dx.IncludeExcludedlParts;
                        InProgress = true;
                        Status = await Xl.ExportOperatorReportAsync(Parts, FromDate, ToDate, path, dx.Type.ToLowerInvariant() == "до" ? 1 : dx.Count, dx.Type.ToLowerInvariant() == "до" ? dx.Count : int.MaxValue, serialParts, includeExcludedParts);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally { InProgress = false; }
        }
        private static bool CanOperatorReportToExcelCommandExecute(object p) => true;
        #endregion

        #region OperatorsShiftsReportToExcel
        public ICommand OperatorsShiftsReportToExcelCommand { get; }
        private async void OnOperatorsShiftsReportToExcelCommandExecuted(object p)
        {
            try
            {
                using (Overlay = new())
                {
                    var path = Util.GetXlsxPath();
                    if (string.IsNullOrEmpty(path))
                    {
                        Status = "Выбор файла отменён";
                        return;
                    }
                    await Task.Run(() =>
                    {
                        InProgress = true;
                        Status = Xl.ExportOperatorsShiftsReport(Parts, FromDate, ToDate, path);
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally { InProgress = false; }
        }
        private static bool CanOperatorsShiftsReportToExcelCommandExecute(object p) => true;
        #endregion

        #region ExportPartsReportToExcel
        public ICommand ExportPartsReportToExcelCommand { get; }
        private async void OnExportPartsReportToExcelCommandExecuted(object p)
        {
            try
            {

                var path = Util.GetXlsxPath();
                if (string.IsNullOrEmpty(path))
                {
                    Status = "Выбор файла отменён";
                    return;
                }
                await Task.Run(() =>
                {
                    InProgress = true;
                    Status = Xl.ExportPartsInfo(Parts, path, FromDate, ToDate);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally { InProgress = false; }
        }
        private static bool CanExportPartsReportToExcelCommandExecute(object p) => true;
        #endregion

        #region ExportNewReportForPeriodToExcel
        public ICommand ExportNewReportForPeriodToExcelCommand { get; }
        private async void OnExportNewReportForPeriodToExcelCommandExecuted(object p)
        {
            try
            {
                var path = Util.GetXlsxPath();
                if (string.IsNullOrEmpty(path))
                {
                    Status = "Выбор файла отменён";
                    return;
                }
                var progress = new Progress<string>(message =>
                {
                    Status = message;
                });

                await App.Current.Dispatcher.InvokeAsync(() => InProgress = true);
                Status = await Task.Run(() =>
                    Xl.ExportNewReportForPeroidAsync(
                        Parts, FromDate, ToDate, ShiftFilter, path, progress
                    ).GetAwaiter().GetResult()
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally { InProgress = false; }
        }
        private static bool CanExportNewReportForPeriodToExcelCommandExecute(object p) => true;
        #endregion

        #region ExportReportForPeriodToExcel
        public ICommand ExportReportForPeriodToExcelCommand { get; }
        private async void OnExportReportForPeriodToExcelCommandExecuted(object p)
        {
            try
            {
                string runCountFilter = "";
                bool addSheetPerMachine = true;
                if (MessageBox.Show("Задать фильтр по количеству запусков?", "Вопросик", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    var dialog = new PartSelectionFilterWindow("", true)
                    {
                        Owner = p as PartsInfoWindow
                    };
                    if (dialog.ShowDialog() != true)
                    {
                        Status = "Отмена";
                        return;
                    }
                    runCountFilter = dialog.RunCountFilter;
                    addSheetPerMachine = dialog.AddSheetPerMachine;
                }
                var path = Util.GetXlsxPath();
                if (string.IsNullOrEmpty(path))
                {
                    Status = "Выбор файла отменён";
                    return;
                }
                var progress = new Progress<string>(message =>
                {
                    Status = message;
                });

                await Task.Run(() =>
                {
                    InProgress = true;
                    Status = Xl.ExportReportForPeroid(Parts, FromDate, ToDate, ShiftFilter, path, AdditionalDescreaseValue, runCountFilter, addSheetPerMachine, progress);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally { InProgress = false; }
        }
        private static bool CanExportReportForPeriodToExcelCommandExecute(object p) => true;
        #endregion

        #region ExportLongSetups
        public ICommand ExportLongSetupsCommand { get; }
        private async void OnExportLongSetupsCommandExecuted(object p)
        {
            try
            {

                var path = Util.GetXlsxPath();
                if (string.IsNullOrEmpty(path))
                {
                    Status = "Выбор файла отменён";
                    return;
                }
                await Task.Run(() =>
                {
                    InProgress = true;
                    Status = Xl.ExportLongSetups(Parts, path);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally { InProgress = false; }
        }
        private static bool CanExportLongSetupsCommandExecute(object p) => true;
        #endregion

        #region ExportHistoryToExcel
        public ICommand ExportHistoryToExcelCommand { get; }
        private async void OnExportHistoryToExcelCommandExecuted(object p)
        {
            try
            {
                var dlg = new PartnameDialogWindow(PartNameFilter);
                if (p is PartsInfoWindow w) dlg.Owner = w;
                if (dlg.ShowDialog() != true) return;
                var path = Util.GetXlsxPath();
                var partName = dlg.PartName;
                var oredersCount = dlg.OrderCount;
                await Task.Run(async () =>
                {
                    InProgress = true;
                    var cancellationToken = _cancellationTokenSource.Token;
                    var parts = await Task.Run(() => Database.ReadPartsWithConditions(BuildConditionsForPartForAllTime(partName), cancellationToken));
                    Status = Xl.ExportHistory(parts, path, oredersCount);
                });
                
                if (string.IsNullOrEmpty(path))
                {
                    Status = "Выбор файла отменён";
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally { InProgress = false; }
        }
        private static bool CanExportHistoryToExcelCommandExecute(object p) => true;
        #endregion

        #region ExportToolSearchCasesToExcel
        public ICommand ExportToolSearchCasesToExcelCommand { get; }
        private async void OnExportToolSearchCasesToExcelCommandExecuted(object p)
        {
            try
            {
                InProgress = true;
                var path = Util.GetXlsxPath();
                if (string.IsNullOrEmpty(path))
                {
                    Status = "Выбор файла отменён";
                    return;
                }
                IProgress<string> progress = new Progress<string>(p => Status = p);
                Status = $"Файл сохранен в: {await Xl.ExportToolSearchCasesAsync(Parts, path, progress)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                Util.WriteLog(ex, "Ошибка при экспорте отчёта по поискам.");
                Status = "";
            }
            finally { InProgress = false; }
        }
        private static bool CanExportToolSearchCasesToExcelCommandExecute(object p) => true;
        #endregion

        #region CheckAssignmentWithFact
        public ICommand CheckAssignmentWithFactCommand { get; }
        private async void OnCheckAssignmentWithFactCommandExecuted(object p)
        {
            try
            {
                if (!File.Exists(AppSettings.Instance.GoogleCredentialPath) )
                {
                    MessageBox.Show("Файл с учетными данными Google не существует.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                if (!File.Exists(AppSettings.Instance.GoogleCredentialPath) && string.IsNullOrWhiteSpace(AppSettings.Instance.AssignedPartsSheet))
                {
                    MessageBox.Show("Не указан ID Google таблицы.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                var path = Util.GetXlsxPath();
                if (string.IsNullOrEmpty(path))
                {
                    Status = "Выбор файла отменён";
                    return;
                }
                IProgress<string> progress = new Progress<string>(m => Status = m);
                InProgress = true;
                progress.Report("Подключение к Google таблице");
                var gs = new GoogleSheet(AppSettings.Instance.GoogleCredentialPath!, AppSettings.Instance.AssignedPartsSheet!);
                progress.Report("Получение информации из Google таблицы");
                var assignedParts = await gs.GetAssignedPartsAsync(progress);
                progress.Report("Данные из СЗН получены, формирование отчёта...");
                Status = await Task.Run(() => Xl.ExportAssignmentCheckResult(Parts, assignedParts, path, progress));


            }
            catch (Google.GoogleApiException ex)
            {
                MessageBox.Show(GoogleSheet.ExceptionMessage(ex), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { InProgress = false; }
        }
        private static bool CanCheckAssignmentWithFactCommandExecute(object p) => true;
        #endregion

        #region NormsAndWorkloadAnalysis
        public ICommand NormsAndWorkloadAnalysisCommand { get; }
        private async void OnNormsAndWorkloadAnalysisCommandExecuted(object p)
        {
            try
            {
                
                IProgress<string> progress = new Progress<string>(m => Status = m);
                InProgress = true;

                //List<DateTime> dates = new();

                //var dlg = new GetDatesWindow(ToDate);
                //if (dlg.ShowDialog() == false)
                //{
                //    Status = "Отмена";
                //    InProgress = false;
                //    await Task.Delay(3000);
                //    Status = "";
                //    return;
                //}
                //dates = dlg.Dates.ToList();
                var path = Util.GetXlsxPath();
                if (string.IsNullOrEmpty(path))
                {
                    Status = "Выбор файла отменён";
                    return;
                }
                Status = await Xl.ExportNormsAndWorkloadAnalysisAsync(Parts, path, progress);


            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { InProgress = false; }
        }
        private static bool CanNormsAndWorkloadAnalysisCommandExecute(object p) => true;
        #endregion
        
        /// <summary>
        /// todo
        /// </summary>
        #region ExportShiftsInfoReport
        public ICommand ExportShiftsInfoReportCommand { get; }
        private async void OnExportShiftsInfoReportCommandExecuted(object p)
        {
            try
            {

                var path = Util.GetXlsxPath();
                if (string.IsNullOrEmpty(path))
                {
                    Status = "Выбор файла отменён";
                    return;
                }
                await Task.Run(() =>
                {
                    InProgress = true;
                    Status = Xl.ExportShiftsInfo(Parts, path, FromDate, ToDate);
                }
                );

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally { InProgress = false; }
        }
        private static bool CanExportShiftsInfoReportCommandExecute(object p) => true;
        #endregion

        /// <summary>
        /// todo
        /// </summary>
        #region ExportVerevkinReportReport
        public ICommand ExportVerevkinReportCommand { get; }
        private async void OnExportVerevkinReportCommandExecuted(object p)
        {
            try
            {
                var path = Util.GetXlsxPath(false);
                if (string.IsNullOrEmpty(path))
                {
                    Status = "Выбор файла отменён";
                    return;
                }
                InProgress = true;
                Progress<string> progress = new(p => Status = p);
                await Task.Run(() =>
                {
                    var verevkinParts = Parts.GroupBy(p => new { p.PartName, p.Order })
                    .Select(g =>
                    {
                        var verevkinPart = new VerevkinPart(g.Key.PartName, g.Key.Order, g.Sum(p => p.FinishedCount));

                        foreach (var part in g)
                        {
                            switch (part.Setup)
                            {
                                case 1:
                                    verevkinPart.MachineTime1 += part.MachiningTime;
                                    break;
                                case 2:
                                    verevkinPart.MachineTime2 += part.MachiningTime;
                                    break;
                                case 3:
                                    verevkinPart.MachineTime3 += part.MachiningTime;
                                    break;
                                case 4:
                                    verevkinPart.MachineTime4 += part.MachiningTime;
                                    break;
                                case 5:
                                    verevkinPart.MachineTime5 += part.MachiningTime;
                                    break;
                            }
                        }
                        return verevkinPart;
                    }).ToList();
                    Xl.ExportVerevkinReport(verevkinParts, path, progress);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally { InProgress = false; }
        }
        private static bool CanExportVerevkinReportCommandExecute(object p) => true;
        #endregion

        #region ExportToExcel
        public ICommand ExportToExcelCommand { get; }
        private async void OnExportToExcelCommandExecuted(object p)
        {
            try
            {

                var path = Util.GetXlsxPath();
                if (string.IsNullOrEmpty(path))
                {
                    Status = "Выбор файла отменён";
                    return;
                }
                await Task.Run(() =>
                {
                    InProgress = true;
                    Status = Xl.ExportDataset(Parts, path);
                });

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally { InProgress = false; }
        }
        private static bool CanExportToExcelCommandExecute(object p) => true;
        #endregion

        #region DeleteFilter
        public ICommand DeleteFilterCommand { get; }
        private void OnDeleteFilterCommandExecuted(object p)
        {
            LockUpdate();
            ShiftFilter = new(ShiftType.All);
            OperatorFilter = string.Empty;
            PartNameFilter = string.Empty;
            OrderFilter = string.Empty;
            SetupFilter = null;
            FinishedCountFilter = "";
            ToDate = PartsInfo.ToDate;
            FromDate = PartsInfo.FromDate;
            _EngineerCommentFilter = "";
            for (int i = 0; i < MachineFilters.Count; i++)
            {
                MachineFilters[i].Filter = MachineFilters[i].Machine == PartsInfo.Machine;
            }
            UnlockUpdate();
        }
        private static bool CanDeleteFilterCommandExecute(object p) => true;
        #endregion

        #region ShowAllMachines
        public ICommand ShowAllMachinesCommand { get; }
        private void OnShowAllMachinesCommandExecuted(object p)
        {
            LockUpdate();
            for (int i = 0; i < MachineFilters.Count; i++)
            {
                MachineFilters[i].Filter = true;
            }
            UnlockUpdate();
        }
        private static bool CanShowAllMachinesCommandExecute(object p) => true;
        #endregion

        #region HideAllMachines
        public ICommand HideAllMachinesCommand { get; }
        private void OnHideAllMachinesCommandExecuted(object p)
        {
            LockUpdate();
            for (int i = 0; i < MachineFilters.Count; i++)
            {
                MachineFilters[i].Filter = false;
            }
            UnlockUpdate();
        }
        private static bool CanHideAllMachinesCommandExecute(object p) => true;
        #endregion

        #region InvertMachines
        public ICommand InvertMachinesCommand { get; }
        private void OnInvertMachinesCommandExecuted(object p)
        {
            LockUpdate();
            for (int i = 0; i < MachineFilters.Count; i++)
            {
                MachineFilters[i].Filter = !MachineFilters[i].Filter;
            }
            UnlockUpdate();
        }
        private static bool CanInvertMachinesCommandExecute(object p) => true;
        #endregion

        #region SetLatheMachines
        public ICommand SetLatheMachinesCommand { get; }
        private void OnSetLatheMachinesCommandExecuted(object p)
        {
            LockUpdate();
            for (int i = 0; i < MachineFilters.Count; i++)
            {
                MachineFilters[i].Filter = MachineFilters[i].Type.ToLowerInvariant().Contains("токарный");
            }
            UnlockUpdate();
        }
        private static bool CanSetLatheMachinesCommandExecute(object p) => true;
        #endregion

        #region SetMillMachines
        public ICommand SetMillMachinesCommand { get; }
        private void OnSetMillMachinesCommandExecuted(object p)
        {
            LockUpdate();
            for (int i = 0; i < MachineFilters.Count; i++)
            {
                MachineFilters[i].Filter = MachineFilters[i].Type.ToLowerInvariant().Contains("фрезерный");
            }
            UnlockUpdate();
        }
        private static bool CanSetMillMachinesCommandExecute(object p) => true;
        #endregion

        #region SetHorMillMachines
        public ICommand SetHorMillMachinesCommand { get; }
        private void OnSetHorMillMachinesCommandExecuted(object p)
        {
            LockUpdate();
            for (int i = 0; i < MachineFilters.Count; i++)
            {
                MachineFilters[i].Filter = MachineFilters[i].Type.ToLowerInvariant().Contains("горизонтально");
            }
            UnlockUpdate();
        }
        private static bool CanSetHorMillMachinesCommandExecute(object p) => true;
        #endregion

        #region SetVerMillMachines
        public ICommand SetVerMillMachinesCommand { get; }
        private void OnSetVerMillMachinesCommandExecuted(object p)
        {
            LockUpdate();
            for (int i = 0; i < MachineFilters.Count; i++)
            {
                MachineFilters[i].Filter = MachineFilters[i].Type.ToLowerInvariant().Contains("вертикально");
            }
            UnlockUpdate();
        }
        private static bool CanSetVerMillMachinesCommandExecute(object p) => true;
        #endregion

        #region ShowArchiveTable
        public ICommand ShowArchiveTableCommand { get; }
        private void OnShowArchiveTableCommandExecuted(object p)
        {
            using (Overlay = new())
            {
                //var disqualifiedKeys = new HashSet<(string PartName, int Setup, string Machine)>();
                //var filteredPrograms = new List<Part>();
                //foreach (var part in Parts)
                //{
                //    var key = (part.PartName, part.Setup, part.Machine);

                //}

                var parts = Parts
                    .GroupBy(p => (p.PartName, p.Setup, p.Machine))
                    .Select(g => g.OrderByDescending(p => p.StartSetupTime).First())
                    .OrderBy(p => p.Machine)
                    .ThenBy(p => p.StartSetupTime).ToList();

                ArchiveListWindow archiveListWindow = new(parts) { Owner = p as PartsInfoWindow };
                archiveListWindow.ShowDialog();
            }
        }
        private static bool CanShowArchiveTableCommandExecute(object p) => true;
        #endregion

        #region ChangeSerialPartNormatives
        public ICommand ChangeSerialPartNormativesCommand { get; }
        private void OnChangeSerialPartNormativesCommandExecuted(object p)
        {

            var serialPart = SerialParts.FirstOrDefault(sp => sp.PartName.NormalizedPartNameWithoutComments() == SelectedPart?.PartName.NormalizedPartNameWithoutComments());
            if (serialPart == null) return;
            if (Util.IsNotAppAdmin(() => MessageBox.Show("Нет прав на выполнение операции", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error)))
                return;
            if (!serialPart.Operations.Any())
            {
                MessageBox.Show(
                    "У этой серийной детали нет операций, к которым могут принадлежать нормативы. Создайте операции с установками в окне 'Серийные детали'", 
                    SelectedPart?.PartName, 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
                return;
            }
            var dlg = new EditSerialPartNormativeWindow() { SerialPart = serialPart };
            if (dlg.ShowDialog() != true) return;
            if (!dlg.NewSetupNormative.HasValue && !dlg.NewProductionNormative.HasValue)
            {
                return;
            }
            foreach (var part in Parts.Where(p => 
            p.PartName.NormalizedPartNameWithoutComments() == SelectedPart?.PartName.NormalizedPartNameWithoutComments() 
            && p.Order == SelectedPart?.Order 
            && p.Setup == SelectedPart?.Setup))
            {
                if (dlg.NewSetupNormative.HasValue 
                    && dlg.NewSetupNormative.Value > 0 
                    && SelectedPart?.SetupTimePlan == part.SetupTimePlan) 
                        part.FixedSetupTimePlan = dlg.NewSetupNormative.Value;
                if (dlg.NewProductionNormative.HasValue 
                    && dlg.NewProductionNormative.Value > 0 
                    && SelectedPart?.SingleProductionTimePlan == part.SingleProductionTimePlan) 
                        part.FixedProductionTimePlan = dlg.NewProductionNormative.Value;
            }
            UpdatePartsCommand.Execute(true);
        }
        private bool CanChangeSerialPartNormativesCommandExecute(object p) => SelectedPart != null && SerialParts.FirstOrDefault(sp => sp.PartName.NormalizedPartNameWithoutComments() == SelectedPart?.PartName.NormalizedPartNameWithoutComments()) != null;
        #endregion

        #region UnlockSerialPartNormatives
        public ICommand UnlockSerialPartNormativesCommand { get; }
        private void OnUnlockSerialPartNormativesCommandExecuted(object p)
        {
 
            var serialPart = SerialParts.FirstOrDefault(sp => sp.PartName.NormalizedPartNameWithoutComments() == SelectedPart?.PartName.NormalizedPartNameWithoutComments());
            if (serialPart == null) return;
            foreach (var part in Parts.Where(p => p.PartName.NormalizedPartNameWithoutComments() == serialPart.PartName.NormalizedPartNameWithoutComments()))
            {
                part.IsUnlocked = true;
            }
            
        }
        private bool CanUnlockSerialPartNormativesCommandExecute(object p) => SelectedPart is { };
        #endregion


        #region DeletePart
        public ICommand DeletePartCommand { get; }
        private void OnDeletePartCommandExecuted(object p)
        {
            if (p is Part part && part == SelectedPart
                && MessageBox.Show($"Удалить деталь: {part.PartName}?\nДанное действие невозможно отменить.", "Удаление информации", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
            {
                switch (Database.DeletePart(part))
                {
                    case DbResult.Ok:
                        _ = LoadPartsAsync();
                        Status = $"Деталь {SelectedPart.PartName} удалена из БД";
                        break;
                    case DbResult.AuthError:
                        Status = "Ошибка авторизации";
                        break;
                    case DbResult.Error:
                        Status = "Ошибка";
                        break;
                    case DbResult.NoConnection:
                        Status = "Нет соединения с БД";
                        break;
                }
            };
        }
        private bool CanDeletePartCommandExecute(object p) => SelectedPart is { };
        #endregion

        #region UpdateParts
        public ICommand UpdatePartsCommand { get; }
        private async void OnUpdatePartsCommandExecutedAsync(object p)
        {
            if (p is bool skipAsk && !skipAsk || p is not bool)
            {
                if (MessageBox.Show("Обновить информацию?", "Вы точно уверены?", MessageBoxButton.YesNo, MessageBoxImage.Question) is MessageBoxResult.No) return;
            }
            
            foreach (var part in Parts.Where(p => p.NeedUpdate))
            {
                var (res, mess) = await part.UpdatePartAsync();
                switch (res)
                {
                    case DbResult.Ok:
                        part.NeedUpdate = false;
                        break;
                    case DbResult.AuthError:
                        MessageBox.Show(mess);
                        break;
                    case DbResult.Error:
                        MessageBox.Show(mess);
                        break;
                }
            }
            _ = LoadPartsAsync();
        }
        private static bool CanUpdatePartsCommandExecute(object p) => true;
        #endregion

        #region RefreshParts
        public ICommand RefreshPartsCommand { get; }
        private void OnRefreshPartsCommandExecuted(object p) => _ = LoadPartsAsync();
        private static bool CanRefreshPartsCommandExecute(object p) => true;
        #endregion

        #region ChangeCompactView
        public ICommand ChangeCompactViewCommand { get; }
        private void OnChangeCompactViewCommandExecuted(object p) => CompactView = !CompactView;
        private static bool CanChangeCompactViewCommandExecute(object p) => true;
        #endregion

        #region ChangeRole
        public ICommand ChangeRoleCommand { get; }
        private void OnChangeRoleCommandExecuted(object p) 
        {
            if (p is string str && Enum.TryParse(str, out User newRole))
            {
                ViewMode = newRole;
            }
        }
        private static bool CanChangeRoleCommandExecute(object p) => true;
        #endregion

        #region ChangeShowUnchecked
        public ICommand ChangeShowUncheckedCommand { get; }
        private void OnChangeShowUncheckedCommandExecuted(object p)
        {
            ViewUnchecked = !ViewUnchecked;
            EngineerCommentFilter = ViewUnchecked ? "ожидание" : "";
        }
        private static bool CanChangeShowUncheckedCommandExecute(object p) => true;
        #endregion

        #region ChangeCalcFixed
        public ICommand ChangeCalcFixedCommand { get; }
        private void OnChangeCalcFixedCommandExecuted(object p)
        {
            Part.CalcFixed = !Part.CalcFixed;
            CalcFixed = Part.CalcFixed;
            Task.Run(() => LoadPartsAsync());
        }
        private static bool CanChangeCalcFixedCommandExecute(object p) => true;
        #endregion

        #region OpenDailyReportWindow
        public ICommand OpenDailyReportWindowCommand { get; }

        public string Error => throw new NotImplementedException();

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(FinishedCountFilter) when !Util.TryParseComparison(FinishedCountFilter, out _, out _) && !string.IsNullOrWhiteSpace(FinishedCountFilter):
                        return "Неверно указан фильтр по количеству изготовленных деталей.";

                    default:
                        return null!;
                }
            }
        }

        private void OnOpenDailyReportWindowCommandExecuted(object p)
        {
            if (FromDate != ToDate)
            {
                MessageBox.Show("Для составления суточного отчета должны быть выбраны одинаковые даты.", "Разные даты", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (MachineFilters.Count(f => f.Filter) > 1)
            {
                MessageBox.Show("Для составления суточного отчета в фильтре должен быть только один станок.", "Выбрано слишком много станков.", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            else if (!MachineFilters.Any(f => f.Filter))
            {
                MessageBox.Show("Для составления суточного отчета в фильтре должен быть как минимум один станок.", "Выбрано слишком мало станков.", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (ShiftFilter.Type != ShiftType.All)
            {
                MessageBox.Show("Для составления суточного отчета не должно быть фильтра по смене.", "Лишние фильтры.", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.IsNullOrEmpty(OperatorFilter))
            {
                MessageBox.Show("Для составления суточного отчета не должно быть фильтра по оператору.", "Лишние фильтры.", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.IsNullOrEmpty(PartNameFilter))
            {
                MessageBox.Show("Для составления суточного отчета не должно быть фильтра по детали.", "Лишние фильтры.", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.IsNullOrEmpty(OrderFilter))
            {
                MessageBox.Show("Для составления суточного отчета не должно быть фильтра по маршрутному листу.", "Лишние фильтры.", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (SetupFilter != null)
            {
                MessageBox.Show("Для составления суточного отчета не должно быть фильтра по установке.", "Лишние фильтры.", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (Parts.Any(x => x.NeedUpdate))
            {
                MessageBox.Show("Есть несохраненные данные.", "Подтверждение.",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (HasErrors)
            {
                MessageBox.Show("Не всё заполнено корректно.", "Предупреждение.",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (Overlay = new())
            {
                var dailyInfoWindow = new DailyReportWindow((Parts, ToDate, MachineFilters.Where(mf => mf.Filter).First().Machine)) { Owner = p as PartsInfoWindow };
                dailyInfoWindow.ShowDialog();
            }
        }
        private static bool CanOpenDailyReportWindowCommandExecute(object p) => true;
        #endregion

        #region OpenMultiValueEditorCommand
        public ICommand OpenMultiValueEditorCommand { get; }

        private void OnOpenMultiValueEditorCommandExecuted(object p)
        {
            if (p is not string fieldName) return;

            string currentValue = fieldName switch
            {
                "Operator" => OperatorFilter,
                "Order" => OrderFilter,
                _ => ""
            };

            var editor = new MultiValueEditorWindow(fieldName, currentValue)
            {
                Owner = Application.Current.Windows.OfType<PartsInfoWindow>().FirstOrDefault(),
                ShowInTaskbar = false,
                Topmost = true 
            };

            editor.Closed += (s, args) =>
            {
                _editors.Remove(fieldName);
                if (editor.Resilt)
                {
                    switch (fieldName)
                    {
                        case "Operator":
                            OperatorFilter = editor.ResultString;
                            break;
                        case "Order":
                            OrderFilter = editor.ResultString;
                            break;
                    }
                }
            };
            _editors[fieldName] = editor;
            editor.Show();
        }

        private static bool CanOpenMultiValueEditorCommandExecute(object p) => p is string;
        #endregion

        public void PushValueToEditor(string type, string value)
        {
            if (!_editors.ContainsKey(type)) 
            {
                OpenMultiValueEditorCommand.Execute(type);
            }
            if (!_editors[type].Values.Any(v => string.Equals(v.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                _editors[type].Values.Add(new() { Value = value });
            }
        }

        private async Task<bool> LoadPartsAsync(bool first = false)
        {
            if (lockUpdate) return false;
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = new();
            var cancellationToken = _cancellationTokenSource.Token;
            await semaphoreSlim.WaitAsync(cancellationToken);
            try
            {
                InProgress = true;
                Status = "Получение информации...";
                if (AppSettings.Instance.ConnectionString == null)
                {
                    Status = "Не настроено соединение с БД";
                    return false;
                }
                await Util.UpdateAppSettingsAsync();
                if (!first) await Task.Delay(1000, cancellationToken);

                if (UseMockData)
                {
                    Parts = await Util.GenerateMockPartsAsync();
                    InProgress = false;
                    return true;
                }

                var isValidFinishedCountFilter = Util.TryParseComparison(FinishedCountFilter, out string finishedCountOperator, out int finishedCountValue);
                var isValidTotalCountFilter = Util.TryParseComparison(TotalCountFilter, out string totalCountOperator, out int totalCountValue);
                var partNameStartStar = PartNameFilter.StartsWith('*');

                if (!MachineFilters.Any(mf => mf.Filter == true))
                {
                    Application.Current.Dispatcher.Invoke(() => { Parts.Clear(); });
                    InProgress = false;
                    return false;
                }

                var tempParts = await Database.ReadPartsWithConditions(BuildConditions(), cancellationToken);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Parts.Clear();
                    Parts = new(tempParts);
                });
                InProgress = false;
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{ex.Message}");
                InProgress = false;
                return false;
            }
            finally
            {
                semaphoreSlim.Release();
                Status = "";
            }
        }

        /// <summary>
        /// Собирает строку запроса
        /// </summary>
        /// <returns></returns>
        private string BuildConditions()
        {
            var sb = new StringBuilder();

            sb.AppendFormat("ShiftDate BETWEEN '{0}' AND '{1}' ", FromDate, ToDate);

            if (ShiftFilter is not { Type: ShiftType.All })
            {
                sb.AppendFormat("AND Shift = '{0}' ", ShiftFilter.FilterText);
            }

            AppendMultiValueCondition(sb, "Operator", OperatorFilter);

            AppendCondition(sb, "PartName", PartNameFilter);

            AppendMultiValueCondition(sb, "[Order]", OrderFilter);

            AppendCondition(sb, "EngineerComment", EngineerCommentFilter);

            if (Util.TryParseComparison(FinishedCountFilter, out var finishedCountOperator, out var finishedCountValue))
                sb.AppendFormat("AND FinishedCount {0} {1} ", finishedCountOperator, finishedCountValue);

            if (Util.TryParseComparison(TotalCountFilter, out var totalCountOperator, out var totalCountValue))
                sb.AppendFormat("AND totalCount {0} {1} ", totalCountOperator, totalCountValue);

            if (SetupFilter != null)
                sb.AppendFormat("AND Setup = {0} ", SetupFilter);

            if (OnlySerialPartsFilter)
            {
                var serialNamesNormalized = string.Join(", ", SerialParts.Select(sp => $"'{sp.PartName.NormalizedPartNameWithoutComments()}'"));
                sb.AppendFormat("AND NormalizedPartName IN ({0}) ", serialNamesNormalized);
            }

            var machines = string.Join(", ", MachineFilters.Where(mf => mf.Filter).Select(m => $"'{m.Machine}'").Distinct());
            sb.AppendFormat("AND Machine IN ({0}) ", machines);

            return sb.ToString();
        }

        private void AppendMultiValueCondition(StringBuilder sb, string column, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            // Проверяем, есть ли множественные значения (через ;)
            if (value.Contains(';'))
            {
                var values = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(v => v.Trim())
                                  .Where(v => !string.IsNullOrWhiteSpace(v))
                                  .ToList();

                if (values.Count > 0)
                {
                    var patterns = values.Select(v => new SearchPattern(v)).ToList();

                    // Если все паттерны без LIKE
                    if (patterns.All(p => p.IsExactMatch))
                    {
                        var quotedValues = string.Join(", ", values.Select(v => $"'{v}'"));
                        sb.AppendFormat("AND {0} IN ({1}) ", column, quotedValues);
                    }
                    else
                    {
                        // Если есть LIKE паттерны, то OR
                        sb.Append("AND (");
                        for (int i = 0; i < patterns.Count; i++)
                        {
                            if (i > 0) sb.Append(" OR ");
                            sb.AppendFormat("{0} {1}", column, patterns[i]);
                        }
                        sb.Append(") ");
                    }
                }
            }
            else
            {
                AppendCondition(sb, column, value);
            }
        }

        /// <summary>
        /// Собирает строку запроса для получения списка всех изготовлений на всех станках для этой детали
        /// </summary>
        /// <returns></returns>
        private string BuildConditionsForPartForAllTime(string partName)
        {
            var sb = new StringBuilder();

            sb.AppendFormat("ShiftDate BETWEEN '{0}' AND '{1}' ", new DateTime(2023, 1, 1), DateTime.Today);

            AppendCondition(sb, "PartName", partName);

            return sb.ToString();
        }

        /// <summary>
        /// Поиск по строке, либо с полным совпадением, либо "is Like"
        /// </summary>
        /// <param name="sb">StringBuilder в котором формируется строка запроса</param>
        /// <param name="column">Столбец в который будем писать</param>
        /// <param name="value">Значение</param>
        private static void AppendCondition(StringBuilder sb, string column, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                var pattern = new SearchPattern(value);
                sb.AppendFormat("AND {0} {1} ", column, pattern);
            }
        }

        /// <summary>
        /// Подтверждение изменения свойства, которое повлечет изменение списка.
        /// Сразу возвращает true, если нет незаписанных позиций, либо спрашивает подтверждения.
        /// </summary>
        /// <returns></returns>
        private bool CanBeChanged()
        {
            if (Parts.Any(x => x.NeedUpdate)
                && MessageBox.Show("Есть незаписанные изменения. При продолжении они будут утеряны.", "Обновить список деталей?",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.Cancel) return false;
            return true;
        }

        public static string FormatSearchInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (input.StartsWith('*') && input.EndsWith('*')) return input.Replace('*', '%');
            if (input.StartsWith('=')) return input[1..];
            if (input.StartsWith('*')) return $"%{input[1..]}";
            if (input.EndsWith('*')) return $"{input[..^1]}%";
            return $"%{input}%";
        }

        private static IEnumerable<TimelineSource> BuildMachineTimelineSources(Signal signal, Machine machine, DateTime start, DateTime end)
        {
            TimelineSource? FromSignal(string displayName, string signalId, Func<string, string>? mapper = null)
            {
                if (string.IsNullOrEmpty(signalId)) return null;
                return TimelineSource.FromSignal(
                    displayName,
                    () => signal.GetSignalAsync(signalId, SignalType.ByTime, Ordering.Asc, start, end), valueMapper: mapper);
            }

            var sources = new (string DisplayName, string SignalId, Func<string, string>? Mapper)[]
            {
                // Общие
                ("№ УП",                machine.WnNcProgramNameSignal,           null),
                ("Имя УП",              machine.WnNcPartNameSignal,              null),
                ("Счётчик",             machine.WnCounterSignal,                 null),
                ("Режим ЧПУ",           machine.WnNcModeSignal,                  machine.Name.Contains("XH6300") ? SignalMappers.NcModeSiemens : SignalMappers.NcModeFanuc),
                ("УП выполняется",      machine.WnProgramRunningSignal,          SignalMappers.Bool),
                ("Останов по M0",       machine.WnStopSignal,                    SignalMappers.Bool),
                ("Останов по М1",       machine.WnOpStopSignal,                  SignalMappers.Bool),
                ("М-код",               machine.WnMcodeSignal,                   null),
                ("Движение",            machine.WnGMoveSignal,                   null),
                ("Цикл",                machine.WnGCycleSignal,                  null),
                ("G-код",               machine.WnGcodeSignal,                   null),
                ("Feed Hold",           machine.WnFeedHoldSignal,                SignalMappers.Bool),
                ("Single Block",        machine.WnSBKSignal,                     SignalMappers.Bool),
                ("Dry Run",             machine.WnDryRunSignal,                  SignalMappers.Bool),
                ("MSTLK",               machine.WnMSTLKSignal,                   SignalMappers.Bool),
                ("MLK",                 machine.WnMLKSignal,                     SignalMappers.Bool),
                ("N",                   machine.WnCurrentBlockNumberSignal,      null),
                ("Текст кадра",         machine.WnCurrentBlockTextSignal,        null),
                ("СК",                  machine.WnCurrentCSSignal,               null),
                ("Плоскость",           machine.WnCurrentPlaneSignal,            null),
                // Коррекции                                                     
                ("Корр. подачи",        machine.WnFeedMultiplierSignal,          null),
                ("Корр. ускор. хода",   machine.WnRapidMultiplierSignal,         null),
                ("Корр. шпинделя 1",    machine.WnSpindleSpeed1MultiplierSignal, null),
                ("Корр. шпинделя 2",    machine.WnSpindleSpeed2MultiplierSignal, null),
                // Актуальные значения                                           
                ("Обор. 1 шпинделя",    machine.WnActSpindle1SpeedSignal,        null),
                ("Обор. 2 шпинделя",    machine.WnActSpindle2SpeedSignal,        null),
                ("Скорость резания",    machine.WnActCutSpeedSignal,             null),
                ("Подача мм/мин",       machine.WnActFeedPerMinSignal,           null),
                ("Подача мм/об",        machine.WnActFeedPerRevSignal,           null),
                // Инструмент                                                    
                ("Инструмент",          machine.WnToolSignal,                    null),
                ("Корр. H",             machine.WnToolHSignal,                   null),
                ("Корр. D",             machine.WnToolDSignal,                   null),
                ("Вектор инстр.",       machine.WnToolVectorSignal,              null),
                ("Геом. R",             machine.WnToolGeomRSignal,               null),
                ("Геом. X",             machine.WnToolGeomXSignal,               null),
                ("Геом. Y",             machine.WnToolGeomYSignal,               null),
                ("Геом. Z",             machine.WnToolGeomZSignal,               null),
                ("Износ R",             machine.WnToolWearRSignal,               null),
                ("Износ X",             machine.WnToolWearXSignal,               null),
                ("Износ Y",             machine.WnToolWearYSignal,               null),
                ("Износ Z",             machine.WnToolWearZSignal,               null),
                // Абсолютные координаты                                         
                ("Abs X",               machine.WnAbsXSignal,                    null),
                ("Abs Y",               machine.WnAbsYSignal,                    null),
                ("Abs Z",               machine.WnAbsZSignal,                    null),
                ("Abs ZA",              machine.WnAbsZASignal,                   null),
                ("Abs B",               machine.WnAbsBSignal,                    null),
                ("Abs C",               machine.WnAbsCSignal,                    null),
                ("Abs W",               machine.WnAbsWSignal,                    null),
                // Относительные координаты                                      
                ("Rel X",               machine.WnRelXSignal,                    null),
                ("Rel Y",               machine.WnRelYSignal,                    null),
                ("Rel Z",               machine.WnRelZSignal,                    null),
                ("Rel ZA",              machine.WnRelZASignal,                   null),
                ("Rel B",               machine.WnRelBSignal,                    null),
                ("Rel C",               machine.WnRelCSignal,                    null),
                ("Rel W",               machine.WnRelWSignal,                    null),
                // Машинные координаты                                           
                ("Mach X",              machine.WnMachXSignal,                   null),
                ("Mach Y",              machine.WnMachYSignal,                   null),
                ("Mach Z",              machine.WnMachZSignal,                   null),
                ("Mach ZA",             machine.WnMachZASignal,                  null),
                ("Mach B",              machine.WnMachBSignal,                   null),
                ("Mach C",              machine.WnMachCSignal,                   null),
                ("Mach W",              machine.WnMachWSignal,                   null),
                // Аварии и нагрузки                                             
                ("Авария",              machine.WnAlarmMessageSignal,            null),
                ("Нагрузка X",          machine.WnLoadXSignal,                   null),
                ("Нагрузка Y",          machine.WnLoadYSignal,                   null),
                ("Нагрузка Z",          machine.WnLoadZSignal,                   null),
                ("Нагрузка C",          machine.WnLoadCSignal,                   null),
                ("Нагрузка B",          machine.WnLoadBSignal,                   null),
                ("Нагрузка W",          machine.WnLoadWSignal,                   null),
                ("Нагрузка 1 шпинд.",   machine.WnLoadSpindle1Signal,            null),
                ("Нагрузка 2 шпинд.",   machine.WnLoadSpindle2Signal,            null),
            };

            foreach (var (displayName, signalId, mapper) in sources)
            {
                var source = FromSignal(displayName, signalId, mapper);
                if (source != null)
                    yield return source;
            }
        }

        private void LockUpdate() => lockUpdate = true;

        private void UnlockUpdate(bool first = false)
        {
            lockUpdate = false;
            _ = LoadPartsAsync(first);
        }

        private void UpdateHasErrors()
        {
            HasErrors = Parts.Any(x => !string.IsNullOrEmpty(x.Error));
        }
    }   
}
