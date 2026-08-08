using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace remeLog.Views
{
    /// <summary>
    /// Диалог переопределения причины отклонения аналитиком (СГТ 1). Отметка мастера
    /// показывается только для контекста и не редактируется — решение аналитика ложится
    /// отдельным слоем (Part.SetupReasonOverride и т.д.), чтобы сохранить и официальную
    /// позицию мастера, и пригодную для статистики историю типовых причин.
    /// </summary>
    public partial class ReasonOverrideDialogWindow : Window
    {
        private readonly IReadOnlyDictionary<string, bool> _requireComment;

        public static new readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata("Переопределение причины"));

        public static readonly DependencyProperty CategoryTitleProperty =
            DependencyProperty.Register(nameof(CategoryTitle), typeof(string), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty MasterReasonProperty =
            DependencyProperty.Register(nameof(MasterReason), typeof(string), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty MasterDetailProperty =
            DependencyProperty.Register(nameof(MasterDetail), typeof(string), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty HasMasterDetailProperty =
            DependencyProperty.Register(nameof(HasMasterDetail), typeof(bool), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ReasonsProperty =
            DependencyProperty.Register(nameof(Reasons), typeof(IEnumerable<string>), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata(null));

        public static readonly DependencyProperty SelectedReasonProperty =
            DependencyProperty.Register(nameof(SelectedReason), typeof(string), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata(null, OnValidatedInputChanged));

        public static readonly DependencyProperty OverrideCommentProperty =
            DependencyProperty.Register(nameof(OverrideComment), typeof(string), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata(string.Empty, OnValidatedInputChanged));

        public static readonly DependencyProperty IsMasterFaultProperty =
            DependencyProperty.Register(nameof(IsMasterFault), typeof(bool), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty MasterFaultCommentProperty =
            DependencyProperty.Register(nameof(MasterFaultComment), typeof(string), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty CommentRequiredProperty =
            DependencyProperty.Register(nameof(CommentRequired), typeof(bool), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty CanConfirmProperty =
            DependencyProperty.Register(nameof(CanConfirm), typeof(bool), typeof(ReasonOverrideDialogWindow),
                new PropertyMetadata(false));

        public new string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary> «Отклонения в наладке» / «Отклонения в изготовлении». </summary>
        public string CategoryTitle
        {
            get => (string)GetValue(CategoryTitleProperty);
            set => SetValue(CategoryTitleProperty, value);
        }

        public string MasterReason
        {
            get => (string)GetValue(MasterReasonProperty);
            set => SetValue(MasterReasonProperty, value);
        }

        public string MasterDetail
        {
            get => (string)GetValue(MasterDetailProperty);
            set => SetValue(MasterDetailProperty, value);
        }

        public bool HasMasterDetail
        {
            get => (bool)GetValue(HasMasterDetailProperty);
            set => SetValue(HasMasterDetailProperty, value);
        }

        public IEnumerable<string>? Reasons
        {
            get => (IEnumerable<string>?)GetValue(ReasonsProperty);
            set => SetValue(ReasonsProperty, value);
        }

        public string? SelectedReason
        {
            get => (string?)GetValue(SelectedReasonProperty);
            set => SetValue(SelectedReasonProperty, value);
        }

        public string OverrideComment
        {
            get => (string)GetValue(OverrideCommentProperty);
            set => SetValue(OverrideCommentProperty, value);
        }

        /// <summary>
        /// Считать ли переопределение ошибкой мастера. По умолчанию нет — сам факт
        /// переопределения ещё не значит вину мастера. Аналитик ставит флаг сам, когда была
        /// возможность выбрать верно: СГТ смотрит историю изготовления, Winnum, 1С, мастер —
        /// только смену и цифры.
        /// </summary>
        public bool IsMasterFault
        {
            get => (bool)GetValue(IsMasterFaultProperty);
            set => SetValue(IsMasterFaultProperty, value);
        }

        /// <summary> Опциональное пояснение, в чём именно ошибся мастер. Видно только при <see cref="IsMasterFault"/> = true. </summary>
        public string MasterFaultComment
        {
            get => (string)GetValue(MasterFaultCommentProperty);
            set => SetValue(MasterFaultCommentProperty, value);
        }

        /// <summary> Требует ли выбранная причина обязательного обоснования. </summary>
        public bool CommentRequired
        {
            get => (bool)GetValue(CommentRequiredProperty);
            set => SetValue(CommentRequiredProperty, value);
        }

        public bool CanConfirm
        {
            get => (bool)GetValue(CanConfirmProperty);
            set => SetValue(CanConfirmProperty, value);
        }

        /// <param name="categoryTitle">Заголовок категории, как в шапке колонки грида.</param>
        /// <param name="masterReason">Отметка мастера — показывается для контекста, не меняется.</param>
        /// <param name="masterDetail">Детализация мастера к его причине.</param>
        /// <param name="reasons">Список типовых причин своей категории.</param>
        /// <param name="requireComment">Причина → обязательно ли обоснование (тот же справочник, что и у мастера).</param>
        /// <param name="currentOverride">Текущее переопределение, если правим существующее.</param>
        public ReasonOverrideDialogWindow(
            string categoryTitle,
            string masterReason,
            string masterDetail,
            IEnumerable<string> reasons,
            IReadOnlyDictionary<string, bool> requireComment,
            string? currentOverride = null,
            string currentComment = "",
            bool currentIsMasterFault = false,
            string currentMasterFaultComment = "")
        {
            InitializeComponent();

            _requireComment = requireComment;

            CategoryTitle = categoryTitle;
            MasterReason = string.IsNullOrWhiteSpace(masterReason) ? "не указана" : masterReason;
            MasterDetail = masterDetail ?? string.Empty;
            HasMasterDetail = !string.IsNullOrWhiteSpace(masterDetail);
            Reasons = reasons;
            OverrideComment = currentComment ?? string.Empty;
            IsMasterFault = currentIsMasterFault;
            MasterFaultComment = currentMasterFaultComment ?? string.Empty;
            SelectedReason = string.IsNullOrWhiteSpace(currentOverride) ? null : currentOverride;

            DataContext = this;
            UpdateValidation();
        }

        private static void OnValidatedInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            ((ReasonOverrideDialogWindow)d).UpdateValidation();

        private void UpdateValidation()
        {
            // Обязательность обоснования берём из того же справочника, по которому валидируется
            // мастер, — переопределение не должно быть обосновано слабее исходной отметки.
            var reason = SelectedReason;
            CommentRequired = !string.IsNullOrWhiteSpace(reason)
                              && _requireComment.TryGetValue(reason, out var required)
                              && required;

            CanConfirm = !string.IsNullOrWhiteSpace(reason)
                         && (!CommentRequired || !string.IsNullOrWhiteSpace(OverrideComment));
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        // Заготовки станок/операция — полностью заменяют текст обоснования, как и в гриде
        // (см. OnVariantClick у MasterCommentCellContextMenu в PartsInfoWindow.xaml.cs).
        private void OnVariantClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;
            OverrideComment = item.Header?.ToString() ?? string.Empty;
        }

        private void OnClearVariantClick(object sender, RoutedEventArgs e) =>
            OverrideComment = string.Empty;

        private void OnInsertValueClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;
            string value = item.Tag?.ToString() ?? item.Header?.ToString() ?? string.Empty;

            int caretIndex = CommentBox.CaretIndex;
            CommentBox.Text = CommentBox.Text.Insert(caretIndex, value);
            CommentBox.CaretIndex = caretIndex + value.Length;
            CommentBox.Focus();
        }
    }
}
