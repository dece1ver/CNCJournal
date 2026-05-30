using libeLog;
using libeLog.Base;
using libeLog.Extensions;
using libeLog.Infrastructure.Wrappers;
using libeLog.Interfaces;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using remeLog.Infrastructure;
using remeLog.Infrastructure.Types;
using remeLog.Infrastructure.Winnum;
using remeLog.Models;
using remeLog.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using static libeLog.Constants;
using static remeLog.Models.CombinedParts;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace remeLog.ViewModels
{
    internal class MainWindowViewModel : ViewModel, IOverlay
    {
        private bool lockUpdate = false;
        private CancellationTokenSource _cancellationTokenSource = new();
        private CancellationTokenSource _bgCts = new();
        private CancellationTokenSource _debounceTokenSource = new();
        private readonly object _debounceLock = new object();
        private bool _updatePending = false;
        private FileSystemWatcher? _watcher;
        private int _showed;

        private SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);

        public MainWindowViewModel()
        {
            CloseApplicationCommand = new LambdaCommand(OnCloseApplicationCommandExecuted, CanCloseApplicationCommandExecute);
            TestCommand = new LambdaCommand(OnTestCommandExecuted, CanTestCommandExecute);
            UpdateDatabaseCommand = new LambdaCommand(OnUpdateDatabaseCommandExecuted, CanUpdateDatabaseCommandExecute);
            EditSettingsCommand = new LambdaCommand(OnEditSettingsCommandExecuted, CanEditSettingsCommandExecute);
            LoadPartsInfoCommand = new LambdaCommand(OnLoadPartsInfoCommandExecuted, CanLoadPartsInfoCommandExecute);
            ShowLongSetupsCommand = new LambdaCommand(OnShowLongSetupsCommandExecuted, CanShowLongSetupsCommandExecute);
            ShowMonitorCommand = new LambdaCommand(OnShowMonitorCommandExecuted, CanShowMonitorCommandExecute);
            EditOperatorsCommand = new LambdaCommand(OnEditOperatorsCommandExecuted, CanEditOperatorsCommandExecute);
            EditSerialPartsCommand = new LambdaCommand(OnEditSerialPartsCommandExecuted, CanEditSerialPartsCommandExecute);
            ShowAboutCommand = new LambdaCommand(OnShowAboutCommandExecuted, CanShowAboutCommandExecute);
            ShowPartsInfoCommand = new LambdaCommand(OnShowPartsInfoCommandExecuted, CanShowPartsInfoCommandExecute);
            IncreaseDateCommand = new LambdaCommand(OnIncreaseDateCommandExecuted, CanIncreaseDateCommandExecute);
            DecreaseDateCommand = new LambdaCommand(OnDecreaseDateCommandExecuted, CanDecreaseDateCommandExecute);
            SetYesterdayDateCommand = new LambdaCommand(OnSetYesterdayDateCommandExecuted, CanSetYesterdayDateCommandExecute);
            SetWeekDateCommand = new LambdaCommand(OnSetWeekDateCommandExecuted, CanSetWeekDateCommandExecute);
            SetMonthDateCommand = new LambdaCommand(OnSetMonthDateCommandExecuted, CanSetMonthDateCommandExecute);
            SetYearDateCommand = new LambdaCommand(OnSetYearDateCommandExecuted, CanSetYearDateCommandExecute);
            SetSpecificMonthCommand = new LambdaCommand(OnSetSpecificMonthCommandExecuted, CanSetSpecificMonthCommandExecute);
            SetSpecificYearCommand = new LambdaCommand(OnSetSpecificYearCommandExecuted, CanSetSpecificYearCommandExecute);
            ShowActiveInstancesCommand = new LambdaCommand(OnShowActiveInstancesCommandExecuted, CanShowActiveInstancesCommandExecute);
            _Machines = new();
            if (AppSettings.Instance.InstantUpdateOnMainWindow) { _ = LoadPartsAsync(true); }
            //var backgroundWorker = new Thread(BackgroundWorker) { IsBackground = true };
            //backgroundWorker.Start();
        }

        public async Task InitializeAsync()
        {
            await BackgroundWorkerAsync();
        }

        private Overlay _Overlay = new(false);

        public Overlay Overlay
        {
            get => _Overlay;
            set => Set(ref _Overlay, value);
        }

        private string _Status = string.Empty;
        /// <summary> Статус </summary>
        public string Status
        {
            get => _Status;
            set => Set(ref _Status, value);
        }

        private bool _InProgress;
        public bool InProgress
        {
            get => _InProgress;
            set => Set(ref _InProgress, value);
        }

        private DateTime _FromDate = DateTime.Today.AddDays(-1);
        public DateTime FromDate
        {
            get => _FromDate;
            set
            {
                if (Set(ref _FromDate, value) && AppSettings.Instance.InstantUpdateOnMainWindow)
                {
                    OnPropertyChanged(nameof(IsSingleShift));
                    _ = LoadPartsAsync();
                }
            }
        }

        private DateTime _ToDate = DateTime.Today.AddDays(-1);
        public DateTime ToDate
        {
            get => _ToDate;
            set
            {
                if (Set(ref _ToDate, value) && AppSettings.Instance.InstantUpdateOnMainWindow)
                {
                    OnPropertyChanged(nameof(IsSingleShift));
                    _ = LoadPartsAsync();
                }
            }
        }

        public List<int> AvailableYears =>
            Enumerable.Range(2023, DateTime.Now.Year - 2023 + 1).ToList();

        private RangeObservableCollection<CombinedParts> _Parts = new();
        /// <summary> Объединенный список объединенных списков </summary>
        public RangeObservableCollection<CombinedParts> Parts
        {
            get => _Parts;
            set
            {
                if (Set(ref _Parts, value))
                {
                    OnPropertyChanged(nameof(TotalMachinesCount));
                    OnPropertyChanged(nameof(ReportsExistCount));
                    OnPropertyChanged(nameof(CheckedReportsCount));
                    OnPropertyChanged(nameof(ReportsSummary));
                    OnPropertyChanged(nameof(CheckedSummary));
                    OnPropertyChanged(nameof(TotalMachinesCountForPeriod));
                    OnPropertyChanged(nameof(ReportsExistCountForPeriod));
                    OnPropertyChanged(nameof(ReportsSummaryForPeriod));
                    OnPropertyChanged(nameof(CheckedSummaryForPeriod));
                }
            }
        }


        private List<string> _Machines;
        /// <summary> Описание </summary>
        public List<string> Machines
        {
            get => _Machines;
            set => Set(ref _Machines, value);
        }


        private bool _Debug = false;
        /// <summary> отладка </summary>
        public bool Debug
        {
            get => _Debug;
            set => Set(ref _Debug, value);
        }

        public bool IsAdministrator =>
            AppSettings.Administrators.Contains(Environment.UserName, StringComparer.OrdinalIgnoreCase);

        public bool IsSingleShift => FromDate == ToDate;
        public bool IsSingleWorkingShift => IsSingleShift && !AppSettings.Holidays.Contains(ToDate);

        private List<ShiftInfo> _totalShifts = new();
        public List<ShiftInfo> TotalShifts
        {
            get => _totalShifts;
            set => Set(ref _totalShifts, value);
        }

        public int TotalMachinesCount => IsSingleWorkingShift ? Parts.Count : 0;
        public int TotalMachinesCountForPeriod => Parts.Count * (Parts.FirstOrDefault()?.TotalShifts ?? 0) / 2;
        public int ReportsExistCount => Parts.Count(p => p.IsReportExist != ReportState.NotExist);
        public int ReportsExistCountForPeriod => TotalShifts.Count / 2;
        public int CheckedReportsCount => Parts.Count(p => p.IsReportChecked);
        public int CheckedReportsCountForPeriod => TotalShifts.Count(s => s.IsChecked) / 2;

        public string ReportsSummary => $"МЦ: {ReportsExistCount:00}/{TotalMachinesCount:00}{(TotalMachinesCount > 0 && ReportsExistCount > 0 ? $" ({(Convert.ToDouble(ReportsExistCount) / TotalMachinesCount):0.#%})" : "")}";
        public string CheckedSummary => $"ТО: {CheckedReportsCount:00}/{TotalMachinesCount:00}{(TotalMachinesCount > 0 && CheckedReportsCount > 0 ? $" ({(Convert.ToDouble(CheckedReportsCount) / TotalMachinesCount):0.#%})" : "")}";

        public string ReportsSummaryForPeriod => $"МЦ: {ReportsExistCountForPeriod:00}/{TotalMachinesCountForPeriod:00}{(TotalMachinesCountForPeriod > 0 && ReportsExistCountForPeriod > 0 ? $" ({(Convert.ToDouble(ReportsExistCountForPeriod) / TotalMachinesCountForPeriod):0.#%})" : "")}";
        public string CheckedSummaryForPeriod => $"ТО: {CheckedReportsCountForPeriod:00}/{TotalMachinesCountForPeriod:00}{(TotalMachinesCountForPeriod > 0 && CheckedReportsCountForPeriod > 0 ? $" ({(Convert.ToDouble(CheckedReportsCountForPeriod) / TotalMachinesCountForPeriod):0.#%})" : "")}";


        #region Команды

        #region CloseApplicationCommand
        public ICommand CloseApplicationCommand { get; }
        private static void OnCloseApplicationCommandExecuted(object p)
        {
            Application.Current.Shutdown();
        }
        private bool CanCloseApplicationCommandExecute(object p) => !InProgress;
        #endregion

        #region TestCommand
        public ICommand TestCommand { get; }
        private void OnTestCommandExecuted(object p)
        {
            var durations = Util.GenerateMockIntervals(new DateTime(2025, 5, 12, 06, 55, 00), new DateTime(2025, 5, 12, 19, 03, 00));
            var winnumWindow = new WinnumInfoWindow("", "", new List<Infrastructure.Winnum.Data.PriorityTagDuration>(), durations);
            winnumWindow.ShowDialog();
        }
        private bool CanTestCommandExecute(object p) => !InProgress;
        #endregion

        #region UpdateDatabaseCommand
        public ICommand UpdateDatabaseCommand { get; }
        private void OnUpdateDatabaseCommandExecuted(object p)
        {
            UpdateDatabaseWindow updateDatabaseWindow = new();
            updateDatabaseWindow.ShowDialog();
        }
        private bool CanUpdateDatabaseCommandExecute(object p) => !InProgress;
        #endregion

        #region EditSettings
        public ICommand EditSettingsCommand { get; }
        private void OnEditSettingsCommandExecuted(object p)
        {
            using (Overlay = new())
            {
                SettingsWindow settingsWindow = new SettingsWindow() { Owner = Application.Current.MainWindow };
                if (settingsWindow.ShowDialog() == true && settingsWindow.DataContext is SettingsWindowViewModel settings)
                {
                    AppSettings.Instance.DataSource = settings.DataSource;
                    AppSettings.Instance.QualificationSourcePath = settings.QualificationSourcePath.Value;
                    AppSettings.Instance.GoogleCredentialPath = settings.GoogleCredentialPath.Value;
                    AppSettings.Instance.AssignedPartsSheet = settings.AssignedPartsSheet.Value;
                    AppSettings.Instance.ConnectionString = settings.ConnectionString.Value;
                    AppSettings.Instance.InstantUpdateOnMainWindow = settings.InstantUpdateOnMainWindow;
                    AppSettings.Instance.User = settings.Role;
                    AppSettings.Save();
                    //Util.TrySetupSyncfusionLicense();
                    Status = "Параметры сохранены";
                }
            }
        }
        private bool CanEditSettingsCommandExecute(object p) => !InProgress;
        #endregion

        #region EditOperators
        public ICommand EditOperatorsCommand { get; }
        private void OnEditOperatorsCommandExecuted(object p)
        {
            using (Overlay = new())
            {
                EditOperatorsWindow editOperatorsWindow = new EditOperatorsWindow();
                editOperatorsWindow.CenterTo(App.Current.MainWindow);
                editOperatorsWindow.ShowDialog();
            }
        }
        private bool CanEditOperatorsCommandExecute(object p) => !InProgress;
        #endregion

        #region EditSerialParts
        public ICommand EditSerialPartsCommand { get; }
        private void OnEditSerialPartsCommandExecuted(object p)
        {
            using (Overlay = new())
            {
                EditSerialPartsWindow editSerialPartsWindow = new EditSerialPartsWindow();
                editSerialPartsWindow.CenterTo(App.Current.MainWindow);
                editSerialPartsWindow.ShowDialog();
            }
        }
        private bool CanEditSerialPartsCommandExecute(object p) => !InProgress;
        #endregion

        #region LoadPartsInfo
        public ICommand LoadPartsInfoCommand { get; }
        private async void OnLoadPartsInfoCommandExecuted(object p)
        {
            if (p is true)
            {
                Parts.Clear();
                var cp = await GenerateMockDataAsync("Hyundai WIA SKT21 №104", DateTime.Today, DateTime.Today);
                var partsInfoWindow = new PartsInfoWindow(cp)
                {
                    Owner = Application.Current.MainWindow,
                    DataContext = new PartsInfoWindowViewModel(cp)
                    {
                        UseMockData = true
                    }
                };
                partsInfoWindow.ShowDialog();
                return;
            }
            await LoadPartsAsync(true);
        }
        private bool CanLoadPartsInfoCommandExecute(object p) => true;
        #endregion

        #region ShowLongSetups
        public ICommand ShowLongSetupsCommand { get; }
        private void OnShowLongSetupsCommandExecuted(object p)
        {
            var longSetupParts = Parts.SelectMany(cp => cp.Parts.Where(p => p.SetupTimeFactIncludePartialAndDowntimes > AppSettings.LongSetupLimit)).OrderBy(p => p.StartSetupTime);
            if (!longSetupParts.Any())
            {
                MessageBox.Show("За выбранный период нет длительных наладок", "Неа", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            using (Overlay = new())
            {
                
                LongSetupsWindow longSetupsWindow = new(longSetupParts.ToObservableCollection());
                longSetupsWindow.CenterTo(App.Current.MainWindow);
                longSetupsWindow.Show();
            }
        }
        private bool CanShowLongSetupsCommandExecute(object p) => !InProgress;
        #endregion

        #region ShowMonitor
        public ICommand ShowMonitorCommand { get; }
        private void OnShowMonitorCommandExecuted(object p)
        {
            using (Overlay = new())
            {
                FanucMonitor fanucMonitor = new();
                fanucMonitor.CenterTo(App.Current.MainWindow);
                fanucMonitor.Show();
            }
        }
        private bool CanShowMonitorCommandExecute(object p) => !InProgress;
        #endregion

        #region ShowAbout
        public ICommand ShowAboutCommand { get; }
        private void OnShowAboutCommandExecuted(object p)
        {
            using (Overlay = new())
            {
                MessageBox.Show($"Тут могла быть ваша реклама.\n\n\t{App.CreateUniqueEventName()}", "О программе", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private bool CanShowAboutCommandExecute(object p) => !InProgress;
        #endregion

        #region ShowPartsInfo
        public ICommand ShowPartsInfoCommand { get; }
        private void OnShowPartsInfoCommandExecuted(object p)
        {

            using (Overlay = new())
            {
                var partsInfo = p is CombinedParts cp ? cp : new CombinedParts("Все станки", FromDate, ToDate) { Parts = Parts.SelectMany(cp => cp.Parts).OrderBy(p => p.StartSetupTime).ToObservableCollection() };

                partsInfo.FromDate = FromDate;
                partsInfo.ToDate = ToDate;
                var partsInfoWindow = new PartsInfoWindow(partsInfo);
                partsInfoWindow.CenterTo(App.Current.MainWindow);
                partsInfoWindow.Closed += (_, _) => _ = LoadPartsAsync();
                partsInfoWindow.Show();
            }
        }

        private bool CanShowPartsInfoCommandExecute(object p) => true;
        #endregion

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

        #region SetSpecificMonthCommand
        public ICommand SetSpecificMonthCommand { get; }
        private void OnSetSpecificMonthCommandExecuted(object p)
        {
            if (!int.TryParse(p?.ToString(), out var month)) return;
            var year = ToDate.Year;
            LockUpdate();
            FromDate = new DateTime(year, month, 1);
            ToDate = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            UnlockUpdate();
        }
        private bool CanSetSpecificMonthCommandExecute(object p) => true;
        #endregion

        #region SetSpecificYearCommand
        public ICommand SetSpecificYearCommand { get; }
        private void OnSetSpecificYearCommandExecuted(object p)
        {
            if (!int.TryParse(p?.ToString(), out var year)) return;
            LockUpdate();
            FromDate = new DateTime(year, 1, 1);
            ToDate = new DateTime(year, 12, 31);
            UnlockUpdate();
        }
        private bool CanSetSpecificYearCommandExecute(object p) => true;
        #endregion

        #region ShowActiveInstances
        public ICommand ShowActiveInstancesCommand { get; }

        private async void OnShowActiveInstancesCommandExecuted(object p)
        {
            if (string.IsNullOrWhiteSpace(AppSettings.Instance.ConnectionString))
            {
                MessageBox.Show("Строка подключения не настроена.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            List<AppPresence> instances;

            try
            {
                InProgress = true;
                Status = "Получение активных экземпляров...";
                instances = await Database.ReadActiveInstancesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при получении данных:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                Status = string.Empty;
                InProgress = false;
            }

            if (!instances.Any())
            {
                MessageBox.Show("Активных экземпляров не обнаружено.", "Активные экземпляры", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using (Overlay = new())
            {
                var window = new ActiveInstancesWindow()
                {
                    Owner = Application.Current.MainWindow
                };
                window.ShowDialog();
            }
        }

        private bool CanShowActiveInstancesCommandExecute(object p) => IsAdministrator && !InProgress;
        #endregion
        #endregion

        private async Task LoadPartsAsync(bool first = false)
        {
            if (lockUpdate)
            {
                _updatePending = true;
                return;
            }

            lock (_debounceLock)
            {
                _debounceTokenSource?.Cancel();
                _debounceTokenSource = new CancellationTokenSource();
            }

            var debounceToken = _debounceTokenSource.Token;

            try
            {
                if (!first)
                {
                    try
                    {
                        await Task.Delay(300, debounceToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                if (debounceToken.IsCancellationRequested)
                    return;

                _updatePending = false;

                if (string.IsNullOrWhiteSpace(AppSettings.Instance.ConnectionString))
                {
                    MessageBox.Show(
                        "Перейдите в параметры приложения и настройте строку подключения к базе данных.",
                        "Приложение не настроено.",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                _cancellationTokenSource.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();
                var cancellationToken = _cancellationTokenSource.Token;

                bool semaphoreAcquired = false;
                try
                {
                    await semaphoreSlim.WaitAsync(cancellationToken);
                    semaphoreAcquired = true;
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var sw = Stopwatch.StartNew();

                try
                {
                    InProgress = true;

                    // Обновление настроек 
                    var t0 = sw.Elapsed;
                    Status = "Обновление настроек...";
                    await Util.UpdateAppSettingsAsync();
                    OnPropertyChanged(nameof(IsAdministrator));
                    Util.WriteLog($"[LoadParts] UpdateAppSettings: {(sw.Elapsed - t0).TotalMilliseconds:F0} ms");

                    // Справочники
                    var t1 = sw.Elapsed;
                    Status = "Загрузка справочников...";

                    var machinesTask = Task.Run(Database.ReadMachines, cancellationToken);
                    var downtimeTask = Task.Run(Database.ReadDowntimeReasons, cancellationToken);
                    var setupTask = Task.Run(() => Database.ReadDeviationReasons(DeviationReasonType.Setup), cancellationToken);
                    var machiningTask = Task.Run(() => Database.ReadDeviationReasons(DeviationReasonType.Machining), cancellationToken);

                    await Task.WhenAll(machinesTask, downtimeTask, setupTask, machiningTask);
#if DEBUG
                    Util.WriteLog($"[LoadParts] Справочники (параллельно): {(sw.Elapsed - t1).TotalMilliseconds:F0} ms"); 
#endif

                    var (mr, machines) = machinesTask.Result;
                    var (dr, downtimes) = downtimeTask.Result;
                    var (sr, setup) = setupTask.Result;
                    var (cr, machining) = machiningTask.Result;

                    if (mr != DbResult.Ok) { ShowDbError(mr, "список станков"); return; }
                    if (dr != DbResult.Ok) { ShowDbError(dr, "причины простоев"); return; }
                    if (sr != DbResult.Ok) { ShowDbError(sr, "причины отклонений наладки"); return; }
                    if (cr != DbResult.Ok) { ShowDbError(cr, "причины отклонений изгот."); return; }

                    Machines = machines;
                    AppSettings.Instance.UnspecifiedDowntimesReasons = downtimes;
                    AppSettings.Instance.SetupReasons = setup;
                    AppSettings.Instance.MachiningReasons = machining;
                    OnPropertyChanged(nameof(IsAdministrator));

                    var t5 = sw.Elapsed;
                    Status = "Получение статусов отчётов...";

                    var machinesCopy = Machines.ToList();

                    var reportStatesTask = Task.Run(() =>
                    {
                        var list = new List<(string Machine, ReportState State, bool IsChecked)>();

                        foreach (var machine in machinesCopy)
                        {
                            ReportState state = ReportState.NotExist;
                            bool dayChecked = false;
                            bool nightChecked = false;

                            if (FromDate == ToDate)
                            {
                                bool dayExist = false;
                                bool nightExist = false;

                                if (Database.ReadShiftInfo(new ShiftInfo(ToDate, ShiftType.Day, machine), out var dayShifts) is DbResult.Ok
                                    && dayShifts.Count > 0 && dayShifts[0].Master != "")
                                {
                                    dayExist = true;
                                    dayChecked = dayShifts.Any(s => s.IsChecked);
                                }

                                if (Database.ReadShiftInfo(new ShiftInfo(ToDate, ShiftType.Night, machine), out var nightShifts) is DbResult.Ok
                                    && nightShifts.Count > 0 && nightShifts[0].Master != "")
                                {
                                    nightExist = true;
                                    nightChecked = nightShifts.Any(s => s.IsChecked);
                                }

                                state = (dayExist, nightExist) switch
                                {
                                    (true, true) => ReportState.Exist,
                                    (true, false) => ReportState.Partial,
                                    (false, true) => ReportState.Partial,
                                    _ => ReportState.NotExist,
                                };
                            }

                            list.Add((machine, state, dayChecked && nightChecked));
                        }

                        return list;
                    }, cancellationToken);

                    var totalShiftsTask = Task.Run(() =>
                    {
                        Database.GetShiftsByPeriod(machinesCopy, FromDate, ToDate,
                            new Shift(ShiftType.All), out var shifts);
                        return shifts;
                    }, cancellationToken);

                    await Task.WhenAll(reportStatesTask, totalShiftsTask);
                    _totalShifts = totalShiftsTask.Result;
                    var reportStates = reportStatesTask.Result;

#if DEBUG
                    Util.WriteLog($"[LoadParts] ReadShiftInfos ({machinesCopy.Count} станков): {(sw.Elapsed - t5).TotalMilliseconds:F0} ms");
#endif

                    var t6 = sw.Elapsed;
                    Status = "Построение списка деталей...";

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var list = reportStates.Select(r =>
                            new CombinedParts(r.Machine, FromDate, ToDate)
                            {
                                IsReportExist = r.State,
                                IsReportChecked = r.IsChecked,
                            }).ToList();

                        Parts.ReplaceAll(list);
                        OnPropertyChanged(nameof(TotalMachinesCount));
                        OnPropertyChanged(nameof(TotalMachinesCountForPeriod));
                        OnPropertyChanged(nameof(ReportsExistCount));
                        OnPropertyChanged(nameof(ReportsExistCountForPeriod));
                        OnPropertyChanged(nameof(CheckedReportsCount));
                        OnPropertyChanged(nameof(CheckedReportsCountForPeriod));
                        OnPropertyChanged(nameof(ReportsSummary));
                        OnPropertyChanged(nameof(CheckedSummary));
                        OnPropertyChanged(nameof(ReportsSummaryForPeriod));
                        OnPropertyChanged(nameof(CheckedSummaryForPeriod));
                    });

#if DEBUG
                    Util.WriteLog($"[LoadParts] UI update (Parts collection): {(sw.Elapsed - t6).TotalMilliseconds:F0} ms"); 
#endif

                    var t7 = sw.Elapsed;
                    Status = "Загрузка деталей...";

                    var partsCopy = Parts.ToList();

                    await Parallel.ForEachAsync(
                        partsCopy,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = 4,
                            CancellationToken = cancellationToken
                        },
                        async (part, ct) =>
                        {
                            var tPart = sw.Elapsed;
                            try
                            {
                                var partsData = await Database.ReadPartsByShiftDateAndMachine(
                                    FromDate, ToDate, part.Machine, ct);

                                await Application.Current.Dispatcher.InvokeAsync(
                                    () => part.Parts = partsData);

#if DEBUG
                                Util.WriteLog($"[LoadParts] ReadParts({part.Machine}): {(sw.Elapsed - tPart).TotalMilliseconds:F0} ms"); 
#endif
                            }
                            catch (OperationCanceledException)
                            {
                                Util.WriteLog($"[LoadParts] ReadParts({part.Machine}): отменено");
                            }
                            catch (SqlException sqlEx)
                            {
                                Util.WriteLog(sqlEx, $"[LoadParts] SqlException при загрузке {part.Machine}");
                                var message = sqlEx.Number switch
                                {
                                    SqlErrorCode.NoConnection => StatusTips.NoConnectionToDb,
                                    SqlErrorCode.AuthError => StatusTips.AuthFailedToDb,
                                    _ => $"Ошибка БД №{sqlEx.Number}\n{sqlEx.Message}",
                                };
                                await Application.Current.Dispatcher.InvokeAsync(
                                    () => MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error));
                            }
                            catch (Exception ex)
                            {
                                Util.WriteLog(ex, $"[LoadParts] Exception при загрузке {part.Machine}");
                                await Application.Current.Dispatcher.InvokeAsync(
                                    () => MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error));
                            }
                        });

#if DEBUG
                    Util.WriteLog($"[LoadParts] ReadParts (все станки): {(sw.Elapsed - t7).TotalMilliseconds:F0} ms");
                    Util.WriteLog($"[LoadParts] ИТОГО: {sw.Elapsed.TotalMilliseconds:F0} ms");
#endif
                }
                finally
                {
                    Status = "";
                    InProgress = false;
                    if (semaphoreAcquired)
                        semaphoreSlim.Release();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Непредвиденная ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Показывает стандартное сообщение об ошибке БД.</summary>
        private static void ShowDbError(DbResult result, string entity)
        {
            var message = result switch
            {
                DbResult.AuthError => $"Не удалось получить {entity} из-за неудачной авторизации в БД.",
                DbResult.NoConnection => "Нет соединения с базой данных.",
                _ => $"Не удалось получить {entity} из-за ошибки.",
            };
            MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }


        private void LockUpdate() => lockUpdate = true;

        private void UnlockUpdate()
        {
            lockUpdate = false;

            if (_updatePending)
            {
                _ = LoadPartsAsync();
            }
        }

        public static async Task<CombinedParts> GenerateMockDataAsync(string machine, DateTime fromDate, DateTime toDate)
        {
            Random random = new();
            var combinedParts = new CombinedParts(machine, fromDate, toDate)
            {
                IsReportExist = (ReportState)random.Next(0, 3),
                IsReportChecked = random.NextDouble() > 0.5,
                Parts = await Util.GenerateMockPartsAsync()
            };
            return combinedParts;
        }

        private async Task BackgroundWorkerAsync()
        {
            try
            {
                var currentProcessPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentProcessPath)) return;

                var parentDirectory = Directory.GetParent(currentProcessPath);
                if (parentDirectory is null) return;
                if (parentDirectory.Name.Equals("update", StringComparison.OrdinalIgnoreCase) == true) return;

                var updateDirectory = Path.Combine(parentDirectory!.FullName ,"update");
                if (!Directory.Exists(updateDirectory))
                    Directory.CreateDirectory(updateDirectory);

                var fileName = Path.GetFileName(currentProcessPath);
                var updateFilePath = Path.Combine(updateDirectory, fileName);

                _watcher = new FileSystemWatcher(updateDirectory)
                {
                    Filter = fileName,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                _watcher.Changed += async (sender, e) => await OnFileChangedAsync(updateFilePath, currentProcessPath);
                _watcher.Created += async (sender, e) => await OnFileChangedAsync(updateFilePath, currentProcessPath);
                _watcher.Renamed += async (sender, e) => await OnFileChangedAsync(updateFilePath, currentProcessPath);

                await Task.Delay(Timeout.Infinite, _bgCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
            }
        }

        private async Task OnFileChangedAsync(string updatePath, string currentProcessPath)
        {
            try
            {
                if (Interlocked.CompareExchange(ref _showed, 1, 0) != 0)
                    return;

 
                if (!File.Exists(updatePath) || !updatePath.IsFileNewerThan(currentProcessPath))
                    return;

                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    using (Overlay = new())
                    {
                        var result = MessageBox.Show(
                            "Для обновления закройте приложение и подождите 5-10 минут.\nЗакрыть сейчас?",
                            "Доступно обновление электронного журнала",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            App.Current.Dispatcher.InvokeShutdown();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
            }
        }


        public void StopBackgroundWorker()
        {
            _bgCts.Cancel();
        }
    }
}