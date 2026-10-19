using libeLog;
using libeLog.Base;
using libeLog.Views;
using remeLog.Infrastructure;
using remeLog.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace remeLog.ViewModels
{
    public class PendingCommandsViewModel : ViewModel, IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        /// <summary>Приложение, чью очередь показываем (remeLog.Core.AppNames); null — все.</summary>
        private readonly string? _application;

        public ObservableCollection<CommandEntry> Commands { get; } = new();

        private bool _inProgress;
        public bool InProgress
        {
            get => _inProgress;
            set => Set(ref _inProgress, value);
        }

        public bool IsEmpty => Commands.Count == 0;

        public LambdaCommand CancelCommand { get; }
        public LambdaCommand CloseCommand { get; }

        public PendingCommandsViewModel(string? application = null)
        {
            _application = application;

            Task.Run(PollLoopAsync);

            CancelCommand = LambdaCommand.Create(
                async parameter =>
                {
                    if (parameter is not Guid id) return;

                    try
                    {
                        InProgress = true;
                        var wasCancelled = await Database.CancelPendingCommandAsync(id).ConfigureAwait(false);

                        if (!wasCancelled)
                        {
                            await App.Current.Dispatcher.InvokeAsync(() =>
                                MessageBoxWindow.Show(
                                    "Команда уже выполняется или отменена.",
                                    "Информация",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information));
                            return;
                        }

                        await App.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var item = Commands.FirstOrDefault(c => c.Id == id);
                            if (item is not null)
                                Commands.Remove(item);
                            OnPropertyChanged(nameof(IsEmpty));
                        });
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, "Ошибка при отмене команды");
                        MessageBoxWindow.Show($"Ошибка: {ex.Message}", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        await App.Current.Dispatcher.InvokeAsync(() => InProgress = false);
                    }
                },
                _ => true);

            CloseCommand = LambdaCommand.Create(_ =>
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is Views.PendingCommandsWindow)
                    {
                        window.Close();
                        break;
                    }
                }
            });
        }

        private async Task PollLoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                try
                {
                    InProgress = true;

                    var items = await Database.GetPendingCommandsAsync(_application).ConfigureAwait(false);

                    var entries = items.Select(i => new CommandEntry
                    {
                        Id = i.Id,
                        CommandType = i.CommandType,
                        TargetApplication = i.TargetApplication,
                        TargetMachine = i.TargetMachine,
                        TargetUser = i.TargetUser,
                        Payload = i.Payload,
                        CreatedUtc = i.CreatedUtc,
                    }).ToList();

                    await App.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Commands.Clear();
                        foreach (var entry in entries)
                            Commands.Add(entry);

                        OnPropertyChanged(nameof(IsEmpty));
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Util.WriteLog(ex, "Ошибка обновления очереди команд");
                }
                finally
                {
                    await App.Current.Dispatcher.InvokeAsync(() => InProgress = false);
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
        }
    }
}
