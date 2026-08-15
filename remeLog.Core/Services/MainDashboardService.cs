using remeLog.Core.Db;
using remeLog.Infrastructure;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace remeLog.Core.Services
{
    /// <summary>
    /// Бизнес-логика сводки по деталям/сменам за период (то, что показывает MainWindow) —
    /// вынесена из MainWindowViewModel.LoadPartsAsync, чтобы быть переиспользуемой из
    /// remeLog.Web. ViewModel остаётся тонкой обвязкой: debounce/semaphore/Dispatcher/
    /// MessageBox — её забота, сама выборка и расчёт состояния смен — здесь.
    /// </summary>
    public static class MainDashboardService
    {
        /// <summary>Результат параллельной загрузки справочников.</summary>
        public record ReferenceData(
            DbResult<List<string>> Machines,
            DbResult<List<string>> DowntimeReasons,
            DbResult<List<(string Reason, bool RequireComment)>> SetupReasons,
            DbResult<List<(string Reason, bool RequireComment)>> MachiningReasons);

        /// <summary>Справочники станков/причин простоев/отклонений — четыре запроса параллельно.</summary>
        public static async Task<ReferenceData> LoadReferenceDataAsync(CancellationToken cancellationToken)
        {
            var machinesTask = Task.Run(Database.ReadMachines, cancellationToken);
            var downtimeTask = Task.Run(Database.ReadDowntimeReasons, cancellationToken);
            var setupTask = Task.Run(() => Database.ReadDeviationReasons(DeviationReasonType.Setup), cancellationToken);
            var machiningTask = Task.Run(() => Database.ReadDeviationReasons(DeviationReasonType.Machining), cancellationToken);

            await Task.WhenAll(machinesTask, downtimeTask, setupTask, machiningTask);

            return new ReferenceData(machinesTask.Result, downtimeTask.Result, setupTask.Result, machiningTask.Result);
        }

        /// <summary>Наличие и статус проверки отчёта по станку за период.</summary>
        public record MachineReportState(string Machine, CombinedParts.ReportState State, bool IsChecked);

        /// <summary>Результат <see cref="LoadShiftOverviewAsync"/>.</summary>
        public record ShiftOverview(List<MachineReportState> ReportStates, List<ShiftInfo> TotalShifts);

        /// <summary>
        /// Для каждого станка — есть ли отчёт за период (только для однодневного периода,
        /// иначе всегда NotExist — отчёт по смене за произвольный период не определён)
        /// и проверен ли он, плюс общий список смен за период.
        /// </summary>
        public static async Task<ShiftOverview> LoadShiftOverviewAsync(
            List<string> machines, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
        {
            var reportStatesTask = Task.Run(() => ComputeReportStates(machines, fromDate, toDate), cancellationToken);
            var totalShiftsTask = Task.Run(
                () => Database.GetShiftsByPeriod(machines, fromDate, toDate, new Shift(ShiftType.All)),
                cancellationToken);

            await Task.WhenAll(reportStatesTask, totalShiftsTask);

            return new ShiftOverview(reportStatesTask.Result, totalShiftsTask.Result.Value ?? new List<ShiftInfo>());
        }

        /// <summary>Отработал ли станок дневную/ночную смену указанных суток (есть ли детали).</summary>
        public record MachineWorkStatus(string Machine, bool WorkedDay, bool WorkedNight);

        /// <summary>
        /// Фактическая работа станка за одни сутки — по наличию деталей на день/ночь
        /// (та же логика, что <see cref="CombinedParts.ShiftsInfo"/> в MainWindow), а не по
        /// тому, подан ли отчёт мастером (см. <see cref="LoadShiftOverviewAsync"/>).
        /// </summary>
        public static async Task<List<MachineWorkStatus>> LoadMachineWorkStatusAsync(
            List<string> machines, DateTime date, CancellationToken cancellationToken)
        {
            var tasks = machines.Select(async machine =>
            {
                var parts = await Database.ReadPartsByShiftDateAndMachine(date, date, machine, cancellationToken);
                var workedDay = parts.Any(p => p.ShiftDate == date && p.Shift == Shifts.Day);
                var workedNight = parts.Any(p => p.ShiftDate == date && p.Shift == Shifts.Night);
                return new MachineWorkStatus(machine, workedDay, workedNight);
            });

            return (await Task.WhenAll(tasks)).ToList();
        }

        /// <summary>
        /// Текущий статус станков (наладка/изготовление/простой) по heartbeat из eLog.
        /// Станки без записи в cnc_machine_activity получают null — вызывающая сторона
        /// должна показать это как "нет данных" (как и запись с истёкшим <see cref="MachineActivity.IsStale"/>).
        /// </summary>
        public static async Task<Dictionary<string, MachineActivity?>> LoadMachineActivityAsync(
            List<string> machines, CancellationToken cancellationToken)
        {
            var activity = await Database.ReadMachineActivityAsync();
            var byMachine = activity.ToDictionary(a => a.Machine);
            return machines.ToDictionary(m => m, m => byMachine.GetValueOrDefault(m));
        }

        private static List<MachineReportState> ComputeReportStates(List<string> machines, DateTime fromDate, DateTime toDate)
        {
            var list = new List<MachineReportState>();

            foreach (var machine in machines)
            {
                var state = CombinedParts.ReportState.NotExist;
                bool dayChecked = false;
                bool nightChecked = false;

                if (fromDate == toDate)
                {
                    bool dayExist = false;
                    bool nightExist = false;

                    if (Database.ReadShiftInfo(new ShiftInfo(toDate, ShiftType.Day, machine)) is { IsOk: true, Value: var dayShifts }
                        && dayShifts is { Count: > 0 } && dayShifts[0].Master != "")
                    {
                        dayExist = true;
                        dayChecked = dayShifts.Any(s => s.IsChecked);
                    }

                    if (Database.ReadShiftInfo(new ShiftInfo(toDate, ShiftType.Night, machine)) is { IsOk: true, Value: var nightShifts }
                        && nightShifts is { Count: > 0 } && nightShifts[0].Master != "")
                    {
                        nightExist = true;
                        nightChecked = nightShifts.Any(s => s.IsChecked);
                    }

                    state = (dayExist, nightExist) switch
                    {
                        (true, true) => CombinedParts.ReportState.Exist,
                        (true, false) => CombinedParts.ReportState.Partial,
                        (false, true) => CombinedParts.ReportState.Partial,
                        _ => CombinedParts.ReportState.NotExist,
                    };
                }

                list.Add(new MachineReportState(machine, state, dayChecked && nightChecked));
            }

            return list;
        }
    }
}
