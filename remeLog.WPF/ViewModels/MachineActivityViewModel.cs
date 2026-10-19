using libeLog.Base;
using remeLog.Core.Services;
using remeLog.Infrastructure;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace remeLog.ViewModels
{
    /// <summary>Строка живой панели: станок + его текущий heartbeat (может отсутствовать).</summary>
    public record MachineActivityRow(string Machine, MachineActivity? Activity);

    /// <summary>
    /// Read-only монитор "что сейчас на станках" — по heartbeat, который тихо пишет eLog
    /// (см. <see cref="MainDashboardService.LoadMachineActivityAsync"/>). В отличие от
    /// <see cref="ActiveInstancesViewModel"/> не отправляет команд, только опрашивает.
    /// </summary>
    public class MachineActivityViewModel : ViewModel, IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private List<string> _machines = new();

        private bool _inProgress = true;
        public bool InProgress
        {
            get => _inProgress;
            set => Set(ref _inProgress, value);
        }

        public ObservableCollection<MachineActivityRow> Rows { get; } = new();

        public MachineActivityViewModel()
        {
            Task.Run(PollLoopAsync);
        }

        private async Task PollLoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

            async Task PollAsync()
            {
                if (_machines.Count == 0)
                {
                    var reference = await MainDashboardService.LoadReferenceDataAsync(CancellationToken.None);
                    _machines = reference.Machines.Value ?? new List<string>();
                }

                var activity = await MainDashboardService.LoadMachineActivityAsync(_machines, CancellationToken.None);

                var sorted = _machines
                    .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                    .Select(m => new MachineActivityRow(m, activity.GetValueOrDefault(m)))
                    .ToList();

                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    Rows.Clear();
                    foreach (var row in sorted) Rows.Add(row);
                    InProgress = false;
                });
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

        public void Dispose() => _cts.Cancel();
    }
}
