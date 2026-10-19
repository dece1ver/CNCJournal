using libeLog;
using libeLog.Base;
using remeLog.Core;
using remeLog.Core.Db;
using remeLog.Infrastructure;
using remeLog.Infrastructure.Types;
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

namespace remeLog.ViewModels
{
    internal class BatchAiAnalysisViewModel : ViewModel
    {
        private readonly AiServiceClient _aiClient = new();
        private CancellationTokenSource? _cts;
        private int _refreshVersion;

        public ObservableCollection<BatchItem> Items { get; } = new();

        private bool _IsRunning;
        public bool IsRunning
        {
            get => _IsRunning;
            set
            {
                if (Set(ref _IsRunning, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private double _OverallProgress;
        public double OverallProgress
        {
            get => _OverallProgress;
            set => Set(ref _OverallProgress, value);
        }

        private string _StatusText = "Готов к запуску";
        public string StatusText
        {
            get => _StatusText;
            set => Set(ref _StatusText, value);
        }

        private int _TotalCount;
        public int TotalCount
        {
            get => _TotalCount;
            set => Set(ref _TotalCount, value);
        }

        private int _ProcessedCount;
        public int ProcessedCount
        {
            get => _ProcessedCount;
            set => Set(ref _ProcessedCount, value);
        }

        private bool _OnlyPending = true;
        public bool OnlyPending
        {
            get => _OnlyPending;
            set
            {
                if (Set(ref _OnlyPending, value))
                {
                    _ = RefreshListAsync();
                }
            }
        }

        private bool _AutoCloseEmpty;
        /// <summary> Отдельный режим: автозакрытие суток без записей при наличии
        /// обоих отчётов мастера. ИИ не используется. </summary>
        public bool AutoCloseEmpty
        {
            get => _AutoCloseEmpty;
            set
            {
                if (Set(ref _AutoCloseEmpty, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                    _ = RefreshListAsync();
                }
            }
        }

        private DateTime _FromDate = DateTime.Today.AddDays(-30);
        /// <summary> Начало периода поиска пустых суток (только AutoCloseEmpty). </summary>
        public DateTime FromDate
        {
            get => _FromDate;
            set
            {
                if (Set(ref _FromDate, value) && AutoCloseEmpty && !IsRunning)
                    _ = RefreshListAsync();
            }
        }

        private DateTime _ToDate = DateTime.Today;
        /// <summary> Конец периода поиска пустых суток (только AutoCloseEmpty). </summary>
        public DateTime ToDate
        {
            get => _ToDate;
            set
            {
                if (Set(ref _ToDate, value) && AutoCloseEmpty && !IsRunning)
                    _ = RefreshListAsync();
            }
        }

        private bool _ThinkingEnabled;
        /// <summary> Режим ИИ-анализа: вкл — агентский контур, выкл — обычный thinking-режим.
        /// Имя сохранено ради привязки и настройки AppSettings.AiThinkingEnabled. </summary>
        public bool ThinkingEnabled
        {
            get => _ThinkingEnabled;
            set
            {
                if (Set(ref _ThinkingEnabled, value))
                {
                    AppSettings.Instance.AiThinkingEnabled = value;
                    AppSettings.Save();
                }
            }
        }

        public ICommand StartCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ShowAllMachinesCommand { get; }
        public ICommand HideAllMachinesCommand { get; }
        public ICommand InvertMachinesCommand { get; }
        public ICommand ShowAutoCloseHistoryCommand { get; }

        /// <summary> Фильтр по станкам для режима автозакрытия (общий контрол,
        /// как в календаре проверки; выбор запоминается). </summary>
        public ObservableCollection<MachineFilter> MachineFilters { get; } = new();

        public List<string> FilteredMachines =>
            MachineFilters.Where(m => m.Filter).Select(m => m.Machine).ToList();

        /// <summary> Текст на кнопке фильтра по станкам. </summary>
        public string MachineFilterSummary => MachineFilters.Summary();

        public BatchAiAnalysisViewModel()
        {
            _ThinkingEnabled = AppSettings.Instance.AiThinkingEnabled;
            // В режиме автозакрытия ИИ не нужен — Старт доступен и без него.
            StartCommand = new LambdaCommand(OnStartExecuted,
                _ => !IsRunning && (AutoCloseEmpty || AiHealthMonitor.Instance.IsAiAvailable));
            CancelCommand = new LambdaCommand(OnCancelExecuted, _ => IsRunning);
            RefreshCommand = new LambdaCommand(OnRefreshExecuted, _ => !IsRunning);
            ShowAllMachinesCommand = new LambdaCommand(_ => SetAllMachineFilters(true), _ => !IsRunning);
            HideAllMachinesCommand = new LambdaCommand(_ => SetAllMachineFilters(false), _ => !IsRunning);
            InvertMachinesCommand = new LambdaCommand(_ =>
            {
                foreach (var mf in MachineFilters) mf.Filter = !mf.Filter;
                AfterMachineFilterChanged();
            }, _ => !IsRunning);
            ShowAutoCloseHistoryCommand = new LambdaCommand(_ =>
            {
                var window = new AutoCloseHistoryWindow { Owner = Application.Current.MainWindow };
                window.Show();
            }, _ => !IsRunning);
            // ИИ может упасть/подняться прямо при открытом окне — пересчитать доступность Старта.
            AiHealthMonitor.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AiHealthMonitor.IsAiAvailable))
                    CommandManager.InvalidateRequerySuggested();
            };
            _ = RefreshListAsync();
        }

        private async Task RefreshListAsync()
        {
            if (AutoCloseEmpty)
            {
                await RefreshEmptyCandidatesAsync();
                return;
            }
            StatusText = "Загрузка списка суток...";
            var all = await Database.GetAllDayReviewsAsync();
            var list = OnlyPending
                ? all.Where(r => !r.HasAiResult).OrderBy(r => r.ShiftDate).ThenBy(r => r.Machine).ToList()
                : all.OrderBy(r => r.ShiftDate).ThenBy(r => r.Machine).ToList();

            Items.Clear();
            foreach (var r in list)
            {
                Items.Add(new BatchItem
                {
                    DayReviewId = r.Id,
                    Date = r.ShiftDate,
                    Machine = r.Machine,
                    State = r.HasAiResult ? BatchItemState.Skipped : BatchItemState.Pending,
                    Summary = r.HasAiResult
                        ? BuildExistingSummary(r)
                        : "ожидание",
                });
            }
            TotalCount = Items.Count;
            ProcessedCount = 0;
            OverallProgress = 0;
            var toAnalyze = OnlyPending
                ? list.Count(r => !r.HasAiResult)
                : list.Count;
            StatusText = list.Count == 0
                ? "Нет записей для анализа"
                : $"К анализу: {toAnalyze} (всего в БД: {all.Count})";
        }

        private void SetAllMachineFilters(bool value)
        {
            foreach (var mf in MachineFilters) mf.Filter = value;
            AfterMachineFilterChanged();
        }

        private void AfterMachineFilterChanged()
        {
            SaveMachineFilter();
            OnPropertyChanged(nameof(FilteredMachines));
            OnPropertyChanged(nameof(MachineFilterSummary));
            if (AutoCloseEmpty && !IsRunning)
                _ = RefreshListAsync();
        }

        private void SaveMachineFilter()
        {
            AppSettings.Instance.BatchAutoCloseSelectedMachines =
                MachineFilters.Where(m => m.Filter).Select(m => m.Machine).ToList();
            AppSettings.Save();
        }

        /// <summary>
        /// Станки для фильтра: грузятся один раз, выбор восстанавливается
        /// из настроек. Пустой сохранённый выбор = выбраны все.
        /// </summary>
        /// <returns> false — станки прочитать не удалось. </returns>
        private async Task<bool> EnsureMachinesAsync()
        {
            if (_machinesLoaded) return MachineFilters.Count > 0;
            var machinesResult = await Database.ReadMachinesAsync();
            if (!machinesResult.IsOk)
            {
                StatusText = $"Не удалось прочитать станки: {machinesResult.Error}";
                return false;
            }
            var saved = AppSettings.Instance.BatchAutoCloseSelectedMachines ?? new List<string>();
            foreach (var mf in machinesResult.Value ?? Enumerable.Empty<MachineFilter>())
            {
                mf.Filter = saved.Count == 0 || saved.Contains(mf.Machine);
                mf.PropertyChanged += (_, _) => AfterMachineFilterChanged();
                MachineFilters.Add(mf);
            }
            _machinesLoaded = true;
            OnPropertyChanged(nameof(FilteredMachines));
            OnPropertyChanged(nameof(MachineFilterSummary));
            return MachineFilters.Count > 0;
        }

        private bool _machinesLoaded;

        /// <summary>
        /// Кандидаты на автозакрытие: пустые суткостанки за выбранный период, по
        /// которым есть оба отчёта мастера, но день ещё не проверен целиком.
        /// Праздники пропускаются, как в календаре проверки.
        /// </summary>
        private async Task RefreshEmptyCandidatesAsync()
        {
            var version = ++_refreshVersion;
            StatusText = "Поиск пустых суток с отчётами мастера...";
            Items.Clear();
            TotalCount = 0;
            ProcessedCount = 0;
            OverallProgress = 0;

            var to = ToDate.Date;
            var from = FromDate.Date;
            if (from > to)
            {
                StatusText = "Начало периода позже конца — поправьте даты";
                return;
            }

            if (!await EnsureMachinesAsync()) return;
            if (version != _refreshVersion) return;
            var machines = FilteredMachines;
            if (machines.Count == 0)
            {
                StatusText = "Не выбран ни один станок — отметьте станки в фильтре";
                return;
            }

            var shiftsResult = Database.GetShiftsByPeriod(machines, from, to, new Shift(ShiftType.All));
            if (!shiftsResult.IsOk)
            {
                StatusText = $"Не удалось прочитать смены: {shiftsResult.Error}";
                return;
            }

            // Пустота — одним запросом на станок за весь период (а не по запросу
            // на сутки): непустые даты вылетают из списка сразу. Перепроверка
            // в момент закрытия остаётся страховкой (данные могли поменяться).
            var nonEmpty = new HashSet<(string Machine, DateTime Date)>();
            foreach (var machine in machines)
            {
                try
                {
                    var machineParts = await Database.ReadPartsByShiftDateAndMachine(
                        from, to, machine, CancellationToken.None);
                    foreach (var p in machineParts)
                        nonEmpty.Add((machine, p.ShiftDate.Date));
                }
                catch (Exception ex)
                {
                    // Станок без проверки пустоты: его сутки останутся
                    // кандидатами, исполнение перепроверит и решит.
                    Util.WriteLog(ex, $"BatchAutoClose: {machine}");
                }
                if (version != _refreshVersion) return;
            }

            var holidays = new HashSet<DateTime>(DomainSettings.Holidays.Select(d => d.Date));
            var count = 0;
            var closed = 0;
            var noReports = 0;
            var withRecords = 0;
            foreach (var g in (shiftsResult.Value ?? Enumerable.Empty<ShiftInfo>())
                         .GroupBy(s => new { s.Machine, Date = s.ShiftDate.Date })
                         .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Machine))
            {
                if (g.Key.Date < from || g.Key.Date > to) continue;
                if (holidays.Contains(g.Key.Date)) continue;
                var day = g.FirstOrDefault(s => s.Shift == new Shift(ShiftType.Day).Name);
                var night = g.FirstOrDefault(s => s.Shift == new Shift(ShiftType.Night).Name);
                // Закрывать можно только при обоих отчётах; без них — не кандидат.
                if (day is null || night is null) { noReports++; continue; }
                // Уже зелёные в календаре проверки — пропускаем.
                if (day.IsChecked && night.IsChecked) { closed++; continue; }
                // Есть записи — не пустые, в список не берём.
                if (nonEmpty.Contains((g.Key.Machine, g.Key.Date))) { withRecords++; continue; }
                Items.Add(new BatchItem
                {
                    DayReviewId = 0,
                    Date = g.Key.Date,
                    Machine = g.Key.Machine,
                    State = BatchItemState.Pending,
                    Summary = "отчёты есть — к проверке",
                });
                count++;
            }

            TotalCount = count;
            var scope = $"{from:dd.MM.yyyy}–{to:dd.MM.yyyy}";
            var rest = $"с записями: {withRecords}, уже закрыто: {closed}, без обоих отчётов: {noReports}";
            StatusText = count == 0
                ? $"Нет пустых кандидатов ({scope}): {rest}"
                : $"Пустых к автозакрытию: {count} ({scope}); {rest}";
            await Task.CompletedTask;
        }

        private static string BuildExistingSummary(DayReview r)
        {
            var verdict = r.AiRequiresReview == true ? "требует проверки"
                        : r.AiRequiresReview == false ? "ок"
                        : "—";
            var conf = r.AiConfidence.HasValue ? $"{r.AiConfidence.Value * 100:F0}%" : "—";
            return $"готово: {verdict} ({conf})";
        }

        private void OnRefreshExecuted(object _)
        {
            _ = RefreshListAsync();
        }

        private void OnCancelExecuted(object _)
        {
            _cts?.Cancel();
            StatusText = "Останавливаю...";
        }

        private async void OnStartExecuted(object _)
        {
            if (Items.Count == 0)
            {
                StatusText = "Нечего анализировать";
                return;
            }

            IsRunning = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            var pending = OnlyPending
                ? Items.Where(i => i.State == BatchItemState.Pending).ToList()
                : Items.Where(i => i.State == BatchItemState.Pending || i.State == BatchItemState.Skipped).ToList();
            var total = pending.Count;
            var done = 0;
            ProcessedCount = 0;
            TotalCount = total;
            OverallProgress = 0;

            try
            {
                foreach (var item in pending)
                {
                    if (ct.IsCancellationRequested)
                    {
                        item.State = BatchItemState.Cancelled;
                        item.Summary = "отменено";
                        break;
                    }

                    item.State = BatchItemState.Running;
                    item.Summary = AutoCloseEmpty ? "проверка..." : "анализ...";
                    item.ThinkingThoughts = "";
                    StatusText = AutoCloseEmpty
                        ? $"Закрытие {done + 1}/{total}: {item.Machine} {item.Date:dd.MM.yyyy}"
                        : $"Анализ {done + 1}/{total}: {item.Machine} {item.Date:dd.MM.yyyy}";

                    try
                    {
                        var parts = await Database.ReadPartsByShiftDateAndMachine(
                            item.Date, item.Date, item.Machine, ct);

                        if (AutoCloseEmpty)
                        {
                            await AutoCloseEmptyItemAsync(item, parts, ct);
                            done++;
                            ProcessedCount = done;
                            OverallProgress = (double)done / total;
                            continue;
                        }

                        var progress = ItemProgress(item);

                        var result = await _aiClient.AnalyzeAsync(
                            item.Machine, item.Date, parts,
                            thinkingProgress: progress,
                            ct: ct);

                        if (ct.IsCancellationRequested)
                        {
                            item.State = BatchItemState.Cancelled;
                            item.Summary = "отменено";
                            break;
                        }

                        if (result.HasError)
                        {
                            item.State = BatchItemState.Error;
                            item.Summary = $"ошибка: {result.Error}";
                            item.Explanation = result.Error;
                        }
                        else
                        {
                            // Агентские вердикты пишутся как штатные (теневой режим снят).
                            await Database.SaveAiAnalysisAsync(
                                item.DayReviewId, result, AppSettings.AiModel, ThinkingEnabled);

                            item.State = BatchItemState.Done;
                            item.Summary = BuildResultSummary(result) + (ThinkingEnabled ? " (агент)" : "");
                            FillItemVerdict(item, result);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        item.State = BatchItemState.Cancelled;
                        item.Summary = "отменено";
                        break;
                    }
                    catch (Exception ex)
                    {
                        Util.WriteLog(ex, $"BatchAiAnalysis: {item.Machine} {item.Date:dd.MM.yyyy}");
                        item.State = BatchItemState.Error;
                        item.Summary = $"ошибка: {ex.Message}";
                        item.Explanation = ex.ToString();
                    }

                    done++;
                    ProcessedCount = done;
                    OverallProgress = (double)done / total;
                }

                StatusText = ct.IsCancellationRequested
                    ? $"Остановлено. Обработано {done}/{total}"
                    : $"Готово. Обработано {done}/{total}";
            }
            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private static string BuildResultSummary(AiAnalysisResult r)
        {
            var verdict = r.RequiresReview ? "требует проверки" : "ок";
            return $"{verdict} ({r.Confidence * 100:F0}%)";
        }

        /// <summary> Прогресс ИИ-анализа в строку пункта (мысли/очередь/инструменты). </summary>
        private static Progress<AiProgress> ItemProgress(BatchItem item)
        {
            var thinkingStarted = false;
            return new Progress<AiProgress>(update =>
            {
                if (update.Kind == AiProgressKind.Queue)
                {
                    item.ThinkingThoughts = update.Text;
                    thinkingStarted = false;
                }
                else if (update.Kind == AiProgressKind.Tool)
                {
                    item.ThinkingThoughts += (item.ThinkingThoughts.Length > 0 ? "\n" : "")
                        + "⚙ " + update.Text + "\n";
                    thinkingStarted = true;
                }
                else
                {
                    item.ThinkingThoughts = thinkingStarted
                        ? item.ThinkingThoughts + update.Text
                        : update.Text;
                    thinkingStarted = true;
                }
            });
        }

        private static void FillItemVerdict(BatchItem item, AiAnalysisResult result)
        {
            item.Explanation = result.Explanation;
            item.ShiftReportNote = result.ShiftReportIssues is { Length: > 0 } issues
                ? "Отчёт мастера: " + string.Join("; ", issues)
                : "";
            item.ShiftCausedEscalation = result.EscalatedByShiftReport;
            item.Confidence = result.Confidence;
            item.RequiresReview = result.RequiresReview;
        }

        /// <summary>
        /// Автозакрытие одного суткостанка через ту же ИИ-логику, что суточный
        /// анализ аналитика («Призвать ИИ к ответу»): отсутствие записей само по
        /// себе ок, решают отчёты мастера. Вердикт ок — закрываем автоматически
        /// (IsChecked + DayReview с вердиктом ИИ); требует проверки — на ручной
        /// разбор. Содержимое отчётов не трогаем.
        /// </summary>
        private async Task AutoCloseEmptyItemAsync(
            BatchItem item, IReadOnlyCollection<Part> parts, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                item.State = BatchItemState.Cancelled;
                item.Summary = "отменено";
                return;
            }
            if (parts.Count > 0)
            {
                item.State = BatchItemState.Skipped;
                item.Summary = $"есть записи ({parts.Count}) — пропуск";
                return;
            }

            var result = await _aiClient.AnalyzeAsync(
                item.Machine, item.Date, parts,
                thinkingProgress: ItemProgress(item),
                ct: ct);

            if (ct.IsCancellationRequested)
            {
                item.State = BatchItemState.Cancelled;
                item.Summary = "отменено";
                return;
            }
            if (result.HasError)
            {
                item.State = BatchItemState.Error;
                item.Summary = $"ошибка: {result.Error}";
                item.Explanation = result.Error;
                return;
            }

            FillItemVerdict(item, result);
            if (result.RequiresReview)
            {
                item.State = BatchItemState.Done;
                item.Summary = BuildResultSummary(result) + " — вручную";
                return;
            }

            // ИИ ок: закрываем. Отчёты технически обязаны существовать —
            // IsChecked ставить некуда без строк cnc_shifts.
            var day = Database.ReadShiftInfo(new ShiftInfo(item.Date, ShiftType.Day, item.Machine));
            var night = Database.ReadShiftInfo(new ShiftInfo(item.Date, ShiftType.Night, item.Machine));
            if (!day.IsOk || !night.IsOk)
            {
                item.State = BatchItemState.Error;
                item.Summary = $"не удалось прочитать отчёты: {(!day.IsOk ? day.Error : night.Error)}";
                return;
            }
            if (day.Value is not { Count: 1 } || night.Value is not { Count: 1 })
            {
                item.State = BatchItemState.Skipped;
                item.Summary = "нет обоих отчётов мастера — пропуск";
                return;
            }

            day.Value[0].IsChecked = true;
            night.Value[0].IsChecked = true;
            var writeDay = Database.WriteShiftInfo(day.Value[0]);
            var writeNight = Database.WriteShiftInfo(night.Value[0]);
            if (!writeDay.IsOk || !writeNight.IsOk)
            {
                item.State = BatchItemState.Error;
                item.Summary = $"не удалось закрыть: {(!writeDay.IsOk ? writeDay.Error : writeNight.Error)}";
                return;
            }

            var review = new DayReview(
                item.Machine, item.Date, Environment.UserName + "+ИИ",
                AnalystDecision.Ok, true,
                "Автозакрытие: записей нет");
            review.AiResult = result;
            review.AiThinkingEnabled = ThinkingEnabled;
            review.AiModelVersion = AppSettings.AiModel;
            // Тот же аудиторский тег, что AppendAiAuditTag ставит в ручном accept-пути.
            var agentMark = result.PromptVersion?.EndsWith("@agent") == true ? " агент" : "";
            review.AiFeedback = $"[ИИ {AppSettings.AiModel}/{result.PromptVersion}{agentMark}]";
            var saved = await Database.SaveDayReviewAsync(review);
            if (saved.Status != DbResult.Ok)
            {
                item.State = BatchItemState.Error;
                item.Summary = $"не удалось записать ревью: {saved.Error}";
                return;
            }

            item.State = BatchItemState.Done;
            item.Summary = "закрыт (пусто, ИИ ок)";
        }
    }

    internal enum BatchItemState
    {
        Pending,
        Running,
        Done,
        Error,
        Skipped,
        Cancelled,
    }

    internal class BatchItem : ViewModel
    {
        public int DayReviewId { get; set; }

        public DateTime Date { get; set; }

        public string Machine { get; set; } = string.Empty;

        private BatchItemState _State;
        public BatchItemState State
        {
            get => _State;
            set
            {
                if (Set(ref _State, value))
                {
                    OnPropertyChanged(nameof(StateBrush));
                    OnPropertyChanged(nameof(Header));
                    if (value == BatchItemState.Running)
                    {
                        IsExpanded = true;
                    }
                }
            }
        }

        private bool _IsExpanded;
        public bool IsExpanded
        {
            get => _IsExpanded;
            set => Set(ref _IsExpanded, value);
        }

        private string _Summary = "ожидание";
        public string Summary
        {
            get => _Summary;
            set
            {
                if (Set(ref _Summary, value))
                {
                    OnPropertyChanged(nameof(Header));
                }
            }
        }

        private string _ThinkingThoughts = "";
        public string ThinkingThoughts
        {
            get => _ThinkingThoughts;
            set => Set(ref _ThinkingThoughts, value);
        }

        private string _Explanation = "";
        public string Explanation
        {
            get => _Explanation;
            set => Set(ref _Explanation, value);
        }

        private string _ShiftReportNote = "";
        /// <summary> Вопросы к суточному отчёту мастера — строкой под объяснением. </summary>
        public string ShiftReportNote
        {
            get => _ShiftReportNote;
            set
            {
                if (Set(ref _ShiftReportNote, value))
                    OnPropertyChanged(nameof(HasShiftReportNote));
            }
        }

        public bool HasShiftReportNote => !string.IsNullOrWhiteSpace(ShiftReportNote);

        private bool _ShiftCausedEscalation;
        /// <summary> Строка отчёта подсвечивается: эскалация вызвана суточным отчётом. </summary>
        public bool ShiftCausedEscalation
        {
            get => _ShiftCausedEscalation;
            set => Set(ref _ShiftCausedEscalation, value);
        }

        public double Confidence { get; set; }

        public bool RequiresReview { get; set; }

        public string Header => $"{Date:dd.MM.yyyy}  ·  {Machine}  ·  {Summary}";

        public System.Windows.Media.Brush StateBrush =>
            State switch
            {
                BatchItemState.Pending => Infrastructure.ThemeBrushes.Text3,
                BatchItemState.Running => Infrastructure.ThemeBrushes.Accent,
                BatchItemState.Done => Infrastructure.ThemeBrushes.Ok,
                BatchItemState.Error => Infrastructure.ThemeBrushes.Alert,
                BatchItemState.Skipped => Infrastructure.ThemeBrushes.Disabled,
                BatchItemState.Cancelled => Infrastructure.ThemeBrushes.Warn,
                _ => Infrastructure.ThemeBrushes.Text,
            };
    }
}
