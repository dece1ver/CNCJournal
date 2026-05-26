using remeLog.Infrastructure;
using remeLog.Models;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace remeLog.ViewModels
{
    public class ActiveInstancesViewModel : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public ObservableCollection<AppPresence> Instances { get; } = new();

        public ActiveInstancesViewModel()
        {
            Task.Run(PollLoopAsync);
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
                        Instances.Clear();

                        foreach (var item in items)
                            Instances.Add(item);
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
