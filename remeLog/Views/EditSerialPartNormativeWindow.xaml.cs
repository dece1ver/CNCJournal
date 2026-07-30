using DocumentFormat.OpenXml.Presentation;
using libeLog;
using libeLog.Models;
using remeLog.Infrastructure;
using remeLog.Infrastructure.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using libeLog.Views;

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для EditSerialPartNormativeWindow.xaml
    /// </summary>
    public partial class EditSerialPartNormativeWindow : Window
    {
        public enum OperationDisplayMode
        {
            Setup, Production
        }

        public EditSerialPartNormativeWindow()
        {
            SetNewSetupNormativeCommand = new LambdaCommand(OnSetNewSetupNormativeCommandExecuted, CanSetNewSetupNormativeCommandExecute);
            SetNewProductionNormativeCommand = new LambdaCommand(OnSetNewProductionNormativeCommandExecuted, CanSetNewProductionNormativeCommandExecute);
            RemoveNormativeCommand = new LambdaCommand(OnRemoveNormativeCommandExecuted, CanRemoveNormativeCommandExecute);
            InitializeComponent();
        }

        public static readonly DependencyProperty SerialPartProperty =
        DependencyProperty.Register(nameof(SerialPart), typeof(SerialPart), typeof(EditSerialPartNormativeWindow),
            new PropertyMetadata(null));

        public static readonly DependencyProperty DisplayModeProperty =
            DependencyProperty.Register(nameof(DisplayMode), typeof(OperationDisplayMode), typeof(EditSerialPartNormativeWindow),
                new PropertyMetadata(OperationDisplayMode.Setup));

        public static readonly DependencyProperty NewSetupNormativeProperty =
        DependencyProperty.Register(nameof(NewSetupNormative), typeof(double?), typeof(EditSerialPartNormativeWindow),
            new PropertyMetadata(null));

        public static readonly DependencyProperty NewProductionNormativeProperty =
        DependencyProperty.Register(nameof(NewProductionNormative), typeof(double?), typeof(EditSerialPartNormativeWindow),
            new PropertyMetadata(null));

        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register(nameof(Status), typeof(string), typeof(EditSerialPartNormativeWindow), 
                new PropertyMetadata(string.Empty));

        public SerialPart SerialPart
        {
            get => (SerialPart)GetValue(SerialPartProperty);
            set => SetValue(SerialPartProperty, value);
        }

        public OperationDisplayMode DisplayMode
        {
            get => (OperationDisplayMode)GetValue(DisplayModeProperty);
            set => SetValue(DisplayModeProperty, value);
        }

        public double? NewSetupNormative
        {
            get => (double?)GetValue(NewSetupNormativeProperty);
            set => SetValue(NewSetupNormativeProperty, value);
        }

        public double? NewProductionNormative
        {
            get => (double?)GetValue(NewProductionNormativeProperty);
            set => SetValue(NewProductionNormativeProperty, value);
        }

        public string Status
        {
            get { return (string)GetValue(StatusProperty); }
            set { SetValue(StatusProperty, value); }
        }

        #region SetNewSetupNormative
        public ICommand SetNewSetupNormativeCommand { get; }
        private void OnSetNewSetupNormativeCommandExecuted(object p)
        {
            if (p is FrameworkElement fe && fe.DataContext is CncSetup cncSetup)
            {
                var parentOperation = SerialPart.Operations.FirstOrDefault(sp => sp.Setups.Contains(cncSetup));
                if (parentOperation == null)
                    return;
                var dlg = new UserInputDialogWindow($"{parentOperation}: {cncSetup.Number} установ", "Введите новый норматив на наладку:", expectedType: typeof(double), focusAndSelect: true, checkBox: new(false, "Подтвержденный"))
                {
                    Owner = Window.GetWindow(fe)
                };
                if (dlg.ShowDialog() == true && double.TryParse(dlg.UserInput, out double normative))
                {
                    if (normative <= 0)
                    {
                        ShowMessage("Значение должно быть больше 0");
                        return;
                    }
                    cncSetup.Normatives.Add(new NormativeEntry() { Type = NormativeEntry.NormativeType.Setup, Value = normative, EffectiveFrom = DateTime.Now, IsApproved = dlg.OptionalCheckBox is { IsChecked: true } });
                    NewSetupNormative = normative;
                    ShowMessage("Установлен новый норматив на наладку");
                }
                else
                {
                    ShowMessage("Отмена");
                }
            }
        }
        private bool CanSetNewSetupNormativeCommandExecute(object p) => true;
        #endregion

        #region SetNewProductionNormative
        public ICommand SetNewProductionNormativeCommand { get; }

        private void OnSetNewProductionNormativeCommandExecuted(object p)
        {
            if (!Util.HasFeature(RemeLogFeature.AdvancedEdit)) { ShowMessage("Нет прав на выполнение операции"); return; }

            if (p is FrameworkElement fe && fe.DataContext is CncSetup cncSetup)
            {
                var parentOperation = SerialPart.Operations.FirstOrDefault(sp => sp.Setups.Contains(cncSetup));
                if (parentOperation == null)
                    return;
                var dlg = new UserInputDialogWindow($"{parentOperation}: {cncSetup.Number} установ", "Введите новый норматив на изготовление:", expectedType: typeof(double), focusAndSelect: true, checkBox: new(false, "Подтвержденный"))
                {
                    Owner = Window.GetWindow(fe)
                };
                if (dlg.ShowDialog() == true && double.TryParse(dlg.UserInput, out double normative))
                {
                    if (normative <= 0)
                    {
                        ShowMessage("Значение должно быть больше 0");
                        return;
                    }
                    cncSetup.Normatives.Add(new NormativeEntry() { Type = NormativeEntry.NormativeType.Production, Value = normative, EffectiveFrom = DateTime.Now, IsApproved = dlg.OptionalCheckBox is { IsChecked: true }});
                    NewProductionNormative = normative;
                    ShowMessage("Установлен новый норматив на изготовление");
                }
                else
                {
                    ShowMessage("Отмена");
                }
            }
        }
        private bool CanSetNewProductionNormativeCommandExecute(object p) => true;
        #endregion

        #region RemoveNormative
        public ICommand RemoveNormativeCommand { get; }
        private async void OnRemoveNormativeCommandExecuted(object p)
        {
            if (!Util.HasFeature(RemeLogFeature.AdvancedEdit)) { ShowMessage("Нет прав на выполнение операции"); return; }

            if (p is FrameworkElement fe && fe.DataContext is NormativeEntry normative)
            {
                if (MessageBoxWindow.Show("Удалить норматив?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxDefaultButton.No) != MessageBoxResult.Yes) return;
                await OperatorsDatabase.RemoveNormativeAsync(normative);
            }
        }
        private bool CanRemoveNormativeCommandExecute(object p) => true;
        #endregion

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            await libeLog.Infrastructure.Database.SaveSerialPartAsync(SerialPart, AppSettings.Instance.ConnectionString!, new Progress<string>(p => Status = p));
            Close();
        }

        private void ShowMessage(string message, int delay = 3000)
        {
            Task.Run(async () =>
            {
                Status = message;
                await Task.Delay(delay);
                if (Status == message) Status = string.Empty;
            });
        }
    }
}
