using System.Windows;
using System.Windows.Controls;

namespace remeLog.Views.Controls
{
    /// <summary>
    /// Живой глаз-Дозор: состояние ИИ в статусбаре.
    /// Флаги задаются привязками снаружи, анимация — триггерами в XAML.
    /// </summary>
    public partial class DozorEye : UserControl
    {
        public DozorEye()
        {
            InitializeComponent();
        }

        /// <summary>Идёт анализ (размышления/шаги агента).</summary>
        public bool IsBusy
        {
            get => (bool)GetValue(IsBusyProperty);
            set => SetValue(IsBusyProperty, value);
        }

        public static readonly DependencyProperty IsBusyProperty =
            DependencyProperty.Register(nameof(IsBusy), typeof(bool), typeof(DozorEye), new PropertyMetadata(false));

        /// <summary>Включён агентский режим (зрачок осматривается, иначе пульсирует).</summary>
        public bool IsAgent
        {
            get => (bool)GetValue(IsAgentProperty);
            set => SetValue(IsAgentProperty, value);
        }

        public static readonly DependencyProperty IsAgentProperty =
            DependencyProperty.Register(nameof(IsAgent), typeof(bool), typeof(DozorEye), new PropertyMetadata(false));

        /// <summary>ИИ доступен (иначе глаз приглушён).</summary>
        public bool IsAvailable
        {
            get => (bool)GetValue(IsAvailableProperty);
            set => SetValue(IsAvailableProperty, value);
        }

        public static readonly DependencyProperty IsAvailableProperty =
            DependencyProperty.Register(nameof(IsAvailable), typeof(bool), typeof(DozorEye), new PropertyMetadata(true));

        /// <summary>Есть готовый результат (включает цветовую индикацию вердикта).</summary>
        public bool HasResult
        {
            get => (bool)GetValue(HasResultProperty);
            set => SetValue(HasResultProperty, value);
        }

        public static readonly DependencyProperty HasResultProperty =
            DependencyProperty.Register(nameof(HasResult), typeof(bool), typeof(DozorEye), new PropertyMetadata(false));

        /// <summary>Результат с ошибкой.</summary>
        public bool HasError
        {
            get => (bool)GetValue(HasErrorProperty);
            set => SetValue(HasErrorProperty, value);
        }

        public static readonly DependencyProperty HasErrorProperty =
            DependencyProperty.Register(nameof(HasError), typeof(bool), typeof(DozorEye), new PropertyMetadata(false));

        /// <summary>Вердикт требует проверки.</summary>
        public bool RequiresReview
        {
            get => (bool)GetValue(RequiresReviewProperty);
            set => SetValue(RequiresReviewProperty, value);
        }

        public static readonly DependencyProperty RequiresReviewProperty =
            DependencyProperty.Register(nameof(RequiresReview), typeof(bool), typeof(DozorEye), new PropertyMetadata(false));
    }
}
