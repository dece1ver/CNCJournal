using libeLog.Base;
using remeLog.Core;
using remeLog.Infrastructure;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace remeLog.ViewModels
{
    /// <summary>
    /// Окно экземпляров: по вкладке на приложение (remeLog, eLog). Общий опрос БД один на
    /// оба списка — присутствие и очередь команд лежат в одних таблицах, и дробить это на
    /// два независимых таймера смысла нет.
    /// </summary>
    public class ActiveInstancesViewModel : ViewModel, IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public InstancesTabViewModel RemeLogTab { get; } = new(AppNames.RemeLog, "remeLog");
        public InstancesTabViewModel ELogTab { get; } = new(AppNames.ELog, "eLog");

        private IEnumerable<InstancesTabViewModel> Tabs
        {
            get
            {
                yield return RemeLogTab;
                yield return ELogTab;
            }
        }

        public ActiveInstancesViewModel()
        {
            Task.Run(PollLoopAsync);
        }

        private async Task PollLoopAsync()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            async Task PollAsync()
            {
                var items = await Database.ReadActiveInstancesAsync();

                var byApplication = new Dictionary<string, (List<AppPresence> Items, int Pending)>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var tab in Tabs)
                {
                    var tabItems = items
                        .Where(i => string.Equals(i.Application, tab.Application, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(i => i.IsOnline)
                        .ThenBy(i => i.UserName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var pending = await Database.GetPendingCommandCountAsync(tab.Application);
                    byApplication[tab.Application] = (tabItems, pending);
                }

                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var tab in Tabs)
                    {
                        var (tabItems, pending) = byApplication[tab.Application];
                        tab.ApplyInstances(tabItems, pending);
                    }
                });

                foreach (var tab in Tabs)
                    await tab.PollCommandResultsAsync().ConfigureAwait(false);
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
                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var tab in Tabs)
                        tab.InProgress = false;
                });
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

        public void Dispose()
        {
            _cts.Cancel();
        }
    }
}
