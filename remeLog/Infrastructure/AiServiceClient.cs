using libeLog.Extensions;
using remeLog.Infrastructure.remeLog.Infrastructure;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace remeLog.Infrastructure
{
    public class AiServiceClient
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(180) };
        private readonly string _baseUrl;

        public AiServiceClient(string? baseUrl = null)
        {
            _baseUrl = (baseUrl ?? $"http://{AppSettings.AiIp.GetIpOrDefault()}:5051")
                .TrimEnd('/');
        }

        public async Task<AiAnalysisResult> AnalyzeAsync(
    string machine, DateTime shiftDate, IEnumerable<Part> parts,
    CancellationToken ct = default)
        {
            var partList = parts.ToList();

            var partsHistories = await LoadPartsHistoriesAsync(machine, shiftDate, partList, ct);

            var request = BuildRequest(machine, shiftDate, partList, partsHistories);
            try
            {
                var response = await _http.PostAsJsonAsync($"{_baseUrl}/api/analysis", request, ct);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<AiAnalysisResult>(cancellationToken: ct);
                return result ?? new AiAnalysisResult { Error = "Пустой ответ сервиса" };
            }
            catch (TaskCanceledException)
            {
                return new AiAnalysisResult { Error = "Превышено время ожидания" };
            }
            catch (Exception ex)
            {
                return new AiAnalysisResult { Error = $"Ошибка связи с AI сервисом: {ex.Message}" };
            }
        }

        /// <summary>
        /// Для каждой уникальной (PartName, Order) среди частей текущих суток
        /// подгружает и агрегирует историю той же детали за прошлые даты на том же станке.
        /// Ключ словаря — (PartName, Order), Machine один для всего вызова.
        /// </summary>
        private static async Task<Dictionary<(string PartName, string Order), PartsHistorySummary>>
            LoadPartsHistoriesAsync(
                string machine, DateTime shiftDate, List<Part> parts, CancellationToken ct)
        {
            var result = new Dictionary<(string, string), PartsHistorySummary>();

            var uniqueParties = parts
                .Select(p => (p.PartName, p.Order))
                .Distinct()
                .ToList();

            foreach (var (partName, order) in uniqueParties)
            {
                ct.ThrowIfCancellationRequested();
                var history = await Database.ReadPartsHistoryAsync(
                    partName, order, machine, shiftDate,
                    AppSettings.PartsHistoryMaxRecords,
                    AppSettings.PartsHistoryMaxDaysBack,
                    ct);
                result[(partName, order)] = SummarizePartsHistory(history);
            }

            return result;
        }

        /// <summary>
        /// Агрегирует список прошлых записей в построчный список смен.
        /// Использует Part.ProductionRatio/SetupRatio — формула не дублируется.
        /// </summary>
        private static PartsHistorySummary SummarizePartsHistory(
            List<PartsHistoryEntry> history)
        {
            var summary = new PartsHistorySummary { RecordsFound = history.Count };
            if (history.Count == 0) return summary;

            // Группируем по дате смены — несколько установок одной детали за сутки
            // объединяем в одну строку истории
            var byDate = history
                .GroupBy(e => e.Part.ShiftDate.Date)
                .OrderByDescending(g => g.Key);

            foreach (var group in byDate)
            {
                var groupParts = group.Select(e => e.Part).ToList();
                var entry = group.First();

                var validPr = groupParts
                    .Select(p => p.ProductionRatio)
                    .Where(r => !double.IsNaN(r) && !double.IsInfinity(r) && r > 0)
                    .ToList();
                double? avgPr = validPr.Count > 0
                    ? Math.Round(validPr.Average(), 2)
                    : (double?)null;

                var validSr = groupParts
                    .Select(p => p.SetupRatio)
                    .Where(r => !double.IsNaN(r) && !double.IsInfinity(r) && r > 0)
                    .ToList();
                double? avgSr = validSr.Count > 0
                    ? Math.Round(validSr.Average(), 2)
                    : (double?)null;

                double totalFinished = groupParts.Sum(p => p.FinishedCount);

                bool hasLowEfficiency = groupParts.Any(p =>
                {
                    var pr = p.ProductionRatio;
                    return !double.IsNaN(pr) && !double.IsInfinity(pr)
                        && pr > 0 && pr < 0.7
                        && string.IsNullOrWhiteSpace(p.MasterMachiningComment);
                });

                summary.Lines.Add(new PartsHistoryLine
                {
                    ShiftDate = group.Key,
                    ProductionRatio = avgPr,
                    SetupRatio = avgSr,
                    FinishedCount = totalFinished,
                    AnalystDecision = entry.AnalystDecision,
                    AnalystComment = entry.AnalystComment,
                    AiExplanation = entry.AiExplanation,
                    HasUnexplainedLowEfficiency = hasLowEfficiency,
                });
            }

            return summary;
        }

        private static object BuildRequest(
            string machine, DateTime shiftDate, IEnumerable<Part> parts,
            Dictionary<(string PartName, string Order), PartsHistorySummary> partsHistories)
        {
            var partList = parts.ToList();
            var partContexts = partList.Select(p => BuildPartContext(p, partsHistories)).ToList();
            var daySignals = DetectDaySignals(partList);

            return new
            {
                machine = machine,
                shiftDate = shiftDate.ToString("yyyy-MM-dd"),
                signals = daySignals,
                parts = partContexts,
            };
        }



        private static object BuildPartContext(
            Part p, 
            Dictionary<(string PartName, string Order), PartsHistorySummary> partsHistories)
        {
            bool noSetup = p.StartSetupTime == p.StartMachiningTime;
            bool noProduction = p.FinishedCount <= 0 && p.ProductionTimeFact <= 0;
            var manualComment = GetManualOperatorComment(p.OperatorComment);

            partsHistories.TryGetValue((p.PartName, p.Order), out var history);

            // Сериализуемое представление истории для JSON — анонимные типы,
            // null если истории нет (деталь делается впервые).
            object? partsHistoryObj = null;
            if (history != null && history.HasHistory)
            {
                var lines = history.Lines.Select(l => new
                {
                    shiftDate = l.ShiftDate.ToString("yyyy-MM-dd"),
                    productionRatio = l.ProductionRatio.HasValue
                        ? l.ProductionRatio.Value.ToString("0%")
                        : "б/и",
                    setupRatio = l.SetupRatio.HasValue
                        ? l.SetupRatio.Value.ToString("0%")
                        : "б/н",
                    finishedCount = (int)l.FinishedCount,
                    analystDecision = l.AnalystDecision ?? "не проверено",
                    analystComment = l.AnalystComment,
                    aiExplanation = l.AiExplanation,
                    hasUnexplainedLowEfficiency = l.HasUnexplainedLowEfficiency,
                }).ToList();

                partsHistoryObj = new
                {
                    recordsFound = history.RecordsFound,
                    lines = lines,
                };
            }

            return new
            {
                partName = p.PartName,
                order = p.Order,
                setupNumber = p.Setup,

                setupRatio = SafeDouble(p.SetupRatio),
                productionRatio = SafeDouble(p.ProductionRatio),

                finishedCount = p.FinishedCount,
                setupTimePlan = p.SetupTimePlan,
                setupTimeFact = p.SetupTimeFact,
                singleProductionTimePlan = p.SingleProductionTimePlan,
                productionTimeFact = p.ProductionTimeFact,
                partialSetup = p.PartialSetupTime,
                machiningTime = Math.Round(p.MachiningTime.TotalMinutes, 1),
                downtimeRatio = SafeDouble(p.SpecifiedDowntimesRatio),

                operatorComment = manualComment,
                masterSetupComment = p.MasterSetupComment ?? string.Empty,
                masterMachiningComment = p.MasterMachiningComment ?? string.Empty,
                masterComment = p.MasterComment ?? string.Empty,

                noManualOperatorComment = string.IsNullOrWhiteSpace(manualComment),
                noSetupHappened = noSetup,
                noProductionHappened = noProduction,

                signals = DetectPartSignals(p, noSetup, noProduction, manualComment),

                partsHistory = partsHistoryObj,
            };
        }

        private static List<string> DetectPartSignals(
            Part p, bool noSetup, bool noProduction, string manualComment)
        {
            var s = new List<string>();
            var machMins = p.MachiningTime.TotalMinutes;

            // Машинное время > штучного норматива более чем на 20%
            // (небольшое превышение — погрешность измерения, существенное — реальная проблема)
            if (machMins > 0.5 && p.SingleProductionTimePlan > 0
                && machMins > p.SingleProductionTimePlan * 1.2)
                s.Add($"Машинное время {machMins:0.#}мин > штучного норматива {p.SingleProductionTimePlan:0.#}мин ({machMins / p.SingleProductionTimePlan:0%})");

            // Частичная наладка существенно превышает норматив наладки (> 2×)
            // Небольшое превышение нормально, очень большое — подозрительно
            if (p.PartialSetupTime > 0 && p.SetupTimePlan > 0
                && p.PartialSetupTime > p.SetupTimePlan * 2.0)
                s.Add($"Частичная наладка {p.PartialSetupTime:0}мин > 2× норматива наладки {p.SetupTimePlan:0}мин");

            // Оператор сообщает о проблеме с нормативом (жёсткое правило!)
            if (OperatorMentionsNormativeIssue(manualComment))
                s.Add("Оператор сообщает о некорректном нормативе или технологии");

            // КПД наладки < 70% без объяснения мастера (освоение — исключение)
            var sr = p.SetupRatio;
            if (IsValidRatio(sr) && sr < 0.695 && p.SetupTimeFact > 0
                && string.IsNullOrWhiteSpace(p.MasterSetupComment)
                && !OperatorMentionsExcusableReason(manualComment))
                s.Add($"КПД наладки {sr:0%} без объяснения мастера");

            // КПД изготовления < 70% без объяснения мастера
            var pr = p.ProductionRatio;
            if (IsValidRatio(pr) && pr < 0.695 && p.FinishedCount > 0
                && string.IsNullOrWhiteSpace(p.MasterMachiningComment)
                && !OperatorMentionsExcusableReason(manualComment))
                s.Add($"КПД изготовления {pr:0%} без объяснения мастера");

            // КПД изготовления > 120% без объяснения — норматив занижен?
            if (IsValidRatio(pr) && pr > 1.2 && p.FinishedCount > 0
                && string.IsNullOrWhiteSpace(p.MasterMachiningComment))
                s.Add($"КПД изготовления {pr:0%} > 120% — возможно норматив занижен");

            // Простои > 30% без ручного комментария (авто-раздел не считается объяснением)
            var dt = p.SpecifiedDowntimesRatio;
            if (IsValidRatio(dt) && dt > 0.30
                && string.IsNullOrWhiteSpace(manualComment)
                && string.IsNullOrWhiteSpace(p.MasterComment))
                s.Add($"Простои {dt:0%} без пояснений");

            // Время изготовления записано но деталей нет — противоречие в данных
            if (p.ProductionTimeFact > 5 && p.FinishedCount == 0
                && !string.IsNullOrWhiteSpace(p.MasterMachiningComment) == false)
                s.Add($"Время изготовления {p.ProductionTimeFact:0}мин но finishedCount = 0");

            // Машинное время = 0 при наличии изготовления
            if (machMins < 0.5 && p.FinishedCount > 0 && p.SingleProductionTimePlan > 0)
                s.Add("Машинное время = 0 при наличии изготовления");

            return s;
        }

        // Сигналы уровня дня

        private static List<string> DetectDaySignals(List<Part> parts)
        {
            var s = new List<string>();

            // Повторяющиеся жалобы на оборудование в ручных комментариях (≥2 записей)
            var withEquipment = parts
                .Where(p => ContainsEquipmentKeywords(GetManualOperatorComment(p.OperatorComment)))
                .ToList();
            if (withEquipment.Count >= 2)
                s.Add($"Повторяющиеся жалобы на оборудование в {withEquipment.Count} записях");

            return s;
        }

        // Вспомогательные

        /// <summary>
        /// Ручная часть комментария оператора — текст ДО авто-раздела "Отмеченные простои:".
        /// Именно этот текст является реальным сообщением оператора.
        /// </summary>
        private static string GetManualOperatorComment(string? comment)
        {
            if (string.IsNullOrWhiteSpace(comment)) return "";
            var idx = comment.IndexOf("Отмеченные простои", StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? comment[..idx].Trim() : comment.Trim();
        }

        private static double? SafeDouble(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? null : Math.Round(v, 4);

        private static bool IsValidRatio(double v) =>
            !double.IsNaN(v) && !double.IsInfinity(v) && v >= 0;

        private static bool OperatorMentionsNormativeIssue(string manual)
        {
            if (string.IsNullOrWhiteSpace(manual)) return false;
            var l = manual.ToLowerInvariant();
            return l.Contains("норматив") || l.Contains("не соответствует")
                || l.Contains("некорректн") || l.Contains("режимы не")
                || l.Contains("программа не соответ") || l.Contains("скорректировать")
                || l.Contains("не укладыва");  // "не укладываюсь в норматив"
        }

        private static bool OperatorMentionsExcusableReason(string manual)
        {
            if (string.IsNullOrWhiteSpace(manual)) return false;
            var l = manual.ToLowerInvariant();
            return l.Contains("освоен");
        }

        private static bool ContainsEquipmentKeywords(string manual)
        {
            if (string.IsNullOrWhiteSpace(manual)) return false;
            var l = manual.ToLowerInvariant();
            return false; // заглушка
        }
    }


    public class AiAnalysisResult
    {
        [JsonPropertyName("requiresReview")] public bool RequiresReview { get; set; }
        [JsonPropertyName("confidence")] public double Confidence { get; set; }
        [JsonPropertyName("signals")] public string[] Signals { get; set; } = Array.Empty<string>();
        [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
        [JsonPropertyName("suggestedReason")] public string SuggestedReason { get; set; } = "";
        [JsonPropertyName("error")] public string? Error { get; set; }
        public bool HasError => !string.IsNullOrEmpty(Error);
    }
}