using libeLog;
using libeLog.Base;
using libeLog.Extensions;
using libeLog.Infrastructure.Wrappers;
using libeLog.Interfaces;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using remeLog.Core.Services;
using remeLog.Infrastructure;
using remeLog.Infrastructure.Types;
using remeLog.Infrastructure.Winnum;
using remeLog.Models;
using remeLog.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using static libeLog.Constants;
using static remeLog.Models.CombinedParts;
using Application = System.Windows.Application;
using libeLog.Views;

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
        private bool _aiHealthMonitorStarted = false;
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
            ShowBatchAiAnalysisCommand = new LambdaCommand(OnShowBatchAiAnalysisCommandExecuted, CanShowBatchAiAnalysisCommandExecute);
            EditOperatorsCommand = new LambdaCommand(OnEditOperatorsCommandExecuted, CanEditOperatorsCommandExecute);
            EditSerialPartsCommand = new LambdaCommand(OnEditSerialPartsCommandExecuted, CanEditSerialPartsCommandExecute);
            ShowAboutCommand = new LambdaCommand(OnShowAboutCommandExecuted, CanShowAboutCommandExecute);
            ShowPartsInfoCommand = new LambdaCommand(OnShowPartsInfoCommandExecuted, CanShowPartsInfoCommandExecute);
            IncreaseDateCommand = new LambdaCommand(OnIncreaseDateCommandExecuted, CanIncreaseDateCommandExecute);
            DecreaseDateCommand = new LambdaCommand(OnDecreaseDateCommandExecuted, CanDecreaseDateCommandExecute);
            SetYesterdayDateCommand = new LambdaCommand(OnSetYesterdayDateCommandExecuted, CanSetYesterdayDateCommandExecute);
            SetSpecificDateCommand = new LambdaCommand(OnSetSpecificDateCommandExecuted, CanSetSpecificDateCommandExecute);
            SetWeekDateCommand = new LambdaCommand(OnSetWeekDateCommandExecuted, CanSetWeekDateCommandExecute);
            SetMonthDateCommand = new LambdaCommand(OnSetMonthDateCommandExecuted, CanSetMonthDateCommandExecute);
            SetYearDateCommand = new LambdaCommand(OnSetYearDateCommandExecuted, CanSetYearDateCommandExecute);
            SetSpecificMonthCommand = new LambdaCommand(OnSetSpecificMonthCommandExecuted, CanSetSpecificMonthCommandExecute);
            SetSpecificYearCommand = new LambdaCommand(OnSetSpecificYearCommandExecuted, CanSetSpecificYearCommandExecute);
            ShowActiveInstancesCommand = new LambdaCommand(OnShowActiveInstancesCommandExecuted, CanShowActiveInstancesCommandExecute);
            ShowMachineActivityCommand = new LambdaCommand(OnShowMachineActivityCommandExecuted, CanShowMachineActivityCommandExecute);
            OpenMachineInspectionCalendarCommand = new LambdaCommand(OnOpenMachineInspectionCalendarCommandExecuted, CanOpenMachineInspectionCalendarCommandExecute);
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

        public bool IsAiAvailable => AiHealthMonitor.Instance.IsAiAvailable;
        public bool IsServerAvailable => AiHealthMonitor.Instance.IsServerAvailable;
        public bool IsOllamaAvailable => AiHealthMonitor.Instance.IsOllamaAvailable;
        public string? HealthError => AiHealthMonitor.Instance.HealthError;
        public string? HealthTooltip => AiHealthMonitor.Instance.HealthTooltip;

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

        public bool HasFeatureAi => Util.HasFeature(RemeLogFeature.Ai);
        public bool HasFeatureAdvancedEdit => Util.HasFeature(RemeLogFeature.AdvancedEdit);
        public bool HasFeatureInstances => Util.HasFeature(RemeLogFeature.Instances);
        public bool HasFeatureValidationOverride => Util.HasFeature(RemeLogFeature.ValidationOverride);

        private void OnAiHealthChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AiHealthMonitor.IsServerAvailable))
                OnPropertyChanged(nameof(IsServerAvailable));
            if (e.PropertyName == nameof(AiHealthMonitor.IsOllamaAvailable))
                OnPropertyChanged(nameof(IsOllamaAvailable));
            if (e.PropertyName == nameof(AiHealthMonitor.IsAiAvailable))
                OnPropertyChanged(nameof(IsAiAvailable));
            if (e.PropertyName == nameof(AiHealthMonitor.HealthError))
                OnPropertyChanged(nameof(HealthError));
            if (e.PropertyName == nameof(AiHealthMonitor.HealthTooltip))
                OnPropertyChanged(nameof(HealthTooltip));
        }

        public string WindowTitle
        {
            get
            {
                var title = "Отчеты электронного журнала";
                if ((!Util.IsAppAdmin() || AppSettings.FeaturesExplicitlySet) && AppSettings.EnabledFeatures != RemeLogFeature.None)
                {
                    title += $" [{string.Join(", ", AppSettings.EnabledFeatures.Names())}]";
                }
                return title;
            }
        }

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
        private bool CanEditOperatorsCommandExecute(object p) => !InProgress && HasFeatureAdvancedEdit;
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
        private bool CanEditSerialPartsCommandExecute(object p) => !InProgress && HasFeatureAdvancedEdit;
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
                MessageBoxWindow.Show("За выбранный период нет длительных наладок", "Неа", MessageBoxButton.OK, MessageBoxImage.Information);
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

        #region ShowBatchAiAnalysis
        public ICommand ShowBatchAiAnalysisCommand { get; }
        private void OnShowBatchAiAnalysisCommandExecuted(object p)
        {
            using (Overlay = new())
            {
                var window = new BatchAiAnalysisWindow();
                window.CenterTo(App.Current.MainWindow);
                window.Show();
            }
        }
        private bool CanShowBatchAiAnalysisCommandExecute(object p) => !InProgress && HasFeatureAi;
        #endregion

        #region ShowAbout
        public ICommand ShowAboutCommand { get; }
        private void OnShowAboutCommandExecuted(object p)
        {
            using (Overlay = new())
            {
                var features = AppSettings.EnabledFeatures.Names();
                var featuresText = features.Length > 0 ? $"\n{string.Join("\n", features.Select(f => $"• {f}"))}" : "—";
                if (IsAdministrator && !AppSettings.FeaturesExplicitlySet)
                    featuresText = "Все (администратор)\n" + string.Join("\n", RemeLogFeatureExtensions.All.Names().Select(f => $"• {f}"));

                var version = App.CreateUniqueEventName();
                var title = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? "remeLog";
                var msg = $"{title}\n" +
                          $"Версия: {version}\n\n" +
                          $"Пользователь: {Environment.UserName}\n" +
                          $"Компьютер: {Environment.MachineName}\n\n" +
                          $"Активные фичи: {featuresText}";
                MessageBoxWindow.Show(msg, "О программе", MessageBoxButton.OK, MessageBoxImage.Information);
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

        #region SetSpecificDateCommand
        public ICommand SetSpecificDateCommand { get; }
        /// <summary> Устанавливает выбранную в пикере дату сразу в оба календаря (От и До). </summary>
        private void OnSetSpecificDateCommandExecuted(object p)
        {
            if (p is not DateTime date) return;
            LockUpdate();
            FromDate = date.Date;
            ToDate = date.Date;
            UnlockUpdate();
        }
        private bool CanSetSpecificDateCommandExecute(object p) => true;
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
                MessageBoxWindow.Show("Строка подключения не настроена.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBoxWindow.Show($"Ошибка при получении данных:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                Status = string.Empty;
                InProgress = false;
            }

            if (!instances.Any())
            {
                MessageBoxWindow.Show("Активных экземпляров не обнаружено.", "Активные экземпляры", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private bool CanShowActiveInstancesCommandExecute(object p) => HasFeatureInstances && !InProgress;
        #endregion

        #region ShowMachineActivity
        public ICommand ShowMachineActivityCommand { get; }

        private void OnShowMachineActivityCommandExecuted(object p)
        {
            foreach (Window w in Application.Current.Windows)
            {
                if (w is MachineActivityWindow existing)
                {
                    existing.Activate();
                    return;
                }
            }

            new MachineActivityWindow
            {
                Owner = Application.Current.MainWindow
            }.Show();
        }

        private bool CanShowMachineActivityCommandExecute(object p) => true;
        #endregion

        #region OpenMachineInspectionCalendar
        public ICommand OpenMachineInspectionCalendarCommand { get; }
        private void OnOpenMachineInspectionCalendarCommandExecuted(object p)
        {
            var vm = new MachineInspectionCalendarViewModel(FromDate, ToDate);
            var window = new MachineInspectionCalendarWindow(vm)
            {
                Owner = Application.Current.MainWindow
            };
            window.Show();
        }
        private bool CanOpenMachineInspectionCalendarCommandExecute(object p) => !InProgress;
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
                    MessageBoxWindow.Show(
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
                    OnPropertyChanged(nameof(HasFeatureAi));
                    OnPropertyChanged(nameof(HasFeatureAdvancedEdit));
                    OnPropertyChanged(nameof(HasFeatureInstances));
                    OnPropertyChanged(nameof(WindowTitle));
                    Util.WriteLog($"[LoadParts] UpdateAppSettings: {(sw.Elapsed - t0).TotalMilliseconds:F0} ms");

                    // Защита от старой сборки поверх более новой БД: если версия схемы в БД выше,
                    // чем знает эта сборка, дальнейшая работа с данными (чтение/сохранение Parts)
                    // рискует читать/писать не туда — прямо блокируем, не даём тихо испортить данные.
                    if (AppSettings.SchemaVersion > AppSettings.RequiredSchemaVersion)
                    {
                        MessageBoxWindow.Show(
                            $"База данных обновлена до версии {AppSettings.SchemaVersion}, а эта сборка remeLog " +
                            $"рассчитана на версию {AppSettings.RequiredSchemaVersion}. Работа с данными заблокирована " +
                            "во избежание порчи — обновите remeLog до актуальной версии.",
                            "Требуется обновление remeLog",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    if (HasFeatureAi && !_aiHealthMonitorStarted)
                    {
                        _aiHealthMonitorStarted = true;
                        AiHealthMonitor.Instance.PropertyChanged += OnAiHealthChanged;
                        AiHealthMonitor.Instance.Start();
                    }

                    // Справочники
                    var t1 = sw.Elapsed;
                    Status = "Загрузка справочников...";

                    var referenceData = await MainDashboardService.LoadReferenceDataAsync(cancellationToken);
#if DEBUG
                    Util.WriteLog($"[LoadParts] Справочники (параллельно): {(sw.Elapsed - t1).TotalMilliseconds:F0} ms");
#endif

                    var mr = referenceData.Machines.Status; var machines = referenceData.Machines.Value ?? new List<string>();
                    var dr = referenceData.DowntimeReasons.Status; var downtimes = referenceData.DowntimeReasons.Value ?? new List<string>();
                    var sr = referenceData.SetupReasons.Status; var setup = referenceData.SetupReasons.Value ?? new List<(string, bool)>();
                    var cr = referenceData.MachiningReasons.Status; var machining = referenceData.MachiningReasons.Value ?? new List<(string, bool)>();

                    if (mr != remeLog.Core.Db.DbResult.Ok) { ShowDbError(mr, "список станков"); return; }
                    if (dr != remeLog.Core.Db.DbResult.Ok) { ShowDbError(dr, "причины простоев"); return; }
                    if (sr != remeLog.Core.Db.DbResult.Ok) { ShowDbError(sr, "причины отклонений наладки"); return; }
                    if (cr != remeLog.Core.Db.DbResult.Ok) { ShowDbError(cr, "причины отклонений изгот."); return; }

                    Machines = machines;
                    AppSettings.Instance.UnspecifiedDowntimesReasons = downtimes;
                    AppSettings.Instance.SetupReasons = setup;
                    AppSettings.Instance.MachiningReasons = machining;
                    OnPropertyChanged(nameof(IsAdministrator));
                    OnPropertyChanged(nameof(HasFeatureAi));
                    OnPropertyChanged(nameof(HasFeatureAdvancedEdit));
                    OnPropertyChanged(nameof(HasFeatureInstances));
                    OnPropertyChanged(nameof(WindowTitle));

                    var t5 = sw.Elapsed;
                    Status = "Получение статусов отчётов...";

                    var machinesCopy = Machines.ToList();

                    var shiftOverview = await MainDashboardService.LoadShiftOverviewAsync(machinesCopy, FromDate, ToDate, cancellationToken);
                    _totalShifts = shiftOverview.TotalShifts;
                    var reportStates = shiftOverview.ReportStates;

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
                                    () => MessageBoxWindow.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error));
                            }
                            catch (Exception ex)
                            {
                                Util.WriteLog(ex, $"[LoadParts] Exception при загрузке {part.Machine}");
                                await Application.Current.Dispatcher.InvokeAsync(
                                    () => MessageBoxWindow.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error));
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

                    // Разовая проверка сразу после старта приложения, когда все
                    // инициализации завершены, — чтобы статус ИИ был виден сразу,
                    // не дожидаясь первого тика периодического таймера.
                    if (first && HasFeatureAi)
                        _ = AiHealthMonitor.Instance.CheckNowAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBoxWindow.Show($"Непредвиденная ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Показывает стандартное сообщение об ошибке БД.</summary>
        private static void ShowDbError(remeLog.Core.Db.DbResult result, string entity)
        {
            var message = result switch
            {
                remeLog.Core.Db.DbResult.AuthError => $"Не удалось получить {entity} из-за неудачной авторизации в БД.",
                remeLog.Core.Db.DbResult.NoConnection => "Нет соединения с базой данных.",
                _ => $"Не удалось получить {entity} из-за ошибки.",
            };
            MessageBoxWindow.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        var result = MessageBoxWindow.Show(
                            "Для обновления перезапустите приложение.\nЗакрыть сейчас?",
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
            AiHealthMonitor.Instance.Stop();
        }
    }
}