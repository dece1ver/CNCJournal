using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace eLog.Views.Controls
{
    /// <summary>
    /// Логика взаимодействия для KeyboardControl.xaml
    /// </summary>
    public partial class KeyboardControl : UserControl
    {
        private bool _CyrillicVisibility;
        private bool _CapsEnabled;

        /// <summary> Caps Lock для экранной клавиатуры: запоминается, не сбрасывается при смене языка. </summary>
        public bool CapsEnabled
        {
            get => _CapsEnabled;
            private set
            {
                _CapsEnabled = value;
                CyrillicKeyboard.CapsEnabled = value;
                LatinKeyboard.CapsEnabled = value;
                UpdateCapsVisual();
            }
        }

        public KeyboardControl()
        {
            InitializeComponent();
            _CyrillicVisibility = true;
            SetVisibility();
            CapsEnabled = false;
            Visibility = Visibility.Collapsed;
        }

        private void LangButton_Click(object sender, RoutedEventArgs e)
        {
            _CyrillicVisibility = !_CyrillicVisibility;
            SetVisibility();
        }

        private void CapsButton_Click(object sender, RoutedEventArgs e)
        {
            CapsEnabled = !CapsEnabled;
        }

        private void UpdateCapsVisual()
        {
            if (_CapsEnabled)
            {
                CapsButton.SetResourceReference(Control.BackgroundProperty, "Areopag.Accent");
                CapsButton.SetResourceReference(Control.ForegroundProperty, "Areopag.OnAccent");
                CapsButton.FontWeight = FontWeights.Bold;
            }
            else
            {
                CapsButton.ClearValue(Control.BackgroundProperty);
                CapsButton.ClearValue(Control.ForegroundProperty);
                CapsButton.FontWeight = FontWeights.Normal;
            }
        }

        private void SetVisibility()
        {
            LangButton.Content = _CyrillicVisibility ? "RU" : "EN";
            CyrillicKeyboard.Visibility = _CyrillicVisibility ? Visibility.Visible : Visibility.Collapsed;
            LatinKeyboard.Visibility = _CyrillicVisibility ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}