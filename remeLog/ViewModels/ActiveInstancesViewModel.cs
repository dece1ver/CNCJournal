using libeLog;
using libeLog.Base;
using remeLog.Infrastructure;
using remeLog.Models;
using remeLog.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace remeLog.ViewModels
{
    public class ActiveInstancesViewModel : ViewModel, IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        private AppPresence? _selectedInstance;
        public AppPresence? SelectedInstance
        {
            get => _selectedInstance;
            set => Set(ref _selectedInstance, value);
        }

        private int _pendingCommandCount;
        public int PendingCommandCount
        {
            get => _pendingCommandCount;
            set => Set(ref _pendingCommandCount, value);
        }

        private bool _inProgress;
        public bool InProgress
        {
            get => _inProgress;
            set => Set(ref _inProgress, value);
        }

        private bool _allSelected;
        public bool AllSelected
        {
            get => _allSelected;
            set => Set(ref _allSelected, value);
        }

        public ObservableCollection<AppPresence> Instances { get; } = new();

        public LambdaCommand ForceCloseCommand { get; }
        public LambdaCommand ShowNotificationCommand { get; }
        public LambdaCommand ForceCloseSelectedCommand { get; }
        public LambdaCommand NotifySelectedCommand { get; }
        public LambdaCommand ToggleAllCommand { get; }

        public ActiveInstancesViewModel()
        {
            Task.Run(PollLoopAsync);

            ForceCloseCommand = LambdaCommand.Create(
                async _ =>
                {
                    try
                    {
                        var target = SelectedInstance;
                        if (target is null || !target.IsOnline) return;

                        var result = MessageBox.Show(
                            $"Принудительно закрыть экземпляр на {target.MachineName}\\{target.UserName}?",
                            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (result != MessageBoxResult.Yes) return;

                        await Database.SendAppCommandAsync(
                            target.SessionId, target.MachineName, target.UserName,
                            "ForceClose", null).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, "Ошибка при отправке команды ForceClose");
                        MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                },
                _ => SelectedInstance?.IsOnline == true);

            ShowNotificationCommand = LambdaCommand.Create(
                async _ =>
                {
                    try
                    {
                        var target = SelectedInstance;
                        if (target is null || !target.IsOnline) return;

                        var dialog = new UserInputDialogWindow(
                            "Уведомление", "Введите текст уведомления:");
                        dialog.Owner = Application.Current.MainWindow;
                        if (dialog.ShowDialog() != true) return;

                        var text = dialog.UserInput;
                        if (string.IsNullOrWhiteSpace(text)) return;

                        await Database.SendAppCommandAsync(
                            target.SessionId, target.MachineName, target.UserName,
                            "ShowNotification", text).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, "Ошибка при отправке команды ShowNotification");
                        MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                },
                _ => SelectedInstance?.IsOnline == true);

            ForceCloseSelectedCommand = LambdaCommand.Create(
                async _ =>
                {
                    try
                    {
                        var targets = Instances.Where(i => i.IsOnline && i.IsSelected).ToList();
                        if (targets.Count == 0)
                        {
                            MessageBox.Show("Нет выбранных онлайн-экземпляров.", "Информация",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        var result = MessageBox.Show(
                            $"Принудительно закрыть {targets.Count} выбранный(х) экземпляр(ов)?",
                            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (result != MessageBoxResult.Yes) return;

                        InProgress = true;
                        await Database.SendAppCommandToAllAsync(
                            targets, "ForceClose", null).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, "Ошибка при отправке команды ForceClose выбранным");
                        MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        await App.Current.Dispatcher.InvokeAsync(() => InProgress = false);
                    }
                },
                _ => Instances.Any(i => i.IsOnline && i.IsSelected));

            NotifySelectedCommand = LambdaCommand.Create(
                async _ =>
                {
                    try
                    {
                        var targets = Instances.Where(i => i.IsOnline && i.IsSelected).ToList();
                        if (targets.Count == 0)
                        {
                            MessageBox.Show("Нет выбранных онлайн-экземпляров.", "Информация",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        var dialog = new UserInputDialogWindow(
                            "Уведомление", "Введите текст уведомления:");
                        dialog.Owner = Application.Current.MainWindow;
                        if (dialog.ShowDialog() != true) return;

                        var text = dialog.UserInput;
                        if (string.IsNullOrWhiteSpace(text)) return;

                        InProgress = true;
                        await Database.SendAppCommandToAllAsync(
                            targets, "ShowNotification", text).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, "Ошибка при отправке команды ShowNotification выбранным");
                        MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        await App.Current.Dispatcher.InvokeAsync(() => InProgress = false);
                    }
                },
                _ => Instances.Any(i => i.IsOnline && i.IsSelected));

            ToggleAllCommand = LambdaCommand.Create(_ =>
            {
                var newState = !AllSelected;
                foreach (var instance in Instances)
                {
                    if (instance.IsOnline)
                        instance.IsSelected = newState;
                }
                AllSelected = newState;
            });
        }

        private async Task PollLoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                try
                {
                    var items = await Database.ReadActiveInstancesAsync();
                    var pendingCount = await Database.GetPendingCommandCountAsync();

                    await App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var selected = SelectedInstance;
                        var selectedIds = new System.Collections.Generic.HashSet<Guid>(
                            Instances.Where(i => i.IsSelected).Select(i => i.SessionId));

                        Instances.Clear();

                        foreach (var item in items)
                        {
                            if (selectedIds.Contains(item.SessionId))
                                item.IsSelected = true;
                            Instances.Add(item);
                        }

                        if (selected is not null)
                        {
                            SelectedInstance = Instances.FirstOrDefault(
                                i => i.SessionId == selected.SessionId);
                        }

                        PendingCommandCount = pendingCount;
                        AllSelected = Instances.Count > 0
                            && Instances.Where(i => i.IsOnline).All(i => i.IsSelected);
                        CommandManager.InvalidateRequerySuggested();
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Util.WriteLog(ex);
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
        }
    }
}
