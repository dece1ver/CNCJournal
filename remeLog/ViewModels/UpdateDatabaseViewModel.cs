using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using DocumentFormat.OpenXml.Bibliography;
using libeLog;
using libeLog.Base;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Sql;
using Microsoft.Data.SqlClient;
using remeLog.Infrastructure;
using remeLog.Models;

namespace remeLog.ViewModels
{
    public class UpdateDatabaseViewModel : ViewModel
    {
        private readonly CancellationTokenSource _cts = new();
        private bool _isBusy;
        private string _status;

        public ObservableCollection<UpdateInfo> LogMessages { get; } = new();

        public bool IsBusy
        {
            get => _isBusy;
            set 
            {
                Set(ref _isBusy, value);
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string Status
        {
            get => _status;
            set => Set(ref _status, value);
        }

        public UpdateDatabaseViewModel()
        {
            StartCommand = new LambdaCommand(OnStartCommandExecuted, CanStartCommandExecute);
            CancelCommand = new LambdaCommand(OnCancelCommandExecuted, CanCancelCommandExecute);
            ExportCommand = new LambdaCommand(OnExportCommandExecuted, CanExportCommandExecute);
            _status = "";
            
        }


        #region Start
        public ICommand StartCommand { get; }
        private async void OnStartCommandExecuted(object p)
        {
            await StartAsync();
        }
        private bool CanStartCommandExecute(object p) => !IsBusy;
        #endregion


        #region Cancel
        public ICommand CancelCommand { get; }
        private void OnCancelCommandExecuted(object p)
        {
            _cts.Cancel();
        }
        private bool CanCancelCommandExecute(object p) => IsBusy;
        #endregion

        #region Export
        public ICommand ExportCommand { get; }
        private async void OnExportCommandExecuted(object p)
        {
            var fileDialog = new SaveFileDialog() { Filter = "Markdown (*.md)|*.md", DefaultExt = "md" };

            if (fileDialog.ShowDialog() != DialogResult.OK)
            {
                Status = "Выбор отменён.";
                return;
            }

            await File.WriteAllTextAsync(fileDialog.FileName, SqlSchemaDocumentation.GenerateMarkdownForTables(SqlSchemaBootstrapper.GetAllTableDefinitions()));
            Status = "Сохранено.";
            await Task.Delay(3000);
            if (Status == "Сохранено.") Status = "";
        }
        private bool CanExportCommandExecute(object p) => true;
        #endregion



        private async Task StartAsync()
        {
            IsBusy = true;
            LogMessages.Clear();
            Status = "Проверка...";

            var progress = new Progress<(string Message, Status? Icon)>(msg =>
            {
                var info = new UpdateInfo(msg.Message, msg.Icon);
                if (msg.Icon != null)
                {
                    for (int i = 0; i < LogMessages.Count; i++)
                    {
                        if (LogMessages[i].Message == msg.Message && LogMessages[i].Icon != null)
                        {
                            LogMessages.RemoveAt(i);
                            LogMessages.Insert(i, info);
                            return;
                        }
                    }
                }
                LogMessages.Add(info);
            });

            try
            {
                using var connection = new SqlConnection(AppSettings.Instance.ConnectionString);
                await SqlSchemaBootstrapper.ApplyAllAsync(connection.ConnectionString, connection, progress, _cts.Token);
                Status = "Готово";
            }
            catch (OperationCanceledException)
            {
                Status = "Отменено";
                LogMessages.Add(new UpdateInfo("Операция отменена пользователем."));
            }
            catch (Exception ex)
            {
                Status = "Ошибка";
                LogMessages.Add(new UpdateInfo("Ошибка: " + ex.Message, libeLog.Infrastructure.Status.Error));
                //if (ex is SqlException sqlException)
                //{
                //    foreach (SqlError error in sqlException.Errors)
                //    {
                //        LogMessages.Add($"Код: {error.Number}");
                //    }
                //}
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
