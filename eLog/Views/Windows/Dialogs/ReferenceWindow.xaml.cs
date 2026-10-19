using System.Windows;
using System.Windows.Input;

namespace eLog.Views.Windows.Dialogs
{
    /// <summary>
    /// Логика взаимодействия для ReferenceWindow.xaml
    /// </summary>
    public partial class ReferenceWindow : Window
    {
        public ReferenceWindow()
        {
            InitializeComponent();
        }

        private void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
