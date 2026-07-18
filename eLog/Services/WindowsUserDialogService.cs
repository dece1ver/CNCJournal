using eLog.Infrastructure;
using eLog.Models;
using eLog.Services.Interfaces;
using eLog.Views.Windows.Dialogs;
using eLog.Views.Windows.Settings;
using libeLog;
using libeLog.Models;
using libeLog.Views;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using static eLog.Infrastructure.Text;
using static eLog.Infrastructure.Extensions.Util;
using OperatorsEditWindow = eLog.Views.Windows.Settings.OperatorsEditWindow;

namespace eLog.Services
{
    internal class WindowsUserDialogService : IUserDialogService
    {
        public bool Confirm(string message, string caption, bool exclamation = false, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.No)
        {
            return MessageBoxWindow.Show(
                       message,
                       caption,
                       MessageBoxButton.YesNo,
                       exclamation
                           ? MessageBoxImage.Exclamation
                           : MessageBoxImage.Question,
                       defaultButton)
                   == MessageBoxResult.Yes;
        }

        public bool Edit(object item)
        {
            return item switch
            {
                Part part => EditDetail(ref part),
                _ => false,
            };
        }

        public void ShowError(string message, string caption = "Ошибка") =>
            MessageBoxWindow.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);

        public void ShowInfo(string message, string caption = "Информация") =>
            MessageBoxWindow.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);

        public void ShowWarning(string message, string caption = "Внимание") =>
            MessageBoxWindow.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);

        /// <summary>
        /// Возвращает вызывает диалог для изменения операторов.
        /// </summary>
        /// <returns>true если операторы были изменены, false при отмене, либо если все операторы остались неизменны</returns>
        public static bool EditOperators()
        {
            if (AppSettings.Instance.DebugMode) { WriteLog($"Редактирование операторов.\n\tОператор: {AppSettings.Instance.CurrentOperator?.DisplayName}"); }
            var currentOperator = AppSettings.Instance.CurrentOperator is null ? null : new Operator(AppSettings.Instance.CurrentOperator);
            var dlg = new OperatorsEditWindow()
            {
                Owner = Application.Current.MainWindow,
            };

            if (dlg.ShowDialog() != true)
            {
                if (AppSettings.Instance.DebugMode) { WriteLog($"Отмена."); }
                return false;
            }
            AppSettings.Instance.Operators = new DeepObservableCollection<Operator>(dlg.Operators.ToList()
                .Where(o => !string.IsNullOrWhiteSpace(o.DisplayName)));
            AppSettings.Instance.CurrentOperator = null;
            foreach (var @operator in AppSettings.Instance.Operators)
            {
                if (@operator.Equals(currentOperator)) AppSettings.Instance.CurrentOperator = @operator;
            }
            if (AppSettings.Instance.DebugMode) { WriteLog($"Подтверждено."); }
            return true;
        }

        public static bool EditSettings()
        {
            if (AppSettings.Instance.DebugMode) { WriteLog($"Редактирование настроек.\n\tОператор: {AppSettings.Instance.CurrentOperator?.DisplayName}"); }
            var dlg = new AppSettingsWindow()
            {
                Owner = Application.Current.MainWindow,
            };
            if (dlg.ShowDialog() != true)
            {
                if (AppSettings.Instance.DebugMode) { WriteLog($"Отмена."); }
                return false;
            }
            AppSettings.Instance.Machine = dlg.Machine;
            AppSettings.Instance.Machines = dlg.Machines.ToList();
            AppSettings.Instance.UpdatePath = dlg.UpdatePath;
            AppSettings.Instance.OrdersSourcePath = dlg.OrdersSourcePathTextBox.Text;
            AppSettings.Instance.OrderQualifiers = dlg.OrderQualifiers;
            AppSettings.Instance.DebugMode = dlg.DebugMode;
            AppSettings.Instance.StorageType = dlg.StorageType;
            AppSettings.Instance.GoogleCredentialsPath = dlg.GoogleCredentialsPath;
            AppSettings.Instance.GsId = dlg.GsId;
            AppSettings.Instance.WiteToGs = dlg.WriteToGs;
            AppSettings.Instance.ConnectionString = dlg.ConnectionString;
            AppSettings.Instance.PathToRecievers = dlg.PathToRecievers;
            AppSettings.Instance.SmtpAddress = dlg.SmtpAddress;
            AppSettings.Instance.SmtpPort = dlg.SmtpPort;
            AppSettings.Instance.SmtpUsername = dlg.SmtpUsername;
            AppSettings.Instance.TimerForNotify = dlg.TimerForNotify;
            AppSettings.Instance.EnableWriteShiftHandover = dlg.EnableWriteShiftHandover;
            if (AppSettings.Instance.DebugMode) { WriteLog($"Подтверждено."); }
            return true;
        }

        public static bool GetBarCode(ref string barCode)
        {
            var dlg = new ReadBarCodeWindow()
            {
                BarCode = string.Empty,
                Owner = Application.Current.MainWindow,
            };
            if (dlg.ShowDialog() != true) return false;
            barCode = dlg.BarCode;
            return true;
        }

        public static EndSetupResult GetSetupResult()
        {
            var dlg = new EndSetupDialogWindow()
            {
                Owner = Application.Current.MainWindow,
            };
            _ = dlg.ShowDialog();
            return dlg.EndSetupResult;
        }

        public static bool EditDetail(ref Part part, bool newDetail = false, bool fromTask = false)
        {
            if (AppSettings.Instance.DebugMode && !newDetail) { WriteLog(part, $"Редактирование детали."); }

            var tempPart = new Part(part);
            tempPart.DownTimes = new DeepObservableCollection<DownTime>(
                        tempPart.DownTimes.Where(downtime => downtime.Type != DownTime.Types.PartialSetup));
            var dlg = new EditDetailWindow(tempPart, newDetail, fromTask)
            {
                Owner = Application.Current.MainWindow,
            };
            if (dlg.ShowDialog() != true)
            {
                if (AppSettings.Instance.DebugMode && !newDetail) { WriteLog($"Отмена."); }
                return false;
            }

            //if (dlg.Part.FinishedCount > 0)
            //{
            //    dlg.Part.DownTimes = new DeepObservableCollection<DownTime>(dlg.Part.DownTimes.Where(dt => dt.Type != DownTime.Types.PartialSetup));
            //}

            part = dlg.Part;

            if (AppSettings.Instance.DebugMode) { WriteLog($"Подтверждено."); }
            return true;
        }

        public static bool FinishDetail(ref Part part)
        {
            if (AppSettings.Instance.DebugMode) { WriteLog(part, $"Завершение детали."); }
            var tempPart = new Part(part);
            var dlg = new EndDetailDialogWindow(tempPart)
            {
                Owner = Application.Current.MainWindow,
                FinishedCount = tempPart.FinishedCount > 0 ? tempPart.FinishedCount.ToString() : string.Empty,
                MachineTimeText = part.MachineTime.TotalMinutes > 0 ? part.MachineTime.ToString(Constants.TimeSpanFormat) : string.Empty,
            };

            if (dlg.ShowDialog() != true)
            {
                if (AppSettings.Instance.DebugMode) { WriteLog($"Отмена."); }
                return false;
            }
            dlg.Part.EndMachiningTime = DateTime.Today.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute);
            if (dlg.Part.FinishedCount is <= 1) dlg.Part.StartMachiningTime = dlg.Part.EndMachiningTime;
            if (dlg.Part.FinishedCount > 0)
            {
                dlg.Part.DownTimes = new DeepObservableCollection<DownTime>(dlg.Part.DownTimes.Where(dt => dt.Type != DownTime.Types.PartialSetup));
            }
            part = dlg.Part;
            if (AppSettings.Instance.DebugMode) { WriteLog($"Подтверждено."); }
            return true;
        }

        public static (DownTime.Types? Type, string ToolType, string Comment) SetDownTimeType(Window? owner = null)
        {
            var toolType = "";
            var comment = "";
            var dlg = new SetDownTimeDialogWindow()
            {
                Owner = owner ?? Application.Current.MainWindow,
            };
            _ = dlg.ShowDialog();
            switch (dlg.Type)
            {
                case DownTime.Types.Maintenance:
                    break;
                case DownTime.Types.ToolSearching:
                    var toolSearchingDlg = new UserInputDialogWindow("Поподробнее:", "Что ищете?", AppSettings.Instance.SearchToolTypes)
                    {
                        Title = DownTimes.ToolSearching,
                        Owner = owner ?? Application.Current.MainWindow
                    };
                    if (toolSearchingDlg.ShowDialog() != true) return (null, toolType, comment);
                    toolType = toolSearchingDlg.SelectedOption;
                    comment = toolSearchingDlg.UserInput;
                    break;
                case DownTime.Types.ToolChanging:
                    break;
                case DownTime.Types.Mentoring:
                    break;
                case DownTime.Types.ContactingDepartments:
                    break;
                case DownTime.Types.FixtureMaking:
                    break;
                case DownTime.Types.HardwareFailure:
                    var failureDlg = new UserInputDialogWindow("Опишите неисправность оборудования.")
                    {
                        Title = DownTimes.HardwareFailure,
                        Owner = owner ?? Application.Current.MainWindow
                    };
                    if (failureDlg.ShowDialog() != true) return (null, toolType, comment);
                    var failureDlgMessage = failureDlg.UserInput ?? "Без комментария.";
                    _ = TrySendHardwareFailureMessageAsync(failureDlgMessage);
                    break;
                case DownTime.Types.PartialSetup:
                    break;
                default:
                    break;
            }
            return (dlg.Type, toolType, comment);
        }

        public static async Task TrySendHardwareFailureMessageAsync(string message)
        {
            await Task.Run(() => {
                var res = Database.SendHardwareFailureMessage(message);
                string msg = "";
                switch (res.Result)
                {
                    case DbResult.Ok:
                        msg = "Сигнал успешно отправлен.";
                        break;
                    case DbResult.AuthError:
                        msg = "Сигнал не отправлен из-за неудачной авторизации.";
                        break;
                    case DbResult.Error:
                        msg = "Сигнал не отправлен из-за ошибки.";
                        break;
                    case DbResult.NoConnection:
                        msg = "Сигнал не отправлен из-за отсутствия подключения к БД.";
                        break;
                }
                if (AppSettings.Instance.DebugMode) WriteLog(msg);
            });
        }
    }
}