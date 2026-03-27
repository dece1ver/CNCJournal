using libeLog.Infrastructure;
using libeLog.Models;
using QCTasks.Commands;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace QCTasks.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly GoogleSheet _googleSheet;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _cts = new();

    private bool _isLoading;
    private string _statusMessage = "Загрузка...";

    public ObservableCollection<ProductionTaskData> Tasks { get; } = new();
    public ObservableCollection<ProductionTaskData> CompletedTasks { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

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

    public MainViewModel()
    {
        _googleSheet = new GoogleSheet(AppSettings.Instance.CredentialsFile, AppSettings.Instance.SheetId);

        RefreshCommand = new RelayCommand(
            () => _ = RefreshAsync(),
            () => !IsLoading);

        CompleteCommand = new RelayCommand<ProductionTaskData>(task => _ = CompleteTaskAsync(task));
        ChangeStatusCommand = new RelayCommand<ProductionTaskData>(task => _ = ChangeStatusAsync(task));

        Tasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsTasksEmpty));
        CompletedTasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCompletedTasks));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = "Обновление данных...";
        _timer.Stop();

        try
        {
            var data = await _googleSheet.GetProductionTasksData(
                "ОТК", new[] { "ОТК" }, new Progress<string>(), _cts.Token);

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

            StatusMessage = $"Обновлено: {DateTime.Now:HH:mm:ss}  •  Следующее обновление через 30 сек";
        }
        catch
        {
            StatusMessage = $"Ошибка обновления ({DateTime.Now:HH:mm:ss})";
        }
        finally
        {
            IsLoading = false;
            _timer.Start();
            OnPropertyChanged(nameof(IsTasksEmpty));
        }
    }

    private async Task CompleteTaskAsync(ProductionTaskData? task)
    {
        if (task is null) return;

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
        if (task is null) return;

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
        await _googleSheet.UpdateCellValue(task.CellAddress, accepted ? "Принято" : "Отклонено");
    }

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