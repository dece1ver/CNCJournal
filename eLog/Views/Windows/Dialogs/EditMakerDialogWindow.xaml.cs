using eLog.Infrastructure;
using eLog.Infrastructure.Extensions;
using eLog.Models;
using libeLog.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace eLog.Views.Windows.Dialogs
{
    /// <summary>
    /// Логика взаимодействия для EditMakerDialogWindow.xaml
    /// </summary>
    public partial class EditMakerDialogWindow : Window, INotifyPropertyChanged
    {
        public Operator Operator { get; set; }
        public string Shift { get; set; }
        public static string[] Shifts => Text.Shifts;
        public EditMakerDialogWindow(Part part)
        {
            Operator = part.Operator;
            Shift = part.Shift;
            InitializeComponent();
        }



        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            OnPropertyChanged(nameof(Operator));
            OnPropertyChanged(nameof(Operator.DisplayName));
        }

        #region PropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string PropertyName = null!)
        {
            var handlers = PropertyChanged;
            if (handlers is null) return;

            var invokationList = handlers.GetInvocationList();
            var args = new PropertyChangedEventArgs(PropertyName);

            foreach (var action in invokationList)
            {
                if (action.Target is DispatcherObject dispatcherObject)
                {
                    dispatcherObject.Dispatcher.Invoke(action, this, args);
                }
                else
                {
                    action.DynamicInvoke(this, args);
                }
            }
        }

        protected virtual bool Set<T>(ref T field, T value, [CallerMemberName] string PropertyName = null!)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(PropertyName);
            return true;
        }




        #endregion
    }
}