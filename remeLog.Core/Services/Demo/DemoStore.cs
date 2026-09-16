using remeLog.Core.Db;
using remeLog.Core.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace remeLog.Core.Services.Demo
{
    /// <summary>
    /// In-memory хранилище демо-режима. Заполняется <see cref="DemoDataFactory"/>
    /// при первом обращении, живёт до перезапуска. Мутации (сохранение деталей,
    /// смен, операторов) меняют только память — SQL Server не требуется.
    /// Потокобезопасность: все операции чтения/записи под lock.
    /// </summary>
    public static class DemoStore
    {
        private static readonly object _gate = new();
        private static DemoDataFactory.DemoSnapshot? _snapshot;
        private static int _nextShiftId = 100000;

        /// <summary>Версия схемы, которую «имеет» демо-БД (см. AppSettings.RequiredSchemaVersion = 2).</summary>
        public const int SchemaVersion = 2;

        public static bool IsInitialized
        {
            get { lock (_gate) return _snapshot is not null; }
        }

        public static void EnsureInitialized(DateTime? today = null, int? seed = null)
        {
            lock (_gate)
            {
                if (_snapshot is not null) return;
                var snapshot = DemoDataFactory.Build(
                    today ?? DateTime.Today, seed ?? DomainSettings.DemoSeed);
                _snapshot = snapshot;
                _nextShiftId = snapshot.Shifts.Any()
                    ? snapshot.Shifts.Max(s => s.Id ?? 0) + 1
                    : 100000;
                ApplyConfigToDomainSettings(snapshot);
            }
        }

        /// <summary>Сброс для тестов.</summary>
        public static void Reset()
        {
            lock (_gate) _snapshot = null;
        }

        private static DemoDataFactory.DemoSnapshot Snapshot
        {
            get
            {
                lock (_gate)
                {
                    if (_snapshot is null)
                    {
                        var snapshot = DemoDataFactory.Build(DateTime.Today, DomainSettings.DemoSeed);
                        _snapshot = snapshot;
                        _nextShiftId = snapshot.Shifts.Any()
                            ? snapshot.Shifts.Max(s => s.Id ?? 0) + 1
                            : 100000;
                        ApplyConfigToDomainSettings(snapshot);
                    }
                    return _snapshot;
                }
            }
        }

        private static void ApplyConfigToDomainSettings(DemoDataFactory.DemoSnapshot snapshot)
        {
            DomainSettings.SerialParts = new HashSet<string>(snapshot.SerialPartNames);
            DomainSettings.SetupReasons = snapshot.SetupReasons.ToList();
            DomainSettings.MachiningReasons = snapshot.MachiningReasons.ToList();
            DomainSettings.MaxSetupLimit = 2;
            DomainSettings.MaxSetupLimits = new Dictionary<string, double>(snapshot.MaxSetupLimits);
            DomainSettings.FallbackMaxSetupLimitValue = 1.5;
            DomainSettings.Holidays = snapshot.Holidays.ToArray();
            DomainSettings.LongSetupLimit = 240;
            DomainSettings.Administrators = new[] { Environment.UserName, "demo-admin" };
            DomainSettings.CncOperations = new[] { "Токарная", "Фрезерная", "Сверлильная" };
            DomainSettings.EngineerComments = new[] { "Принято", "Доработать режимы", "Проверить припуск" };
            DomainSettings.SchemaVersion = SchemaVersion;
            DomainSettings.EnabledFeatures =
                RemeLogFeature.AdvancedEdit | RemeLogFeature.ValidationOverride | RemeLogFeature.ReasonOverride;
        }

        // ── Справочники ──────────────────────────────────────────────

        public static List<string> GetMachines() =>
            Snapshot.Machines.Select(m => m.Machine).OrderBy(n => n).ToList();

        public static List<MachineFilter> GetMachineFilters() =>
            Snapshot.Machines
                .OrderBy(m => m.Machine)
                .Select(m => new MachineFilter(m.Machine, m.Type, false))
                .ToList();

        public static List<OperatorInfo> GetOperators()
        {
            lock (_gate)
            {
                return Snapshot.Operators
                    .Select(o => new OperatorInfo(o.Id, o.FirstName, o.LastName, o.Patronymic, o.Qualification, o.IsActive))
                    .ToList();
            }
        }

        public static List<string> GetDowntimeReasons() =>
            Snapshot.DowntimeReasons.ToList();

        public static List<(string Reason, bool RequireComment)> GetDeviationReasons(DeviationReasonType type) =>
            (type == DeviationReasonType.Setup ? Snapshot.SetupReasons : Snapshot.MachiningReasons).ToList();

        public static HashSet<string> GetSerialPartNames() =>
            new(Snapshot.SerialPartNames);

        public static List<MachineActivity> GetMachineActivity()
        {
            lock (_gate) return Snapshot.MachineActivity.ToList();
        }

        // ── Детали ───────────────────────────────────────────────────

        public static List<Part> GetParts(DateTime fromDate, DateTime toDate, string? machine = null)
        {
            lock (_gate)
            {
                return Snapshot.Parts
                    .Where(p => p.ShiftDate.Date >= fromDate.Date && p.ShiftDate.Date <= toDate.Date)
                    .Where(p => machine == null || p.Machine == machine)
                    .OrderBy(p => p.StartSetupTime)
                    .Select(p => new Part(p))
                    .ToList();
            }
        }

        public static List<Part> GetPartsByGuids(IEnumerable<Guid> guids)
        {
            var set = new HashSet<Guid>(guids);
            lock (_gate)
            {
                return Snapshot.Parts.Where(p => set.Contains(p.Guid)).Select(p => new Part(p)).ToList();
            }
        }

        /// <summary>
        /// LINQ-эквивалент SQL-фильтров <see cref="PartsFilterService.BuildConditions"/>:
        /// та же семантика дат, смен, масок (*…*), мультизначений через «;»,
        /// серийности и станков. SQL-чипы (Text/Number/Bool) — по имени колонки
        /// через рефлексию свойств <see cref="Part"/>; неизвестные — пропускаются.
        /// </summary>
        public static List<Part> QueryParts(PartsFilterCriteria criteria)
        {
            List<Part> source;
            lock (_gate) source = Snapshot.Parts.Select(p => new Part(p)).ToList();

            var serialNames = criteria.SerialPartNormalizedNames;
            var result = source.Where(p =>
                p.ShiftDate.Date >= criteria.FromDate.Date && p.ShiftDate.Date <= criteria.ToDate.Date
                && (criteria.ShiftFilter.Type == ShiftType.All || p.Shift == criteria.ShiftFilter.FilterText)
                && MatchMultiOrPattern(p.Operator, criteria.OperatorFilter)
                && MatchPattern(p.PartName, criteria.PartNameFilter)
                && MatchMultiOrPattern(p.Order, criteria.OrderFilter)
                && MatchPattern(p.EngineerConclusion, criteria.EngineerConclusionFilter)
                && MatchPattern(p.EngineerComment, criteria.EngineerCommentFilter)
                && MatchComparison((int)Math.Round(p.FinishedCount), criteria.FinishedCountFilter)
                && MatchComparison(p.TotalCount, criteria.TotalCountFilter)
                && (criteria.SetupFilter == null || p.Setup == criteria.SetupFilter.Value)
                && MatchSerial(p, criteria.SerialPartsFilter, serialNames)
                && criteria.SelectedMachines.Contains(p.Machine)
                && MatchChips(p, criteria.ChipFilters)
            ).ToList();

            // InMemory-чипы — тем же сервисом, что и для БД.
            return PartsFilterService.ApplyInMemoryFilters(result, criteria.ChipFilters);
        }

        public static DbResult<string> UpsertPart(Part part)
        {
            lock (_gate)
            {
                var existing = Snapshot.Parts.FirstOrDefault(p => p.Guid == part.Guid);
                if (existing is null) Snapshot.Parts.Add(new Part(part));
                else
                {
                    int idx = Snapshot.Parts.IndexOf(existing);
                    Snapshot.Parts[idx] = new Part(part);
                }
                return DbResult<string>.Ok("OK");
            }
        }

        public static DbResult<bool> DeletePart(Guid guid)
        {
            lock (_gate)
            {
                int removed = Snapshot.Parts.RemoveAll(p => p.Guid == guid);
                return DbResult<bool>.Ok(removed > 0);
            }
        }

        // ── Смены ────────────────────────────────────────────────────

        public static List<ShiftInfo> GetShifts(ICollection<string> machines, DateTime fromDate, DateTime toDate, Shift shift)
        {
            lock (_gate)
            {
                return Snapshot.Shifts
                    .Where(s => machines.Contains(s.Machine))
                    .Where(s => s.ShiftDate.Date >= fromDate.Date && s.ShiftDate.Date <= toDate.Date)
                    .Where(s => shift.Type == ShiftType.All || s.Shift == shift.Name)
                    .Select(CloneShift)
                    .ToList();
            }
        }

        public static List<ShiftInfo> FindShift(ShiftInfo key)
        {
            lock (_gate)
            {
                return Snapshot.Shifts
                    .Where(s => s.ShiftDate.Date == key.ShiftDate.Date && s.Shift == key.Shift && s.Machine == key.Machine)
                    .Select(CloneShift)
                    .ToList();
            }
        }

        public static DbResult<bool> WriteShift(ShiftInfo shift)
        {
            lock (_gate)
            {
                var existing = Snapshot.Shifts.FirstOrDefault(s =>
                    s.ShiftDate.Date == shift.ShiftDate.Date && s.Shift == shift.Shift && s.Machine == shift.Machine);
                if (existing is not null) return UpdateShift(shift);
                shift.Id ??= _nextShiftId++;
                Snapshot.Shifts.Add(CloneShift(shift));
                return DbResult<bool>.Ok(true);
            }
        }

        public static DbResult<bool> UpdateShift(ShiftInfo shift)
        {
            lock (_gate)
            {
                var existing = Snapshot.Shifts.FirstOrDefault(s =>
                    s.ShiftDate.Date == shift.ShiftDate.Date && s.Shift == shift.Shift && s.Machine == shift.Machine);
                if (existing is null) return WriteShift(shift);
                existing.Master = shift.Master;
                existing.UnspecifiedDowntimes = shift.UnspecifiedDowntimes;
                existing.DowntimesComment = shift.DowntimesComment;
                existing.CommonComment = shift.CommonComment;
                existing.IsChecked = shift.IsChecked;
                return DbResult<bool>.Ok(true);
            }
        }

        private static ShiftInfo CloneShift(ShiftInfo s) => new(
            s.Id, s.ShiftDate, s.Shift, s.Machine, s.Master,
            s.UnspecifiedDowntimes, s.DowntimesComment, s.CommonComment, s.IsChecked,
            s.GiverWorkplaceCleaned, s.GiverFailures, s.GiverExtraneousNoises,
            s.GiverLiquidLeaks, s.GiverToolBreakage, s.GiverСoolantСoncentration,
            s.RecieverWorkplaceCleaned, s.RecieverFailures, s.RecieverExtraneousNoises,
            s.RecieverLiquidLeaks, s.RecieverToolBreakage, s.RecieverСoolantСoncentration);

        // ── Операторы ────────────────────────────────────────────────

        public static void SaveOperator(OperatorInfo op)
        {
            lock (_gate)
            {
                var existing = Snapshot.Operators.FirstOrDefault(o => o.Id == op.Id);
                if (existing is null)
                {
                    int id = Snapshot.Operators.Any() ? Snapshot.Operators.Max(o => o.Id) + 1 : 1;
                    Snapshot.Operators.Add(new OperatorInfo(id, op.FirstName, op.LastName, op.Patronymic, op.Qualification, op.IsActive));
                }
                else
                {
                    existing.FirstName = op.FirstName;
                    existing.LastName = op.LastName;
                    existing.Patronymic = op.Patronymic;
                    existing.Qualification = op.Qualification;
                    existing.IsActive = op.IsActive;
                }
            }
        }

        public static void DeleteOperator(int id)
        {
            lock (_gate) Snapshot.Operators.RemoveAll(o => o.Id == id);
        }

        // ── Матчинг фильтров (семантика SearchPattern) ───────────────

        private static bool MatchPattern(string value, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            value ??= "";
            if (filter.StartsWith('='))
                return string.Equals(value, filter[1..], StringComparison.OrdinalIgnoreCase);
            if (filter.StartsWith('*') && filter.EndsWith('*') && filter.Length >= 2)
                return value.Contains(filter[1..^1], StringComparison.OrdinalIgnoreCase);
            if (filter.StartsWith('*'))
                return value.EndsWith(filter[1..], StringComparison.OrdinalIgnoreCase);
            if (filter.EndsWith('*'))
                return value.StartsWith(filter[..^1], StringComparison.OrdinalIgnoreCase);
            return value.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchMultiOrPattern(string value, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            if (!filter.Contains(';')) return MatchPattern(value, filter.Trim());
            return filter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(v => MatchPattern(value, v));
        }

        private static bool MatchComparison(int actual, (string Op, int Value)? filter)
        {
            if (filter is not { } f) return true;
            return f.Op switch
            {
                "=" or "≡" => actual == f.Value,
                ">" => actual > f.Value,
                "" or ">=" or "≥" => actual >= f.Value,
                "<" => actual < f.Value,
                "<=" or "≤" => actual <= f.Value,
                "!=" or "≠" => actual != f.Value,
                _ => true,
            };
        }

        private static bool MatchSerial(Part p, PartsFilterType filter, IReadOnlyCollection<string> serialNames)
        {
            if (filter == PartsFilterType.All) return true;
            bool isSerial = serialNames.Contains(p.PartName.NormalizedPartNameWithoutComments());
            return filter == PartsFilterType.Serial ? isSerial : !isSerial;
        }

        private static bool MatchChips(Part p, IReadOnlyCollection<FilterChip> chips)
        {
            foreach (var chip in chips.Where(c => !c.IsInMemory))
            {
                var prop = typeof(Part).GetProperties()
                    .FirstOrDefault(pr => string.Equals(pr.Name, chip.SqlColumn, StringComparison.OrdinalIgnoreCase));
                if (prop is null) continue;
                var raw = prop.GetValue(p);
                var values = chip.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (values.Length == 0) continue;
                bool any = values.Any(v => ChipValueEquals(raw, v));
                if (!any) return false;
            }
            return true;
        }

        private static bool ChipValueEquals(object? raw, string filter)
        {
            var text = filter.TrimEnd('%', ' ').Trim();
            return raw switch
            {
                null => false,
                bool b => text is "1" or "True" or "true" or "✓" ? b : !b,
                double d => double.TryParse(text, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double f) && d == f,
                int i => int.TryParse(text, out int f) && i == f,
                DateTime dt => dt.ToString("dd.MM.yyyy").Contains(text, StringComparison.OrdinalIgnoreCase),
                TimeSpan ts => ts.ToString().Contains(text, StringComparison.OrdinalIgnoreCase),
                _ => string.Equals(raw.ToString(), text, StringComparison.OrdinalIgnoreCase),
            };
        }

        /// <summary>Сериализованные серийные детали для окон libeLog (демо: имена + нормативы-заглушки).</summary>
        public static List<(int Id, string PartName)> GetSerialParts() =>
            Snapshot.SerialPartNames
                .OrderBy(n => n)
                .Select((n, i) => (i + 1, Snapshot.Parts.FirstOrDefault(p =>
                    p.PartName.NormalizedPartNameWithoutComments() == n)?.PartName ?? n))
                .ToList();

        public static void AddSerialPartName(string partName)
        {
            var normalized = partName.NormalizedPartNameWithoutComments();
            if (string.IsNullOrEmpty(normalized)) return;
            lock (_gate)
            {
                Snapshot.SerialPartNames.Add(normalized);
                DomainSettings.SerialParts = new HashSet<string>(Snapshot.SerialPartNames);
            }
        }

        public static void RemoveSerialPartName(string partName)
        {
            lock (_gate)
            {
                Snapshot.SerialPartNames.Remove(partName.NormalizedPartNameWithoutComments());
                DomainSettings.SerialParts = new HashSet<string>(Snapshot.SerialPartNames);
            }
        }
    }
}
