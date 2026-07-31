using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace remeLog.Views.Controls
{
    /// <summary>
    /// Логика взаимодействия для IncDecControl.xaml
    /// </summary>
    public partial class IncDecControl : UserControl
    {
        public object? Value
        {
            get { return (object?)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(object), typeof(IncDecControl), 
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));



        public IncDecControl()
        {
            InitializeComponent();
        }

        private void IncButton_Click(object sender, RoutedEventArgs e)
        {
            Increment();
        }

        private void DecButton_Click(object sender, RoutedEventArgs e)
        {
            Decrement();
        }

        private void UserControl_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
            {
                Increment();
            } else
            {
                Decrement();
            }
        }

        private void Increment()
        {
            switch (Value)
            {
                case null:
                    Value = 1;
                    break;
                case int i:
                    Value = i + 1;
                    break;
                case DateTime dt:
                    Value = dt.AddDays(1);
                    break;
                default:
                    break;
            }
        }

        private void Decrement()
        {
            switch (Value)
            {
                case int i when i > 1:
                    Value = i - 1;
                    break;
                case int i when i == 1:
                    Value = null;
                    break;
                case DateTime dt:
                    Value = dt.AddDays(-1);
                    break;
                default:
                    break;
            }
        }
    }
}
