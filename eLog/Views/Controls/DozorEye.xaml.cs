using System.Windows;
using System.Windows.Controls;

namespace eLog.Views.Controls
{
    /// <summary>
    /// Живой глаз-Дозор для чата: в покое статичен (зрачок Primary),
    /// пока IsBusy — зрачок краснеет и пульсирует (как DozorEye в remeLog).
    /// </summary>
    public partial class DozorEye : UserControl
    {
        public static readonly DependencyProperty IsBusyProperty =
            DependencyProperty.Register(
                nameof(IsBusy), typeof(bool), typeof(DozorEye),
                new PropertyMetadata(false));

        public bool IsBusy
        {
            get => (bool)GetValue(IsBusyProperty);
            set => SetValue(IsBusyProperty, value);
        }

        public DozorEye()
        {
            InitializeComponent();
        }
    }
}
