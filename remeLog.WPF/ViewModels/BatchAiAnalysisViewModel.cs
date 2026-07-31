using libeLog;
using libeLog.Base;
using remeLog.Infrastructure;
using remeLog.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace remeLog.ViewModels
{
    internal class BatchAiAnalysisViewModel : ViewModel
    {
        private readonly AiServiceClient _aiClient = new();
        private CancellationTokenSource? _cts;

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

        private bool _ThinkingEnabled;
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

        public BatchAiAnalysisViewModel()
        {
            _ThinkingEnabled = AppSettings.Instance.AiThinkingEnabled;
            StartCommand = new LambdaCommand(OnStartExecuted, _ => !IsRunning);
            CancelCommand = new LambdaCommand(OnCancelExecuted, _ => IsRunning);
            RefreshCommand = new LambdaCommand(OnRefreshExecuted, _ => !IsRunning);
            _ = RefreshListAsync();
        }

        private async Task RefreshListAsync()
        {
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
                    item.Summary = "анализ...";
                    item.ThinkingThoughts = "";
                    StatusText = $"Анализ {done + 1}/{total}: {item.Machine} {item.Date:dd.MM.yyyy}";

                    try
                    {
                        var parts = await Database.ReadPartsByShiftDateAndMachine(
                            item.Date, item.Date, item.Machine, ct);

                        if (parts.Count == 0)
                        {
                            item.State = BatchItemState.Error;
                            item.Summary = "нет записей за сутки";
                            done++;
                            ProcessedCount = done;
                            OverallProgress = (double)done / total;
                            continue;
                        }

                        var progress = new Progress<string>(thought =>
                        {
                            item.ThinkingThoughts += thought;
                        });

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
                            await Database.SaveAiAnalysisAsync(
                                item.DayReviewId, result, AppSettings.AiModel, ThinkingEnabled);

                            item.State = BatchItemState.Done;
                            item.Summary = BuildResultSummary(result);
                            item.Explanation = result.Explanation;
                            item.Confidence = result.Confidence;
                            item.RequiresReview = result.RequiresReview;
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

        public double Confidence { get; set; }

        public bool RequiresReview { get; set; }

        public string Header => $"{Date:dd.MM.yyyy}  ·  {Machine}  ·  {Summary}";

        public System.Windows.Media.Brush StateBrush =>
            State switch
            {
                BatchItemState.Pending => System.Windows.Media.Brushes.Gray,
                BatchItemState.Running => System.Windows.Media.Brushes.DodgerBlue,
                BatchItemState.Done => System.Windows.Media.Brushes.ForestGreen,
                BatchItemState.Error => System.Windows.Media.Brushes.OrangeRed,
                BatchItemState.Skipped => System.Windows.Media.Brushes.DarkGray,
                BatchItemState.Cancelled => System.Windows.Media.Brushes.DarkOrange,
                _ => System.Windows.Media.Brushes.Black,
            };
    }
}
