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

        public ObservableCollection<AppPresence> Instances { get; } = new();

        public LambdaCommand ForceCloseCommand { get; }
        public LambdaCommand ShowNotificationCommand { get; }

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
        }

        private async Task PollLoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                try
                {
                    var items = await Database.ReadActiveInstancesAsync();

                    await App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var selected = SelectedInstance;

                        Instances.Clear();

                        foreach (var item in items)
                            Instances.Add(item);

                        if (selected is not null)
                        {
                            SelectedInstance = Instances.FirstOrDefault(
                                i => i.SessionId == selected.SessionId);
                        }

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
