using libeLog;
using libeLog.Base;
using remeLog.Infrastructure;
using remeLog.Models;
using remeLog.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using libeLog.Views;

namespace remeLog.ViewModels
{
    public class ActiveInstancesViewModel : ViewModel, IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<Guid, string> _pendingResults = new();
        private const string UpdateNotificationMessage = "Для обновления перезапустите приложение.\nЗакрыть сейчас?";

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

        private bool _inProgress = true;
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

        private string _lastCommandResult = string.Empty;
        public string LastCommandResult
        {
            get => _lastCommandResult;
            set => Set(ref _lastCommandResult, value);
        }

        public ObservableCollection<AppPresence> Instances { get; } = new();

        public LambdaCommand ForceCloseCommand { get; }
        public LambdaCommand ShowNotificationCommand { get; }
        public LambdaCommand ForceCloseSelectedCommand { get; }
        public LambdaCommand NotifySelectedCommand { get; }
        public LambdaCommand NotifyUpdateCommand { get; }
        public LambdaCommand NotifyUpdateSelectedCommand { get; }
        public LambdaCommand ToggleAllCommand { get; }
        public LambdaCommand ShowPendingCommandsCommand { get; }

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

                        var result = MessageBoxWindow.Show(
                            $"Принудительно закрыть экземпляр на {target.MachineName}\\{target.UserName}?",
                            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxDefaultButton.No);
                        if (result != MessageBoxResult.Yes) return;

