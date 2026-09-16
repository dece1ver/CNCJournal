using remeLog.Core.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace remeLog.Core.Services.Demo
{
    /// <summary>
    /// Детерминированный генератор mock-данных для демо-режима (<c>--demo</c>).
    /// Строит реалистичный сценарий последних 14 дней: станки, операторы, смены,
    /// детали с типовыми кейсами (норма, длинная наладка, брак, освоение,
    /// переопределение причин аналитиком, серийные, простои).
    /// Чистая функция от (today, seed) — воспроизводима между запусками.
    /// </summary>
    public static class DemoDataFactory
    {
        public const int DefaultSeed = 42;
        public const int DaysBack = 14;

        public sealed record DemoSnapshot(
            List<(string Machine, string Type)> Machines,
            List<OperatorInfo> Operators,
            List<Part> Parts,
            List<ShiftInfo> Shifts,
            List<string> DowntimeReasons,
            List<(string Reason, bool RequireComment)> SetupReasons,
            List<(string Reason, bool RequireComment)> MachiningReasons,
            List<DateTime> Holidays,
            Dictionary<string, double> MaxSetupLimits,
            List<MachineActivity> MachineActivity,
            HashSet<string> SerialPartNames);

        private static readonly string[] PartNames =
        {
            "Корпус редуктора", "Вал приводной", "Фланец соединительный",
            "Крышка подшипника", "Шестерня ведущая", "Втулка направляющая",
            "Опора стойки", "Диск тормозной", "Муфта кулачковая",
            "Плита основания", "Кронштейн", "Гильза цилиндра",
        };

        // Серийные детали — нормализованные имена должны совпадать с PartName
        // после NormalizedPartNameWithoutComments (нижний регистр, без скобок).
        private static readonly string[] SerialNames =
        {
            "Корпус редуктора", "Вал приводной", "Фланец соединительный",
        };

        private static readonly string[] Orders =
        {
            "ПР2601-00001", "ПР2601-00002", "ПР2602-00011", "ПР2603-00007",
            "ПР2604-00021", "ПР2605-00003", "ПР2606-00015",
        };

        public static DemoSnapshot Build(DateTime today, int seed = DefaultSeed)
        {
            var rnd = new Random(seed);
            var endDate = today.Date;
            var startDate = endDate.AddDays(-(DaysBack - 1));

            var machines = new List<(string Machine, string Type)>
            {
                ("Hyundai WIA SKT21 №104", "Токарный"),
                ("DMG MORI NLX 2500 №201", "Токарный"),
                ("Mazak QT-200 №112", "Токарный"),
                ("Doosan Puma 2600 №305", "Токарный"),
                ("Hermle C42 №401", "Фрезерный"),
                ("2Н135 №502", "Сверлильный"),
            };

            var operators = new List<OperatorInfo>
            {
                new(1, "Иван", "Петров", "Сергеевич", 5, true),
                new(2, "Сергей", "Сидоров", "Петрович", 4, true),
                new(3, "Алексей", "Кузнецов", "Игоревич", 3, true),
                new(4, "Дмитрий", "Смирнов", "Алексеевич", 6, true),
                new(5, "Ольга", "Васильева", "Николаевна", 4, true),
                new(6, "Николай", "Морозов", "Викторович", 2, true),
                new(7, "Андрей", "Попов", "Дмитриевич", 3, true),
                new(8, "Игорь", "Соколов", "Олегович", 5, false),
            };
            var activeOperators = operators.Where(o => o.IsActive).ToList();

            var setupReasons = new List<(string Reason, bool RequireComment)>
            {
                ("Отсутствие нормативов", false),
                ("Некорректные нормативы", false),
                ("Освоение", false),
                ("Изготовление типовой детали", false),
                ("Неопытный оператор", false),
                ("Другое", true),
            };
            var machiningReasons = new List<(string Reason, bool RequireComment)>
            {
                ("Отсутствие нормативов", false),
                ("Некорректные нормативы", false),
                ("Несоответствующие заготовки", false),
                ("Штучная/длительная работа", false),
                ("Особенности изготовления", false),
                ("Другое", true),
            };
            var downtimeReasons = new List<string>
            {
                "Отсутствие заготовок", "Отсутствие инструмента", "Ожидание крана",
                "Отключение электроэнергии", "Поломка оборудования", "Ожидание технолога",
                "Уборка рабочего места", "Совещание",
            };

            // Праздники: 2 даты внутри периода (не считаются рабочими сменами).
            var holidays = new List<DateTime> { startDate.AddDays(3), endDate.AddDays(-5) };

            var maxSetupLimits = machines.ToDictionary(m => m.Machine, m => 2.0);

            var serialPartNames = new HashSet<string>(
                SerialNames.Select(n => n.NormalizedPartNameWithoutComments()));

            var parts = new List<Part>();
            var shifts = new List<ShiftInfo>();
            int shiftId = 1;

            // Последний станок списка — без деталей за период (кейс «нет данных»).
            var workingMachines = machines.Take(machines.Count - 1).ToList();

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                foreach (var (machine, _) in workingMachines)
                {
                    // Выходные: реже работаем (треть шансов), праздники — стоим.
                    bool isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                    if (holidays.Contains(date)) continue;
                    if (isWeekend && rnd.NextDouble() < 0.65) continue;

                    foreach (var shiftType in new[] { ShiftType.Day, ShiftType.Night })
                    {
                        // Ночь реже дня.
                        if (shiftType == ShiftType.Night && rnd.NextDouble() < 0.3) continue;

                        var shiftName = new Shift(shiftType).Name;
                        var op = activeOperators[rnd.Next(activeOperators.Count)];
                        var operatorName = $"{op.LastName} {op.FirstName[0]}.{op.Patronymic[0]}.";

                        // 1–2 детали за смену. Деталь должна целиком (с запасом 30 мин
                        // на переход) умещаться в смену: Part.FixedDate схлопывает время
                        // на сутки ShiftDate, и «вылезание» далеко за границу смены
                        // (особенно ночь за 08:00) ломает цепочку времён.
                        int partCount = rnd.Next(1, 3);
                        var cursor = ShiftStart(date, shiftType);
                        var shiftEnd = ShiftEnd(date, shiftType);
                        bool anyPart = false;

                        for (int i = 0; i < partCount; i++)
                        {
                            int scenario = parts.Count % 9;
                            int setupMin = scenario == 5
                                ? 300 + rnd.Next(0, 120)
                                : 40 + rnd.Next(0, 140);
                            int machMin = 120 + rnd.Next(0, 300);
                            if ((cursor.AddMinutes(setupMin + machMin + 50) - shiftEnd).TotalMinutes > 30) break;
                            var part = BuildPart(rnd, machine, shiftName, date, operatorName,
                                ref cursor, scenario, setupMin, machMin);
                            parts.Add(part);
                            anyPart = true;
                        }

                        if (!anyPart) continue;

                        // Отчёт мастера: у старых смен заполнен, у свежих — частично.
                        int ageDays = (endDate - date).Days;
                        bool reportFilled = ageDays > 1 || rnd.NextDouble() < 0.7;
                        shifts.Add(new ShiftInfo(
                            shiftId++, date, shiftType, machine,
                            reportFilled ? "Петров П.П." : "",
                            0, "", reportFilled ? $"Смена сдана ({shiftName.ToLower()})" : "",
                            reportFilled && rnd.NextDouble() < 0.5,
                            null, null, null, null, null, null, null, null, null, null, null, null));
                    }
                }
            }

            var activities = BuildActivities(workingMachines.Select(m => m.Machine).ToList(), parts);

            return new DemoSnapshot(
                machines, operators, parts, shifts,
                downtimeReasons, setupReasons, machiningReasons,
                holidays, maxSetupLimits, activities, serialPartNames);
        }

        private static DateTime ShiftStart(DateTime date, ShiftType type) =>
            type == ShiftType.Day ? date.AddHours(7) : date.AddHours(19);

        private static DateTime ShiftEnd(DateTime date, ShiftType type) =>
            type == ShiftType.Day ? date.AddHours(19) : date.AddDays(1).AddHours(7);

        private static Part BuildPart(
            Random rnd, string machine, string shiftName, DateTime shiftDate,
            string operatorName, ref DateTime cursor, int scenario,
            int setupMinutes, int machiningMinutes)
        {
            bool isSerial = rnd.NextDouble() < 0.3;
            string partName = isSerial
                ? SerialNames[rnd.Next(SerialNames.Length)]
                : PartNames[rnd.Next(PartNames.Length)];
            string order = Orders[rnd.Next(Orders.Length)] + $".{rnd.Next(1, 4)}.{rnd.Next(1, 4)}";
            int setup = rnd.Next(1, 3);

            // Кейс строки: 0–4 норма, 5 длинная наладка, 6 брак,
            // 7 освоение (нет норматива), 8 переопределение аналитиком.
            var startSetup = cursor;
            var startMachining = startSetup.AddMinutes(setupMinutes);
            var endMachining = startMachining.AddMinutes(machiningMinutes);
            cursor = endMachining.AddMinutes(rnd.Next(10, 50));

            double setupDowntimes = rnd.NextDouble() < 0.25 ? rnd.Next(5, 45) : 0;
            double machiningDowntimes = rnd.NextDouble() < 0.25 ? rnd.Next(5, 60) : 0;
            double partialSetup = rnd.NextDouble() < 0.12 ? rnd.Next(10, 60) : 0;
            double toolSearching = rnd.NextDouble() < 0.12 ? rnd.Next(5, 30) : 0;
            double hardwareFailure = rnd.NextDouble() < 0.06 ? rnd.Next(15, 60) : 0;

            // Факты выводятся из времён: нормативы подбираем под КПД ≈ 0.9–1.1.
            double setupFact = (startMachining - startSetup).TotalMinutes
                - DateTimes.GetBreaksBetween(startSetup, startMachining).TotalMinutes
                - setupDowntimes - partialSetup;
            if (setupFact < 5) setupFact = 5;
            double productionFact = (endMachining - startMachining).TotalMinutes
                - DateTimes.GetBreaksBetween(startMachining, endMachining).TotalMinutes
                - machiningDowntimes;
            if (productionFact < 10) productionFact = 10;

            double finished = scenario == 7 ? 0 : 5 + rnd.Next(0, 60);
            double finishedFact = finished > 0 ? finished - 1 : 0; // одна в наладке
            if (finishedFact < 0) finishedFact = 0;

            double setupPlan = setupFact * (0.9 + rnd.NextDouble() * 0.2);
            double singlePlan = finishedFact > 0
                ? productionFact / finishedFact * (0.9 + rnd.NextDouble() * 0.2)
                : 4 + rnd.NextDouble() * 6;

            string masterSetup = "", masterSetupDetail = "";
            string masterMachining = "", masterMachiningDetail = "";
            string setupOverride = "", setupOverrideComment = "";
            string machiningOverride = "", machiningOverrideComment = "";
            string overrideBy = "";
            DateTime? overrideAt = null;
            int defective = 0;
            string engineerConclusion = "";
            string operatorComment = rnd.NextDouble() < 0.3 ? "Работа без замечаний" : "";

            switch (scenario)
            {
                case 5: // длинная наладка — причина + комментарии Robert
                    masterSetup = "Освоение";
                    masterSetupDetail = "Первая установка, привязка инструмента с нуля";
                    setupPlan = setupFact * 0.5;
                    break;
                case 6: // брак
                    defective = rnd.Next(1, 4);
                    operatorComment = $"Брак {defective} шт — занижение размера";
                    masterMachining = "Несоответствующие заготовки";
                    masterMachiningDetail = "Припуск гулял, правили режимы";
                    break;
                case 7: // освоение без норматива
                    setupPlan = 0;
                    singlePlan = 0;
                    masterSetup = "Отсутствие нормативов";
                    masterSetupDetail = "Деталь новая, норматив не установлен";
                    masterMachining = "Отсутствие нормативов";
                    masterMachiningDetail = "Штучное время не нормировано";
                    finished = 0;
                    break;
                case 8: // переопределение аналитиком
                    masterSetup = "Другое";
                    masterSetupDetail = "Долго искали инструмент";
                    setupOverride = "Неопытный оператор";
                    setupOverrideComment = "Подтверждено по журналу: первый месяц на станке";
                    overrideBy = "demo-analyst";
                    overrideAt = shiftDate.AddDays(1).AddHours(9);
                    // Причина «Другое» требует комментария — detail уже заполнен.
                    if (setupDowntimes > 0.5 * (endMachining - startSetup).TotalMinutes)
                        masterSetupDetail += "; простои отмечены";
                    break;
                default:
                    // Отклонения КПД вне 70–200%: объясняем типовой причиной,
                    // иначе валидация потребует причину (это и тестируем частично:
                    // каждая ~9-я строка без причины остаётся с ошибкой валидации).
                    double setupRatio = setupPlan / setupFact;
                    if ((setupRatio < 0.695 || setupRatio > 2.0) && scenario != 0)
                    {
                        masterSetup = "Особенности изготовления";
                        masterSetupDetail = "Корректировка режимов по факту";
                    }
                    break;
            }

            return new Part(
                Guid.NewGuid(), machine, shiftName, shiftDate, operatorName,
                partName, order, setup, finished, defective,
                (int)finished + rnd.Next(0, 20) + defective,
                startSetup, startMachining, 0, endMachining,
                setupPlan, setupPlan, singlePlan, 0,
                TimeSpan.FromMinutes(2 + rnd.NextDouble() * 8),
                setupDowntimes, machiningDowntimes, partialSetup,
                0, 0, toolSearching, 0, 0, 0, 0, hardwareFailure, 0,
                operatorComment, masterSetup, masterMachining,
                setupDowntimes + machiningDowntimes > 0 ? "Простои отмечены" : "",
                "", "", masterSetupDetail, masterMachiningDetail,
                0, 0, engineerConclusion, false, "",
                scenario == 5 ? "Сложная привязка, запрошен технолог" : "",
                scenario == 5 ? "Технолог помог с корректорами" : "",
                "", 0, "",
                setupOverride, setupOverrideComment, false, "",
                machiningOverride, machiningOverrideComment, false, "",
                overrideBy, overrideAt);
        }

        private static List<MachineActivity> BuildActivities(List<string> machines, List<Part> parts)
        {
            var now = DateTime.Now;
            var result = new List<MachineActivity>();
            if (machines.Count == 0) return result;

            // Первый станок — «в изготовлении» прямо сейчас, второй — наладка,
            // третий — простой, остальные — stale (покажут «нет данных»).
            for (int i = 0; i < machines.Count; i++)
            {
                var last = parts.LastOrDefault(p => p.Machine == machines[i]);
                bool fresh = i < 3;
                result.Add(new MachineActivity
                {
                    Machine = machines[i],
                    Status = i switch { 0 => MachineActivity.MachiningStatus, 1 => MachineActivity.SetupStatus, 2 => MachineActivity.IdleStatus, _ => MachineActivity.IdleStatus },
                    PartName = last?.PartName ?? "Корпус редуктора",
                    Order = last?.Order ?? "ПР2601-00001.1.1",
                    Operator = last?.Operator ?? "Петров И.С.",
                    Setup = (byte)(last?.Setup ?? 1),
                    Shift = last?.Shift ?? Shifts.Day,
                    PhaseStartLocal = fresh ? now.AddMinutes(-(20 + i * 15)) : now.AddHours(-5),
                    UpdatedLocal = fresh ? now.AddSeconds(-10) : now.AddMinutes(-30),
                });
            }
            return result;
        }
    }
}
