using libeLog.Infrastructure;
using libeLog.Models;
using QCTasks.Commands;
using QCTasks.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace QCTasks.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private GoogleSheet? _googleSheet;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _cts = new();

    private bool _isLoading;
    private bool _isConfigured;
    private string _statusMessage = "";

    public ObservableCollection<ProductionTaskData> Tasks { get; } = new();
    public ObservableCollection<ProductionTaskData> CompletedTasks { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    /// <summary>Все обязательные поля конфига заполнены и файл существует.</summary>
    public bool IsConfigured
    {
        get => _isConfigured;
        private set
        {
            if (!SetField(ref _isConfigured, value)) return;
            OnPropertyChanged(nameof(IsNotConfigured));
            // Обновляем CanExecute у RefreshCommand
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsNotConfigured => !_isConfigured;

    public bool HasCompletedTasks => CompletedTasks.Count > 0;
    public bool IsTasksEmpty => Tasks.Count == 0 && !IsLoading;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CompleteCommand { get; }
    public ICommand ChangeStatusCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    public Window? OwnerWindow { get; set; }

    public MainViewModel()
    {
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => IsConfigured && !IsLoading);
        CompleteCommand = new RelayCommand<ProductionTaskData>(task => _ = CompleteTaskAsync(task));
        ChangeStatusCommand = new RelayCommand<ProductionTaskData>(task => _ = ChangeStatusAsync(task));
        OpenSettingsCommand = new RelayCommand(OpenSettings);

        Tasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsTasksEmpty));
        CompletedTasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCompletedTasks));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await RefreshAsync();

        ApplyConfig();
    }

    // ── Конфиг ────────────────────────────────────────────────────────────

    /// <summary>
    /// Проверяет конфиг и либо стартует загрузку, либо показывает предупреждение.
    /// Вызывается при старте и после сохранения настроек.
    /// </summary>
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
        _googleSheet = new GoogleSheet(cfg.CredentialsFile, cfg.SheetId);
        StatusMessage = "Загрузка...";
        _ = RefreshAsync();
    }

    /// <summary>Возвращает текст проблемы или null если всё ок.</summary>
    private static string? Validate(AppSettings cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.CredentialsFile))
            return "Не указан файл учётных данных Google. Откройте настройки и заполните конфигурацию.";

        if (!System.IO.File.Exists(cfg.CredentialsFile))
            return $"Файл учётных данных не найден:\n{cfg.CredentialsFile}\n\nПроверьте путь в настройках.";

        if (string.IsNullOrWhiteSpace(cfg.SheetId))
            return "Не указан ID таблицы Google Sheets. Откройте настройки и заполните конфигурацию.";

        return null;
    }

    // ── Загрузка ──────────────────────────────────────────────────────────

    public async Task RefreshAsync()
    {
        if (!IsConfigured || _googleSheet is null) return;

        IsLoading = true;
        StatusMessage = "Обновление данных...";
        _timer.Stop();

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

            StatusMessage = $"Обновлено: {DateTime.Now:HH:mm:ss}  •  следующее через 30 сек";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка обновления ({DateTime.Now:HH:mm:ss}): {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            _timer.Start();
            OnPropertyChanged(nameof(IsTasksEmpty));
        }
    }

    // ── Действия ──────────────────────────────────────────────────────────

    private async Task CompleteTaskAsync(ProductionTaskData? task)
    {
        if (task is null || !IsConfigured) return;

        _timer.Stop();

        var result = MessageBox.Show(
            $"Принять или отклонить деталь?\n\n{task.PartName}\n\nДа — Принято,  Нет — Отклонено",
            "Решение по детали",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
        {
            _timer.Start();
            return;
        }

        bool accepted = result == MessageBoxResult.Yes;
        task.EngeneersComment = accepted ? "Принято" : "Отклонено";

        Tasks.Remove(task);
        if (!CompletedTasks.Contains(task))
            CompletedTasks.Add(task);

        await WriteResultAsync(task, accepted);
        _timer.Start();
    }

    private async Task ChangeStatusAsync(ProductionTaskData? task)
    {
        if (task is null || !IsConfigured) return;

        var result = MessageBox.Show(
            $"Изменить статус для:\n\n{task.PartName}\n\nДа — Принято,  Нет — Отклонено",
            "Изменить статус",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel) return;

        bool accepted = result == MessageBoxResult.Yes;

        int idx = CompletedTasks.IndexOf(task);
        if (idx >= 0)
        {
            task.EngeneersComment = accepted ? "Принято" : "Отклонено";
            CompletedTasks.RemoveAt(idx);
            CompletedTasks.Insert(idx, task);
        }

        await WriteResultAsync(task, accepted);
    }

    private async Task WriteResultAsync(ProductionTaskData task, bool accepted)
    {
        if (_googleSheet is null) return;
        await _googleSheet.UpdateCellValue(task.CellAddress, accepted ? "Принято" : "Отклонено");
    }

    private void OpenSettings()
    {
        _timer.Stop();

        var win = new SettingsWindow { Owner = OwnerWindow };
        win.ShowDialog();

        if (win.DataContext is SettingsViewModel { Saved: true })
        {
            AppSettings.Reload();
            ApplyConfig();
            return;
        }

        if (IsConfigured) _timer.Start();
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}