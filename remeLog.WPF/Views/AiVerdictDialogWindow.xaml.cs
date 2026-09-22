using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace remeLog.Views
{
    /// <summary> Действие, выбранное аналитиком в диалоге вердикта ИИ. </summary>
    public enum AiVerdictAction
    {
        None,
        /// <summary> Вердикт OK подтверждён: исключения + закрытие дня («Проверено техотделом»). </summary>
        ConfirmOk,
        /// <summary> День отправлен на дальнейшую проверку (исключения применяются тоже). </summary>
        Escalate,
        /// <summary> Несогласие с вердиктом OK: день закрывается как OK, отзыв обязателен. </summary>
        OppositeOk,
        /// <summary> Несогласие с вердиктом «требует проверки»: день эскалируется, отзыв обязателен. </summary>
        OppositeEscalate,
        /// <summary> Применить только отмеченные исключения, статус дня не менять. </summary>
        ExcludesOnly,
    }

    /// <summary>
    /// Строка кандидата на исключение из расчёта К1 в диалоге вердикта ИИ.
    /// </summary>
    public class AiVerdictCandidateItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary> Строка суток после сопоставления (null — предложение ни к чему не привязалось). </summary>
        public Part? Part { get; }

        public string DisplayText { get; }

        /// <summary> Привязалось ли предложение к строке суток. Непривязанные чекбоксы заблокированы. </summary>
        public bool IsMatched => Part != null;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public AiVerdictCandidateItem(Part? part, string displayText, bool isSelected)
        {
            Part = part;
            DisplayText = displayText;
            _isSelected = isSelected;
        }
    }

    /// <summary>
    /// Диалог вердикта ИИ по сутко-станку (этап 2 «Тестовый запуск»): ИИ первичен,
    /// аналитик подтверждает явным действием. Показывает оценку + объяснение, список
    /// кандидатов на исключение из К1 с чекбоксами и кнопки действий — подтверждение,
    /// отправка на дальнейшую проверку или только исключения. Кнопки сами ничего
    /// не пишут — VM читает <see cref="ChosenAction"/> и выполняет accept-путь.
    /// </summary>
    public partial class AiVerdictDialogWindow : Window
    {
        public static new readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata("Вердикт ИИ"));

        public static readonly DependencyProperty VerdictTitleProperty =
            DependencyProperty.Register(nameof(VerdictTitle), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty VerdictDetailsProperty =
            DependencyProperty.Register(nameof(VerdictDetails), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty VerdictBrushProperty =
            DependencyProperty.Register(nameof(VerdictBrush), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata("#212121"));

        public static readonly DependencyProperty ExplanationProperty =
            DependencyProperty.Register(nameof(Explanation), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SignalsProperty =
            DependencyProperty.Register(nameof(Signals), typeof(ObservableCollection<string>), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(null));

        public static readonly DependencyProperty HasSignalsProperty =
            DependencyProperty.Register(nameof(HasSignals), typeof(bool), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ShiftReportIssuesProperty =
            DependencyProperty.Register(nameof(ShiftReportIssues), typeof(ObservableCollection<string>), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(null));

        public static readonly DependencyProperty HasShiftReportIssuesProperty =
            DependencyProperty.Register(nameof(HasShiftReportIssues), typeof(bool), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ShiftReportSummaryProperty =
            DependencyProperty.Register(nameof(ShiftReportSummary), typeof(ObservableCollection<string>), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(null));

        public static readonly DependencyProperty HasShiftReportSummaryProperty =
            DependencyProperty.Register(nameof(HasShiftReportSummary), typeof(bool), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ShiftCausedEscalationProperty =
            DependencyProperty.Register(nameof(ShiftCausedEscalation), typeof(bool), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty DataVerdictTextProperty =
            DependencyProperty.Register(nameof(DataVerdictText), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty DataVerdictBrushProperty =
            DependencyProperty.Register(nameof(DataVerdictBrush), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata("#212121"));

        public static readonly DependencyProperty ShiftVerdictTextProperty =
            DependencyProperty.Register(nameof(ShiftVerdictText), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ShiftVerdictBrushProperty =
            DependencyProperty.Register(nameof(ShiftVerdictBrush), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata("#212121"));

        public static readonly DependencyProperty CandidatesProperty =
            DependencyProperty.Register(nameof(Candidates), typeof(ObservableCollection<AiVerdictCandidateItem>), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(null));

        public static readonly DependencyProperty HasCandidatesProperty =
            DependencyProperty.Register(nameof(HasCandidates), typeof(bool), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty UnmatchedTextProperty =
            DependencyProperty.Register(nameof(UnmatchedText), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty HasUnmatchedProperty =
            DependencyProperty.Register(nameof(HasUnmatched), typeof(bool), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ShowExcludesOnlyProperty =
            DependencyProperty.Register(nameof(ShowExcludesOnly), typeof(bool), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty FlaggedPreviewTextProperty =
            DependencyProperty.Register(nameof(FlaggedPreviewText), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty HasFlaggedPreviewProperty =
            DependencyProperty.Register(nameof(HasFlaggedPreview), typeof(bool), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty PrimaryActionTextProperty =
            DependencyProperty.Register(nameof(PrimaryActionText), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty PrimaryActionTooltipProperty =
            DependencyProperty.Register(nameof(PrimaryActionTooltip), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty PrimaryActionVisibleProperty =
            DependencyProperty.Register(nameof(PrimaryActionVisible), typeof(bool), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty OppositeActionTextProperty =
            DependencyProperty.Register(nameof(OppositeActionText), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty OppositeActionTooltipProperty =
            DependencyProperty.Register(nameof(OppositeActionTooltip), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty OppositeEnabledProperty =
            DependencyProperty.Register(nameof(OppositeEnabled), typeof(bool), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty DayStatusHintProperty =
            DependencyProperty.Register(nameof(DayStatusHint), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty HasDayStatusHintProperty =
            DependencyProperty.Register(nameof(HasDayStatusHint), typeof(bool), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty FeedbackProperty =
            DependencyProperty.Register(nameof(Feedback), typeof(string), typeof(AiVerdictDialogWindow),
                new PropertyMetadata(string.Empty, OnFeedbackChanged));

        private static void OnFeedbackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((AiVerdictDialogWindow)d).UpdateOppositeState();

        /// <summary> Несогласие доступно только с отзывом — он обязателен и уйдёт в обучение. </summary>
        private void UpdateOppositeState() =>
            OppositeEnabled = !string.IsNullOrWhiteSpace(Feedback);

        public new string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary> Крупный заголовок вердикта: «Всё в порядке» / «Требует проверки». </summary>
        public string VerdictTitle
        {
            get => (string)GetValue(VerdictTitleProperty);
            set => SetValue(VerdictTitleProperty, value);
        }

        /// <summary> Строка-подзаголовок: уверенность и контекст разбора. </summary>
        public string VerdictDetails
        {
            get => (string)GetValue(VerdictDetailsProperty);
            set => SetValue(VerdictDetailsProperty, value);
        }

        /// <summary> Цвет заголовка вердикта (зелёный / оранжевый). </summary>
        public string VerdictBrush
        {
            get => (string)GetValue(VerdictBrushProperty);
            set => SetValue(VerdictBrushProperty, value);
        }

        public string Explanation
        {
            get => (string)GetValue(ExplanationProperty);
            set => SetValue(ExplanationProperty, value);
        }

        public ObservableCollection<string> Signals
        {
            get => (ObservableCollection<string>)GetValue(SignalsProperty);
            set => SetValue(SignalsProperty, value);
        }

        public bool HasSignals
        {
            get => (bool)GetValue(HasSignalsProperty);
            set => SetValue(HasSignalsProperty, value);
        }

        /// <summary> Вопросы к суточному отчёту мастера — отдельный блок, без действий. </summary>
        public ObservableCollection<string> ShiftReportIssues
        {
            get => (ObservableCollection<string>)GetValue(ShiftReportIssuesProperty);
            set => SetValue(ShiftReportIssuesProperty, value);
        }

        public bool HasShiftReportIssues
        {
            get => (bool)GetValue(HasShiftReportIssuesProperty);
            set => SetValue(HasShiftReportIssuesProperty, value);
        }

        /// <summary> Построчная сводка проверки отчёта мастера — видна всегда. </summary>
        public ObservableCollection<string> ShiftReportSummary
        {
            get => (ObservableCollection<string>)GetValue(ShiftReportSummaryProperty);
            set => SetValue(ShiftReportSummaryProperty, value);
        }

        public bool HasShiftReportSummary
        {
            get => (bool)GetValue(HasShiftReportSummaryProperty);
            set => SetValue(HasShiftReportSummaryProperty, value);
        }

        /// <summary> Рамка отчёта мастера желтеет: эскалация вызвана суточным отчётом. </summary>
        public bool ShiftCausedEscalation
        {
            get => (bool)GetValue(ShiftCausedEscalationProperty);
            set => SetValue(ShiftCausedEscalationProperty, value);
        }

        public string DataVerdictText
        {
            get => (string)GetValue(DataVerdictTextProperty);
            set => SetValue(DataVerdictTextProperty, value);
        }

        public string DataVerdictBrush
        {
            get => (string)GetValue(DataVerdictBrushProperty);
            set => SetValue(DataVerdictBrushProperty, value);
        }

        /// <summary> Общий вывод по суточному отчёту для заголовка секции. </summary>
        public string ShiftVerdictText
        {
            get => (string)GetValue(ShiftVerdictTextProperty);
            set => SetValue(ShiftVerdictTextProperty, value);
        }

        public string ShiftVerdictBrush
        {
            get => (string)GetValue(ShiftVerdictBrushProperty);
            set => SetValue(ShiftVerdictBrushProperty, value);
        }

        public ObservableCollection<AiVerdictCandidateItem> Candidates
        {
            get => (ObservableCollection<AiVerdictCandidateItem>)GetValue(CandidatesProperty);
            set => SetValue(CandidatesProperty, value);
        }

        public bool HasCandidates
        {
            get => (bool)GetValue(HasCandidatesProperty);
            set => SetValue(HasCandidatesProperty, value);
        }

        public string UnmatchedText
        {
            get => (string)GetValue(UnmatchedTextProperty);
            set => SetValue(UnmatchedTextProperty, value);
        }

        public bool HasUnmatched
        {
            get => (bool)GetValue(HasUnmatchedProperty);
            set => SetValue(HasUnmatchedProperty, value);
        }

        /// <summary> Показывать ли кнопку «Только исключить из К1» (есть привязанные кандидаты). </summary>
        public bool ShowExcludesOnly
        {
            get => (bool)GetValue(ShowExcludesOnlyProperty);
            set => SetValue(ShowExcludesOnlyProperty, value);
        }

        /// <summary> Строки, которые ИИ отметит проблемными при эскалации. </summary>
        public string FlaggedPreviewText
        {
            get => (string)GetValue(FlaggedPreviewTextProperty);
            set => SetValue(FlaggedPreviewTextProperty, value);
        }

        public bool HasFlaggedPreview
        {
            get => (bool)GetValue(HasFlaggedPreviewProperty);
            set => SetValue(HasFlaggedPreviewProperty, value);
        }

        public string PrimaryActionText
        {
            get => (string)GetValue(PrimaryActionTextProperty);
            set => SetValue(PrimaryActionTextProperty, value);
        }

        public string PrimaryActionTooltip
        {
            get => (string)GetValue(PrimaryActionTooltipProperty);
            set => SetValue(PrimaryActionTooltipProperty, value);
        }

        public bool PrimaryActionVisible
        {
            get => (bool)GetValue(PrimaryActionVisibleProperty);
            set => SetValue(PrimaryActionVisibleProperty, value);
        }

        public string OppositeActionText
        {
            get => (string)GetValue(OppositeActionTextProperty);
            set => SetValue(OppositeActionTextProperty, value);
        }

        public string OppositeActionTooltip
        {
            get => (string)GetValue(OppositeActionTooltipProperty);
            set => SetValue(OppositeActionTooltipProperty, value);
        }

        public bool OppositeEnabled
        {
            get => (bool)GetValue(OppositeEnabledProperty);
            set => SetValue(OppositeEnabledProperty, value);
        }

        public string DayStatusHint
        {
            get => (string)GetValue(DayStatusHintProperty);
            set => SetValue(DayStatusHintProperty, value);
        }

        public bool HasDayStatusHint
        {
            get => (bool)GetValue(HasDayStatusHintProperty);
            set => SetValue(HasDayStatusHintProperty, value);
        }

        public string Feedback
        {
            get => (string)GetValue(FeedbackProperty);
            set => SetValue(FeedbackProperty, value);
        }

        /// <summary> Выбранное действие (имеет смысл только при DialogResult == true). </summary>
        public AiVerdictAction ChosenAction { get; private set; } = AiVerdictAction.None;

        private readonly bool _requiresReview;

        /// <summary> Выбранные привязанные строки для исключения из К1. </summary>
        public IReadOnlyList<Part> AcceptedParts =>
            Candidates.Where(c => c.IsMatched && c.IsSelected && c.Part != null).Select(c => c.Part!).ToList();

        /// <param name="matched">Сопоставленные кандидаты: строка суток + причина ИИ.</param>
        /// <param name="unmatched">Тексты предложений, не привязавшихся ни к одной строке.</param>
        /// <param name="canChangeDayStatus">Результат ИИ сохранён в БД — статус дня менять можно.</param>
        /// <param name="alreadyReviewedHint">Текст предупреждения о перезаписи существующей проверки ("" — нет).</param>
        /// <param name="flaggedPreview">Строки, которые ИИ отметит проблемными при эскалации.</param>
        /// <param name="shiftReportIssues">Вопросы к суточному отчёту мастера — отдельный блок, без действий.</param>
        /// <param name="shiftReportSummary">Построчная сводка проверки отчёта — видна всегда, даже без вопросов.</param>
        /// <param name="shiftCausedEscalation">Рамка отчёта желтеет: причина эскалации — суточный отчёт.</param>
        /// <param name="dataCausedEscalation">Вердикт заголовка данных: причина эскалации — записи.</param>
        public AiVerdictDialogWindow(
            string machine,
            System.DateTime shiftDate,
            bool requiresReview,
            double confidence,
            string explanation,
            IReadOnlyList<string> signals,
            IReadOnlyList<(Part Part, string Reason)> matched,
            IReadOnlyList<string> unmatched,
            bool canChangeDayStatus,
            string alreadyReviewedHint = "",
            IReadOnlyList<string>? flaggedPreview = null,
            IReadOnlyList<string>? shiftReportIssues = null,
            IReadOnlyList<string>? shiftReportSummary = null,
            bool shiftCausedEscalation = false,
            bool dataCausedEscalation = false)
        {
            InitializeComponent();

            _requiresReview = requiresReview;

            Title = $"Вердикт ИИ за {shiftDate:dd.MM.yyyy} — {machine}";
            VerdictTitle = requiresReview ? "Требует проверки" : "Всё в порядке";
            VerdictBrush = requiresReview ? "#E65100" : "#2E7D32";
            VerdictDetails = $"Уверенность {confidence:0%} · {machine} · {shiftDate:dd.MM.yyyy}";
            Explanation = string.IsNullOrWhiteSpace(explanation) ? "Без объяснения." : explanation;
            Signals = new ObservableCollection<string>(signals ?? Array.Empty<string>());
            HasSignals = Signals.Count > 0;
            var shiftIssues = shiftReportIssues ?? Array.Empty<string>();
            ShiftReportIssues = new ObservableCollection<string>(shiftIssues);
            HasShiftReportIssues = ShiftReportIssues.Count > 0;
            var shiftSummary = shiftReportSummary ?? Array.Empty<string>();
            ShiftReportSummary = new ObservableCollection<string>(shiftSummary);
            HasShiftReportSummary = ShiftReportSummary.Count > 0;
            ShiftCausedEscalation = shiftCausedEscalation;

            // Общие выводы в заголовках блоков — в палитре вердикта (оранжевый/зелёный).
            DataVerdictText = dataCausedEscalation ? "требует проверки" : "в порядке";
            DataVerdictBrush = dataCausedEscalation ? "#E65100" : "#2E7D32";
            ShiftVerdictText = shiftCausedEscalation ? "требует проверки" : "в порядке";
            ShiftVerdictBrush = shiftCausedEscalation ? "#E65100" : "#2E7D32";

            var items = new ObservableCollection<AiVerdictCandidateItem>();
            foreach (var (part, reason) in matched)
            {
                var alreadyExcluded = part.ExcludeFromReports;
                items.Add(new AiVerdictCandidateItem(part,
                    $"{part.PartName} | М/Л: {part.Order} | Уст.{part.Setup} — {reason}" +
                    (alreadyExcluded ? " (уже исключена)" : string.Empty),
                    isSelected: !alreadyExcluded));
            }
            foreach (var text in unmatched)
                items.Add(new AiVerdictCandidateItem(null, $"{text} (строка не найдена)", isSelected: false));
            Candidates = items;
            HasCandidates = items.Count > 0;
            HasUnmatched = unmatched.Count > 0;
            UnmatchedText = HasUnmatched
                ? "Не привязались к строкам суток: " + string.Join("; ", unmatched)
                : string.Empty;
            ShowExcludesOnly = matched.Count > 0;

            PrimaryActionVisible = canChangeDayStatus;
            if (!requiresReview)
            {
                PrimaryActionText = "Подтвердить — всё в порядке";
                PrimaryActionTooltip = "Применит отмеченные исключения и закроет день (Проверено техотделом)";
                OppositeActionText = "Не согласен — эскалировать";
                OppositeActionTooltip = "Применит отмеченные исключения и отправит день на дальнейшую проверку (потребуется отзыв)";
            }
            else
            {
                PrimaryActionText = "Эскалировать на дальнейшую проверку";
                PrimaryActionTooltip = "Применит отмеченные исключения и отправит день на дальнейшую проверку";
                OppositeActionText = "Не согласен — всё в порядке";
                OppositeActionTooltip = "Применит отмеченные исключения и закроет день (потребуется отзыв)";
            }

            var flagged = flaggedPreview ?? Array.Empty<string>();
            HasFlaggedPreview = canChangeDayStatus && requiresReview && flagged.Count > 0;
            FlaggedPreviewText = HasFlaggedPreview
                ? "ИИ отметит как проблемные:" + Environment.NewLine + string.Join(Environment.NewLine, flagged)
                : string.Empty;

            var noFlaggedHint = canChangeDayStatus && requiresReview && flagged.Count == 0;
            HasDayStatusHint = !canChangeDayStatus
                || !string.IsNullOrWhiteSpace(alreadyReviewedHint)
                || noFlaggedHint;
            DayStatusHint = !canChangeDayStatus
                ? "Результат ИИ не сохранён в БД — статус дня не меняется, доступны только исключения из К1."
                : noFlaggedHint && string.IsNullOrWhiteSpace(alreadyReviewedHint)
                    ? "ИИ не указал конкретные строки — проблемные отметьте вручную через контекстное меню."
                    : alreadyReviewedHint;

            Feedback = string.Empty;
            UpdateOppositeState();
            DataContext = this;
        }

        private void Primary_Click(object sender, RoutedEventArgs e)
        {
            ChosenAction = _requiresReview ? AiVerdictAction.Escalate : AiVerdictAction.ConfirmOk;
            DialogResult = true;
            Close();
        }

        private void Opposite_Click(object sender, RoutedEventArgs e)
        {
            if (!OppositeEnabled) return;
            ChosenAction = _requiresReview ? AiVerdictAction.OppositeOk : AiVerdictAction.OppositeEscalate;
            DialogResult = true;
            Close();
        }

        private void ExcludesOnly_Click(object sender, RoutedEventArgs e)
        {
            ChosenAction = AiVerdictAction.ExcludesOnly;
            DialogResult = true;
            Close();
        }
    }
}
