using eLog.Infrastructure;
using eLog.Models;
using libeLog;
using libeLog.Base;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace eLog.ViewModels
{
    /// <summary> Одно сообщение в окне чата. </summary>
    public class ChatMessageItem : ViewModel
    {
        private string _Text = "";
        private string _Activity = "";
        private bool _IsThinking;
        private bool _IsError;
        private bool _HasSources;
        private string _SourcesHeader = "";

        public bool IsUser { get; set; }
        public DateTime Time { get; set; } = DateTime.Now;

        public string Text
        {
            get => _Text;
            set => Set(ref _Text, value);
        }

        /// <summary> Текущий шаг генерации («Думаю…», «Очередь…», «⚙ смотрю смену…»). </summary>
        public string Activity
        {
            get => _Activity;
            set => Set(ref _Activity, value);
        }

        /// <summary> Ответ ещё не начал печататься — показываем индикатор вместо пустоты. </summary>
        public bool IsThinking
        {
            get => _IsThinking;
            set => Set(ref _IsThinking, value);
        }

        /// <summary> В облаке текст ошибки, а не ответ. </summary>
        public bool IsError
        {
            get => _IsError;
            set => Set(ref _IsError, value);
        }

        /// <summary> Источники ответа — отдельным сворачиваемым блоком, не в тексте. </summary>
        public ObservableCollection<ChatSourceDto> Sources { get; } = new();

        public bool HasSources
        {
            get => _HasSources;
            private set => Set(ref _HasSources, value);
        }

        public string SourcesHeader
        {
            get => _SourcesHeader;
            private set => Set(ref _SourcesHeader, value);
        }

        public void SetSources(IEnumerable<ChatSourceDto> sources)
        {
            Sources.Clear();
            foreach (var s in sources)
                Sources.Add(new ChatSourceDto
                {
                    Title = string.IsNullOrWhiteSpace(s.Title) ? s.Url : s.Title,
                    Url = s.Url ?? "",
                    Snippet = s.Snippet ?? "",
                });
            HasSources = Sources.Count > 0;
            SourcesHeader = $"Источники ({Sources.Count})";
        }
    }

    /// <summary>
    /// Производственный ИИ-чат (кнопка на главном окне). Ответы стримятся
    /// токенами в плейсхолдер ассистента; отмена — через CTS. История последних
    /// 20 сообщений и снимок смены уходят каждым запросом (сервер stateless).
    /// </summary>
    internal class AiChatViewModel : ViewModel
    {
        private const int MaxHistory = 20;
        private readonly AiChatClient _client = new();
        private readonly List<ChatMessageDto> _history = new();
        private CancellationTokenSource? _cts;

        private string _Input = "";
        private bool _IsBusy;
        private string _Status = "";

        public AiChatViewModel()
        {
            SendCommand = new LambdaCommand(OnSendExecuted, CanSendExecute);
            CancelCommand = new LambdaCommand(_ => _cts?.Cancel(), _ => IsBusy);
            ClearCommand = new LambdaCommand(_ => Clear(), _ => !IsBusy && Messages.Count > 0);
            SearchInWeb = AppSettings.Instance.AiChatAllowWebSearch;
            // Приветствия статикой нет: окно дёргает GreetAsync при открытии —
            // модель сама собирает информацию об операторе инструментами.
        }

        /// <summary>
        /// Скрытый стартовый запрос при открытии диалога: в ленту падает только ответ.
        /// Модель собирает информацию об операторе инструментами и здоровается по форме,
        /// цифры эффективности не выдумывает — только факты из инструментов.
        /// </summary>
        public async Task GreetAsync()
        {
            if (IsBusy) return;
            var assistantItem = new ChatMessageItem
            {
                IsUser = false,
                Text = "",
                IsThinking = true,
                Activity = "Сканирование биометрии…",
            };
            Messages.Add(assistantItem);
            await ExecuteChatAsync(assistantItem,
                new List<ChatMessageDto> { new ChatMessageDto { Role = "user", Content = GreetingTrigger } });
        }

        // Формулировка без «Вы работаете»: смена может быть только выбрана, и
        // утверждать работу в неначатую смену запрещено (правило 13 промпта).
        // Раньше триггер требовал именно эту форму — и модель писала «вы
        // работаете на Mazak QTS350, смена День (выбрана, но не начата)».
        private const string GreetingTrigger =
            "[Автостарт] Оператор только что открыл чат. Собери информацию о нём и смене " +
            "инструментами (get_shift_info, при активной детали — get_active_part) и поздоровайся " +
            "строго по форме: «Здравствуйте, {Имя Отчество}! Станок {станок}, смена {смена} — " +
            "уточни, начата ли она. {Один факт: активная деталь и её состояние / либо сколько " +
            "записей в журнале}. Ваше обращение зафиксировано. Что Вас интересует?» " +
            "Никаких выдуманных цифр эффективности — только факты из инструментов.";

        public ObservableCollection<ChatMessageItem> Messages { get; } = new();

        public string Input
        {
            get => _Input;
            set
            {
                if (!Set(ref _Input, value)) return;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsBusy
        {
            get => _IsBusy;
            private set
            {
                if (!Set(ref _IsBusy, value)) return;
                OnPropertyChanged(nameof(CanCancel));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool CanCancel => IsBusy;

        public string Status
        {
            get => _Status;
            private set => Set(ref _Status, value);
        }

        private bool _SearchInWeb;
        /// <summary> Галка «Искать в сети» — уходит флагом AllowWebSearch. </summary>
        public bool SearchInWeb
        {
            get => _SearchInWeb;
            set => Set(ref _SearchInWeb, value);
        }

        public ICommand SendCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ClearCommand { get; }

        private bool CanSendExecute(object p) =>
            !IsBusy && !string.IsNullOrWhiteSpace(Input);

        private async void OnSendExecuted(object p)
        {
            var text = (Input ?? "").Trim();
            if (string.IsNullOrEmpty(text) || IsBusy) return;

            var userItem = new ChatMessageItem { IsUser = true, Text = text };
            Messages.Add(userItem);
            _history.Add(new ChatMessageDto { Role = "user", Content = text });
            Input = "";

            var assistantItem = new ChatMessageItem { IsUser = false, Text = "", IsThinking = true, Activity = "Думаю…" };
            Messages.Add(assistantItem);
            await ExecuteChatAsync(assistantItem,
                _history.Skip(Math.Max(0, _history.Count - MaxHistory)).ToList());
        }

        /// <summary>
        /// Общий прогон запроса (кнопка и автоприветствие): стрим токенов в облако,
        /// шаги — в Activity облака и статус, источники — отдельным блоком.
        /// </summary>
        private async Task ExecuteChatAsync(
            ChatMessageItem assistantItem, List<ChatMessageDto> sendMessages)
        {
            IsBusy = true;
            Status = assistantItem.Activity;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            var progress = new Progress<ChatProgress>(u =>
            {
                // Шаги дозора (очередь, инструменты) видны прямо в облаке ответа,
                // а не только в строке статуса внизу: пустого облака больше нет.
                if (u.Kind == ChatProgressKind.Queue || u.Kind == ChatProgressKind.Tool)
                {
                    assistantItem.Activity = u.Text;
                    Status = u.Text;
                }
                else
                {
                    if (Status.StartsWith("⚙")) Status = "";
                    assistantItem.IsThinking = false;
                    assistantItem.Text += u.Text;
                }
            });

            try
            {
                var request = new ChatRequestDto
                {
                    Messages = sendMessages,
                    Context = BuildContextSnapshot(),
                    Model = string.IsNullOrWhiteSpace(AppSettings.Instance.AiModel)
                        ? null : AppSettings.Instance.AiModel.Trim(),
                    AllowWebSearch = SearchInWeb,
                };

                var result = await _client.ChatStreamAsync(request, progress, ct);
                assistantItem.IsThinking = false;
                assistantItem.Text = result.Reply;
                assistantItem.SetSources(result.Sources);
                _history.Add(new ChatMessageDto { Role = "assistant", Content = result.Reply });
                var marks = new List<string>();
                if (result.ToolRounds > 0) marks.Add($"смотрел смену ({result.ToolRounds})");
                if (result.WebUsed) marks.Add("искал в сети");
                Status = marks.Count > 0 ? "Дозор " + string.Join(", ", marks) + "." : "";
            }
            catch (OperationCanceledException)
            {
                assistantItem.IsThinking = false;
                if (string.IsNullOrWhiteSpace(assistantItem.Text))
                    Messages.Remove(assistantItem);
                Status = "Остановлено.";
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is InvalidOperationException)
            {
                assistantItem.IsThinking = false;
                assistantItem.IsError = true;
                assistantItem.Text = $"Не получилось получить ответ: {ex.Message}";
                Status = "";
                await Infrastructure.Extensions.Util.WriteLogAsync(ex);
            }
            catch (Exception ex)
            {
                // Любое другое исключение (обрыв сокета, таймаут, ошибка разбора
                // потока) раньше уходило наружу: индикатор «⚙ смотрю журнал…»
                // оставался висеть после ответа, а облако ответа — с
                // «размышляю…». Сбрасываем оба и показываем текст ошибки.
                assistantItem.IsThinking = false;
                if (string.IsNullOrWhiteSpace(assistantItem.Text))
                    assistantItem.Text = "Связь с ИИ-помощником прервалась.";
                assistantItem.IsError = true;
                Status = "";
                await Infrastructure.Extensions.Util.WriteLogAsync(ex);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                // Незакрытый шаг дозора («⚙ смотрю смену…», «Очередь к ИИ…») не должен
                // висеть после ответа: это последний прогон, всё, что началось, кончено.
                if (Status.StartsWith("⚙", StringComparison.Ordinal)
                    || Status.StartsWith("Очередь", StringComparison.Ordinal))
                    Status = "";
                IsBusy = false;
            }
        }

        private void Clear()
        {
            Messages.Clear();
            _history.Clear();
            Status = "";
            _ = GreetAsync();
        }

        /// <summary>
        /// Снимок «происходящего»: станок, смена, оператор, активная деталь
        /// (первая незавершённая, иначе верхняя) + её последние простои.
        /// </summary>
        /// <summary> Дата смены по её началу: ночная смена, которая идёт после полуночи
    /// (до 09:00), относится к предыдущим суткам — так же, как пишет в базу сам
    /// eLog (Infrastructure/Extensions/Parts.cs: ночная смена, завершённая раньше
    /// 09:00, dated предыдущим днём). Пример: закончил в 3 часа 21-го — смена
    /// началась 20-го в 19:00, детали относятся к 20-му. </summary>
    /// <summary>Час, после которого ночная смена считается сегодняшней (09:00).
    /// Держим в одном числе с записью в базу — Infrastructure/Extensions/Parts.cs.</summary>
    private const int ShiftDateNightCutoverHour = 9;

    private static string ShiftDateOfToday(string? shift)
    {
        var now = DateTime.Now;
        var date = now.Hour < ShiftDateNightCutoverHour
            && string.Equals(shift, Text.NightShift, StringComparison.OrdinalIgnoreCase)
            ? now.Date.AddDays(-1)
            : now.Date;
        return date.ToString("yyyy-MM-dd");
    }

    private static ElogChatContextDto BuildContextSnapshot()
        {
            var settings = AppSettings.Instance;
            var parts = settings.Parts;
            Part? active = null;
            if (parts != null)
            {
                active = parts.FirstOrDefault(p => p.IsFinished == Part.State.InProgress)
                    ?? parts.FirstOrDefault();
            }

            return new ElogChatContextDto
            {
                Machine = settings.Machine?.Name ?? "",
                Shift = settings.CurrentShift ?? "",
                OperatorName = settings.CurrentOperator?.DisplayName ?? "",
                ShiftStarted = settings.IsShiftStarted,
                PartsCount = parts?.Count ?? 0,
                // Типы простоя, которые оператор отмечает у себя, — из клиента:
                // список захардкожен в eLog (Text.DownTimes), и он НЕ совпадает
                // со справочником причин простоя целой смены (cnc_downtime_reasons),
                // который выбирает мастер. Единственный источник — здесь.
                OperatorDowntimeTypes = new List<string>
                {
                    Text.DownTimes.CreateNcProgram,
                    Text.DownTimes.Maintenance,
                    Text.DownTimes.ToolSearching,
                    Text.DownTimes.ToolChanging,
                    Text.DownTimes.Mentoring,
                    Text.DownTimes.ContactingDepartments,
                    Text.DownTimes.FixtureMaking,
                    Text.DownTimes.HardwareFailure,
                },
                ActivePart = active == null ? null : new ChatActivePartDto
                {
                    Name = active.Name ?? "",
                    Number = active.Number ?? "",
                    Order = active.Order ?? "",
                    Setup = active.Setup,
                    StartSetupTime = active.StartSetupTime == DateTime.MinValue
                        ? "" : active.StartSetupTime.ToString("dd.MM.yyyy HH:mm"),
                    OperatorComment = active.OperatorComments ?? "",
                    Downtimes = active.DownTimes == null ? new() : active.DownTimes
                        .TakeLast(6)
                        .Select(d => new ChatDowntimeDto
                        {
                            Name = d.Name ?? "",
                            Start = d.StartTimeText ?? "",
                            End = d.EndTimeText ?? "",
                            Comment = d.Comment ?? "",
                        }).ToList(),
                },
            };
        }
    }
}