                        await Database.SendAppCommandAsync(
                            target.SessionId, target.MachineName, target.UserName,
                            "ForceClose", null).ConfigureAwait(false);
                        await RefreshPendingCountAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, "Ошибка при отправке команды ForceClose");
                        MessageBoxWindow.Show($"Ошибка: {ex.Message}", "Ошибка",
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

                        var commandId = await Database.SendAppCommandAsync(
                            target.SessionId, target.MachineName, target.UserName,
                            "ShowNotification", text).ConfigureAwait(false);

                        lock (_pendingResults)
                        {
                            _pendingResults[commandId] = $"{target.MachineName}\\{target.UserName}";
                        }
                        LastCommandResult = $"Ожидание ответа от {target.MachineName}...";
                        await RefreshPendingCountAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, "Ошибка при отправке команды ShowNotification");
                        MessageBoxWindow.Show($"Ошибка: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                },
                _ => SelectedInstance?.IsOnline == true);

            NotifyUpdateCommand = LambdaCommand.Create(
                async _ =>
                {
                    var target = SelectedInstance;
                    if (target is null || !target.IsOnline) return;

                    var commandId = await Database.SendAppCommandAsync(
                        target.SessionId, target.MachineName, target.UserName,
                        "UpdateNotification", UpdateNotificationMessage).ConfigureAwait(false);

                    lock (_pendingResults)
                        _pendingResults[commandId] = $"{target.MachineName}\\{target.UserName}";

                    LastCommandResult = $"Ожидание ответа от {target.MachineName}...";
                    await RefreshPendingCountAsync().ConfigureAwait(false);
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
                            MessageBoxWindow.Show("Нет выбранных онлайн-экземпляров.", "Информация",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        var result = MessageBoxWindow.Show(
                            $"Принудительно закрыть {targets.Count} выбранный(х) экземпляр(ов)?",
                            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxDefaultButton.No);
                        if (result != MessageBoxResult.Yes) return;

                        InProgress = true;
                        await Database.SendAppCommandToAllAsync(
                            targets, "ForceClose", null).ConfigureAwait(false);
                        await RefreshPendingCountAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, "Ошибка при отправке команды ForceClose выбранным");
                        MessageBoxWindow.Show($"Ошибка: {ex.Message}", "Ошибка",
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
                            MessageBoxWindow.Show("Нет выбранных онлайн-экземпляров.", "Информация",
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
                        var ids = await Database.SendAppCommandToAllAsync(
                            targets, "ShowNotification", text).ConfigureAwait(false);

                        lock (_pendingResults)
                        {
                            for (var i = 0; i < ids.Count; i++)
                            {
                                _pendingResults[ids[i]] = $"{targets[i].MachineName}\\{targets[i].UserName}";
                            }
                        }
                        LastCommandResult = $"Ожидание ответа от {targets.Count} экземпляров...";
                        await RefreshPendingCountAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, "Ошибка при отправке команды ShowNotification выбранным");
                        MessageBoxWindow.Show($"Ошибка: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        await App.Current.Dispatcher.InvokeAsync(() => InProgress = false);
                    }
                },
                _ => Instances.Any(i => i.IsOnline && i.IsSelected));

            NotifyUpdateSelectedCommand = LambdaCommand.Create(
                async _ =>
                {
                    try
                    {
                        var targets = Instances.Where(i => i.IsOnline && i.IsSelected).ToList();
                        if (targets.Count == 0)
                        {
                            MessageBoxWindow.Show("Нет выбранных онлайн-экземпляров.", "Информация",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        InProgress = true;
                        var ids = await Database.SendAppCommandToAllAsync(
                            targets, "UpdateNotification", UpdateNotificationMessage).ConfigureAwait(false);

                        lock (_pendingResults)
                        {
                            for (var i = 0; i < ids.Count; i++)
                                _pendingResults[ids[i]] = $"{targets[i].MachineName}\\{targets[i].UserName}";
                        }

                        LastCommandResult = $"Ожидание ответа от {targets.Count} экземпляров...";
                        await RefreshPendingCountAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, "Ошибка при отправке уведомления об обновлении");
                        MessageBoxWindow.Show($"Ошибка: {ex.Message}", "Ошибка",
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

            ShowPendingCommandsCommand = LambdaCommand.Create(
                async _ =>
                {
                    await App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var window = new Views.PendingCommandsWindow();
                        window.Owner = Application.Current.MainWindow;
                        window.ShowDialog();
                    });
                });
        }

        private async Task RefreshPendingCountAsync()
        {
            var count = await Database.GetPendingCommandCountAsync().ConfigureAwait(false);
            await App.Current.Dispatcher.InvokeAsync(() => PendingCommandCount = count);
        }

        private async Task PollLoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            async Task PollAsync()
            {
                var items = await Database.ReadActiveInstancesAsync();
                var pendingCount = await Database.GetPendingCommandCountAsync();

                var sorted = items
                    .OrderByDescending(i => i.IsOnline)
                    .ThenBy(i => i.UserName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    var selected = SelectedInstance;
                    var selectedIds = new HashSet<Guid>(
                        Instances.Where(i => i.IsSelected).Select(i => i.SessionId));

                    Instances.Clear();

                    foreach (var item in sorted)
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

                await PollCommandResultsAsync().ConfigureAwait(false);
            }

            try
            {
                await PollAsync();
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
            }
            finally
            {
                await App.Current.Dispatcher.InvokeAsync(() => InProgress = false);
            }

            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                try
                {
                    await PollAsync();
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

        private async Task PollCommandResultsAsync()
        {
            List<Guid> completedIds;

            lock (_pendingResults)
            {
                if (_pendingResults.Count == 0) return;
                completedIds = new List<Guid>();
            }

            var resultMessages = new List<string>();

            foreach (var (id, target) in _pendingResults.ToList())
            {
                var result = await Database.GetCommandResultAsync(id).ConfigureAwait(false);
                if (result is null) continue;

                completedIds.Add(id);
                resultMessages.Add($"{target}: {result}");
            }

            if (completedIds.Count == 0) return;

            lock (_pendingResults)
            {
                foreach (var id in completedIds)
                {
                    _pendingResults.Remove(id);
                }
            }

            var summary = string.Join("\n", resultMessages);
            await App.Current.Dispatcher.InvokeAsync(() => LastCommandResult = summary);
        }

        public void Dispose()
        {
            _cts.Cancel();
        }
    }
}
