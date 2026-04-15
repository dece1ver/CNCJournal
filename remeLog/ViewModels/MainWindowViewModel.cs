using libeLog;
using libeLog.Base;
using libeLog.Extensions;
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
        private readonly object lockObject = new();
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

        private ObservableCollection<CombinedParts> _Parts = new();
        /// <summary> Объединенный список объединенных списков </summary>
        public ObservableCollection<CombinedParts> Parts
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


        public bool IsSingleShift => FromDate == ToDate;

        public int TotalMachinesCount => Parts.Count;
        public int ReportsExistCount => Parts.Count(p => p.IsReportExist != ReportState.NotExist);
        public int CheckedReportsCount => Parts.Count(p => p.IsReportChecked);

        public string ReportsSummary => $"МЦ: {ReportsExistCount:00}/{TotalMachinesCount:00}";
        public string CheckedSummary => $"ТО: {CheckedReportsCount:00}/{TotalMachinesCount:00}";


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

                try
                {
                    InProgress = true;

                    if (string.IsNullOrWhiteSpace(AppSettings.Instance.ConnectionString))
                    {
                        MessageBox.Show("Перейдите в параметры приложения и настройте строку подключения к базе данных.", "Приложение не настроено.", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource = new();
                    var cancellationToken = _cancellationTokenSource.Token;
                    await semaphoreSlim.WaitAsync(cancellationToken);
                    await Util.UpdateAppSettingsAsync();

                    Constants.Dates.Holidays = await Database.GetHolidaysAsync(null);

                    Status = "Получение списка станков...";

                    switch (await Task.Run(Machines.ReadMachines))
                    {
                        case DbResult.AuthError:
                            MessageBox.Show("Не удалось получить список станков из-за неудачной авторизации в базе данных.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                        case DbResult.Error:
                            MessageBox.Show("Не удалось получить список станков из-за ошибки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                        case DbResult.NoConnection:
                            MessageBox.Show("Нет соединения с базой данных.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                    }
                    Status = "Получение списка причин неотмеченных простоев...";
                    switch (await Task.Run(AppSettings.Instance.UnspecifiedDowntimesReasons.ReadDowntimeReasons))
                    {
                        case DbResult.AuthError:
                            MessageBox.Show("Не удалось получить список причин простоев из-за неудачной авторизации в базе данных.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                        case DbResult.Error:
                            MessageBox.Show("Не удалось получить список причин простоев из-за ошибки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                        case DbResult.NoConnection:
                            MessageBox.Show("Нет соединения с базой данных.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                    }

                    Status = "Получение списка причин отклонений в наладке...";
                    switch (await Task.Run(() => AppSettings.Instance.SetupReasons.ReadDeviationReasons(DeviationReasonType.Setup)))
                    {
                        case DbResult.AuthError:
                            MessageBox.Show("Не удалось получить список причин отклонений для наладок из-за неудачной авторизации в базе данных.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                        case DbResult.Error:
                            MessageBox.Show("Не удалось получить список причин отклонений для наладок из-за ошибки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                        case DbResult.NoConnection:
                            MessageBox.Show("Нет соединения с базой данных.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                    }

                    Status = "Получение списка причин отклонений в изготовлении...";
                    switch (await Task.Run(() => AppSettings.Instance.MachiningReasons.ReadDeviationReasons(DeviationReasonType.Machining)))
                    {
                        case DbResult.AuthError:
                            MessageBox.Show("Не удалось получить список причин отклонений для изготовления из-за неудачной авторизации в базе данных.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                        case DbResult.Error:
                            MessageBox.Show("Не удалось получить список причин отклонений для изготовления из-за ошибки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                        case DbResult.NoConnection:
                            MessageBox.Show("Нет соединения с базой данных.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                            break;
                    }

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Status = "Очистка списка деталей...";
                        var list = new List<CombinedParts>();
                        foreach (var machine in Machines)
                        {
                            ReportState state = ReportState.NotExist;
                            bool dayExist = false;
                            bool nightExist = false;
                            bool dayChecked = false;
                            bool nightChecked = false;
                            Status = $"Получение информации за сутки на стнке {machine}...";
                            if (FromDate == ToDate)
                            {
                                if (Database.ReadShiftInfo(new ShiftInfo(ToDate, ShiftType.Day, machine), out var dbDayShifts) is DbResult.Ok && dbDayShifts.Count > 0 && dbDayShifts[0].Master != "")
                                {
                                    dayExist = true;
                                    if (dbDayShifts.Any(s => s.IsChecked)) dayChecked = true;
                                }

                                if (Database.ReadShiftInfo(new ShiftInfo(ToDate, ShiftType.Night, machine), out var dbNightShifts) is DbResult.Ok && dbNightShifts.Count > 0 && dbNightShifts[0].Master != "")
                                {
                                    nightExist = true;
                                    if (dbNightShifts.Any(s => s.IsChecked)) nightChecked = true;
                                }

                                if (dayExist && nightExist)
                                {
                                    state = ReportState.Exist;
                                }
                                else if (dayExist || nightExist)
                                {
                                    state = ReportState.Partial;
                                }
                            }
                            list.Add(new CombinedParts(machine, FromDate, ToDate) { IsReportExist = state, IsReportChecked = dayChecked && nightChecked });
                        }
                        Parts = list.ToObservableCollection();
                        return true;
                    });
                    Status = "Загрузка информации...";
                    foreach (var part in Parts)
                    {
                        try
                        {
                            var partsData = await Database.ReadPartsByShiftDateAndMachine(FromDate, ToDate, part.Machine, cancellationToken);
                            await App.Current.Dispatcher.InvokeAsync(() => part.Parts = partsData);
                        }
                        catch (OperationCanceledException)
                        {

                        }
                        catch (SqlException sqlEx)
                        {
                            var message = sqlEx.Number switch
                            {
                                SqlErrorCode.NoConnection => StatusTips.NoConnectionToDb,
                                SqlErrorCode.AuthError => StatusTips.AuthFailedToDb,
                                _ => $"Ошибка БД №{sqlEx.Number}\n{sqlEx.Message}",
                            };
                            MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                finally
                {
                    Status = "";
                    semaphoreSlim.Release();
                    InProgress = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Непредвиденная ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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