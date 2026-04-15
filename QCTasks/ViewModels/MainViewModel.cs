using libeLog.Infrastructure;
using libeLog.Infrastructure.Enums;
using libeLog.Models;
using QCTasks.Commands;
using QCTasks.Models;
using QCTasks.Services;
using QCTasks.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace QCTasks.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(30);
    private GoogleSheet? _googleSheet;
    private DbService _db;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private QcNotificationService? _notify;
    private QcUser? _user;

    // Карта: (PartName, Order) -> ID строки в qc_inspections
    // Заполняется при "В работу" и при восстановлении после рестарта
    private readonly Dictionary<(string, string), int> _activeDbIds = new();

    private bool _isLoading;
    private bool _isConfigured;
    private bool _showCompletedTasks = true;
    private int _tasksLimit = 0;
    private string _statusMessage = "";

    public ObservableCollection<ProductionTaskData> Tasks { get; } = new();
    public ObservableCollection<ProductionTaskData> CompletedTasks { get; } = new();
    public ObservableCollection<ProductionTaskData> DisplayedTasks { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        private set 
        { 
            Set(ref _isLoading, value); 
            CommandManager.InvalidateRequerySuggested(); 
        }

    }

    public bool IsConfigured
    {
        get => _isConfigured;
        private set
        {
            if (!Set(ref _isConfigured, value)) return;
            OnPropertyChanged(nameof(IsNotConfigured));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsNotConfigured => !_isConfigured;

    public bool HasCompletedTasks => CompletedTasks.Count > 0;
    public bool IsTasksEmpty => DisplayedTasks.Count == 0 && !IsLoading;

    /// <summary>Видимость секции завершённых задач.</summary>
    public bool ShowCompletedTasks
    {
        get => _showCompletedTasks;
        set
        {
            Set(ref _showCompletedTasks, value);
            OnPropertyChanged(nameof(CompletedSectionVisible));
            OnPropertyChanged(nameof(ToggleCompletedLabel));
        }
    }

    public bool CompletedSectionVisible => ShowCompletedTasks && HasCompletedTasks;
    public string ToggleCompletedLabel => ShowCompletedTasks ? "Скрыть выполненные" : "Показать выполненные";

    /// <summary>0 = без ограничений.</summary>
    public int TasksLimit
    {
        get => _tasksLimit;
        set
        {
            Set(ref _tasksLimit, value);
            OnPropertyChanged(nameof(TasksLimitLabel));
            OnPropertyChanged(nameof(IsLimitActive));
            RebuildDisplayedTasks();
        }
    }

    public string TasksLimitLabel => _tasksLimit == 0 ? "Все" : _tasksLimit.ToString();
    public bool IsLimitActive => _tasksLimit > 0;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public QcUser? User
    {
        get => _user;
        private set => Set(ref _user, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand StartWorkCommand { get; }
    public ICommand CompleteCommand { get; }
    public ICommand ChangeStatusCommand { get; }
    public ICommand ChangeUserCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ToggleCompletedCommand { get; }
    public ICommand SetTasksLimitCommand { get; }

    public Window? OwnerWindow { get; set; }

    public MainViewModel()
    {
        _db = new DbService(AppSettings.Instance.SqlConnectionString);

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => IsConfigured && !IsLoading);
        StartWorkCommand = new RelayCommand<ProductionTaskData>(task => _ = StartWorkAsync(task));
        CompleteCommand = new RelayCommand<ProductionTaskData>(task => _ = CompleteTaskAsync(task));
        ChangeStatusCommand = new RelayCommand<ProductionTaskData>(task => _ = ChangeStatusAsync(task));
        ChangeUserCommand = new RelayCommand(() => _ = ChangeUserAsync(), () => _db.IsAvailable);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ToggleCompletedCommand = new RelayCommand(() => ShowCompletedTasks = !ShowCompletedTasks);
        SetTasksLimitCommand = new RelayCommand<int>(limit => TasksLimit = limit);

        Tasks.CollectionChanged += (_, _) =>
        {
            RebuildDisplayedTasks();
            OnPropertyChanged(nameof(IsTasksEmpty));
        };
        CompletedTasks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCompletedTasks));
            OnPropertyChanged(nameof(CompletedSectionVisible));
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await RefreshAsync();

        ApplyConfig();

        _ = ChangeUserAsync();
    }

    private async Task ChangeUserAsync()
    {
        if (!_db.IsAvailable) return;
        while (OwnerWindow is not { IsLoaded: true })
            await Task.Delay(100);

        string? code;
        do
        {
            code = InputDialog.Show(OwnerWindow, "Авторизация", "Отсканируйте штрихкод", showInput: false);
            if (!string.IsNullOrWhiteSpace(code)) User = await _db.GetQcUserByCodeAsync(code);
            AppSettings.CurrentUser = User;
        }
        while (_db.IsAvailable && User == null);
    }

    private void RebuildDisplayedTasks()
    {
        DisplayedTasks.Clear();
        var source = _tasksLimit > 0 ? Tasks.Take(_tasksLimit) : Tasks;
        foreach (var t in source)
            DisplayedTasks.Add(t);
        OnPropertyChanged(nameof(IsTasksEmpty));
    }

    private void ApplyConfig()
    {
        var cfg = AppSettings.Instance;
        var problem = Validate(cfg);
        if (problem is not null)
        {
            IsConfigured = false;
            StatusMessage = problem;
            _timer.Stop();
            return;
        }

        IsConfigured = true;
        _notify = new QcNotificationService(cfg);
        _googleSheet = new GoogleSheet(cfg.CredentialsFile, cfg.SheetId);
        _db = new DbService(cfg.SqlConnectionString);
        StatusMessage = "Загрузка...";
        _ = RefreshAsync();
    }

    private static string? Validate(AppSettings cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.CredentialsFile))
            return "Не указан файл учётных данных Google.";

        if (!File.Exists(cfg.CredentialsFile))
            return $"Файл учётных данных не найден: {cfg.CredentialsFile}";

        if (string.IsNullOrWhiteSpace(cfg.SheetId))
            return "Не указан ID таблицы Google Sheets.";

        return null;
    }

    public async Task RefreshAsync()
    {
        if (!IsConfigured || _googleSheet is null) return;

        IsLoading = true;
        StatusMessage = "Обновление данных...";
        _timer.Stop();

        if (!string.IsNullOrEmpty(AppSettings.Instance.PathToRecipients) && File.Exists(AppSettings.Instance.PathToRecipients))
        {
            var recipientTypes = new[] {
                ReceiversType.ProcessEngineeringDepartment,
                ReceiversType.ProductionSupervisors,
                ReceiversType.QualityControl,
            };
            var recipients = recipientTypes.SelectMany(r => Utils.ReadReceiversFromFile(r, AppSettings.Instance.PathToRecipients))
                .Distinct()
                .ToList();
            if (recipients.Count > 0) AppSettings.Instance.RejectionNotifyRecipients = recipients;
        }

        try
        {
            var data = await _googleSheet.GetProductionTasksData(
                "ОТК", new string[] { "ОТК" }, new Progress<string>(), _cts.Token);

            Tasks.Clear();
            CompletedTasks.Clear();

            foreach (var task in data)
            {
                bool isDone = task.EngeneersComment is "Принято" or "Отклонено";

                if (isDone)
                {
                    if (!CompletedTasks.Any(t => t.PartName == task.PartName && t.Order == task.Order))
                        CompletedTasks.Add(task);
                }
                else
                {
                    if (!Tasks.Any(t => t.PartName == task.PartName && t.Order == task.Order))
                        Tasks.Add(task);
                }
            }
            await RestoreActiveDbIdsAsync();

            StatusMessage = $"Обновлено: {DateTime.Now:HH:mm:ss}  •  интервал: {TimerInterval.TotalSeconds} сек.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка обновления ({DateTime.Now:HH:mm:ss}): {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _timer.Start();
            RebuildDisplayedTasks();
            OnPropertyChanged(nameof(RefreshCommand.CanExecute));
        }
    }

    /// <summary>
    /// После рестарта приложения для позиций "В работе" пытаемся найти
    /// незакрытые строки в БД, чтобы корректно завершить их при нажатии "Завершить".
    /// </summary>
    private async Task RestoreActiveDbIdsAsync()
    {
        foreach (var task in Tasks.Where(t => t.EngeneersComment == "В работе"))
        {
            var key = (task.PartName, task.Order);
            if (_activeDbIds.ContainsKey(key)) continue;

            var id = await _db.FindActiveInspectionAsync(task.PartName, task.Order);
            if (id.HasValue)
                _activeDbIds[key] = id.Value;
        }
    }

    /// <summary>Запуск задачи.</summary>
    private async Task StartWorkAsync(ProductionTaskData? task)
    {
        if (task is null || !IsConfigured) return;

        _timer.Stop();

        var staleId = await _db.FindActiveInspectionAsync(task.PartName, task.Order);
        if (staleId.HasValue)
            await _db.CancelInspectionAsync(staleId.Value);

        task.EngeneersComment = "В работе";
        int idx = Tasks.IndexOf(task);
        if (idx >= 0)
        {
            Tasks.RemoveAt(idx);
            Tasks.Insert(idx, task);
        }

        await WriteStatusAsync(task);

        // Пишем в БД
        var dbId = await _db.StartInspectionAsync(task.PartName, task.Order, task.PartsCount);
        if (dbId.HasValue)
            _activeDbIds[(task.PartName, task.Order)] = dbId.Value;

        _timer.Start();
    }

    /// <summary>Завершение задачи.</summary>
    private async Task CompleteTaskAsync(ProductionTaskData? task)
    {
        if (task is null || !IsConfigured) return;

        _timer.Stop();

        var (Confirmed, Text) = InputConfirmDialog.Show(
            OwnerWindow,
            title: "Результат проверки",
            message: task.PartName,
            detail: string.IsNullOrWhiteSpace(task.Order) ? null : $"М/Л: {task.Order}",
            yesText: "Принято",
            noText: "Отклонено",
            cancelText: "Отмена", 
            disallowNoOnEmpty: true);

        if (Confirmed is null)
        {
            _timer.Start();
            return;
        }

        bool accepted = Confirmed.Value;
        task.EngeneersComment = accepted ? "Принято" : "Отклонено";
        task.QcComment = Text;

        Tasks.Remove(task);
        if (!CompletedTasks.Contains(task))
            CompletedTasks.Add(task);
        if (!accepted && _notify is { } notify && notify.IsAvailable)
        {
            var recipients = AppSettings.Instance.RejectionNotifyRecipients;
            if (recipients.Count > 0)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        notify.SendRejectionNotice(task, task.QcComment ?? "", recipients);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Notify] {ex.Message}");
                    }
                });
            }
        }
        await WriteStatusAsync(task);


        var key = (task.PartName, task.Order);
        if (_activeDbIds.TryGetValue(key, out int dbId))
        {
            await _db.CompleteInspectionAsync(dbId, accepted, task);
            _activeDbIds.Remove(key);
        }

        _timer.Start();
    }

    /// <summary>Изменение статуса уже завершённой позиции.</summary>
    private async Task ChangeStatusAsync(ProductionTaskData? task)
    {
        if (task is null || !IsConfigured) return;

        var (Confirmed, Text) = InputConfirmDialog.Show(
            OwnerWindow,
            title: "Изменить статус",
            message: task.PartName,
            detail: string.IsNullOrWhiteSpace(task.Order) ? null : $"М/Л: {task.Order}",
            yesText: "Принято",
            noText: "Отклонено", 
            defaultValue: task.QcComment, 
            disallowNoOnEmpty: true);

        if (Confirmed is null) return;
        bool accepted = Confirmed.Value;
        var newStatus = accepted ? "Принято" : "Отклонено";
        var currentStatus = task.EngeneersComment;
        if (newStatus == currentStatus && Text == task.QcComment) return; // ничего не поменялось
        var statusChange = $"{currentStatus} → {newStatus}";
        task.QcComment = Text;
        int idx = CompletedTasks.IndexOf(task);
        if (idx >= 0)
        {
            task.EngeneersComment = accepted ? "Принято" : "Отклонено";
            CompletedTasks.RemoveAt(idx);
            CompletedTasks.Insert(idx, task);
        }

        await WriteStatusAsync(task);
        await _db.UpdateInspectionAsync(task, statusChange);
    }

    private async Task WriteStatusAsync(ProductionTaskData task)
    {
        if (_googleSheet is null) return;

        var freshAddress = await _googleSheet.FindTaskCellAddressAsync(
            machine: "ОТК",
            machines: new[] { "ОТК" },
            partName: task.PartName,
            order: task.Order,
            cancellationToken: _cts.Token);

        if (freshAddress is null)
        {
            Console.Error.WriteLine(
                $"[WriteStatusAsync] Строка не найдена: {task.PartName} / {task.Order}");
            return;
        }

        task.CellAddress = freshAddress;

        await _googleSheet.UpdateCellValues(freshAddress, new string[] {task.EngeneersComment, task.QcComment});
    }

    private void OpenSettings()
    {
        _timer.Stop();

        var win = new SettingsWindow { Owner = OwnerWindow };
        win.ShowDialog();

        if (win.DataContext is SettingsViewModel { Saved: true })
        {
            AppSettings.Reload();
            _activeDbIds.Clear();
            ApplyConfig();
            return;
        }

        if (IsConfigured) _timer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}