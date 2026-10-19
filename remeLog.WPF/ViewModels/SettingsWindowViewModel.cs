using libeLog;
using libeLog.Base;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Models;
using remeLog.Infrastructure;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using remeLog.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using libeLog.Views;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace remeLog.ViewModels
{
    internal class SettingsWindowViewModel : ViewModel
    {
        public SettingsWindowViewModel()
        {
            SetQualificationSourceTableCommand = new LambdaCommand(OnSetQualificationSourceTableCommandExecuted, CanSetQualificationSourceTableCommandExecute);
            SetGoogleCredentialPathCommand = new LambdaCommand(OnSetGoogleCredentialPathCommandExecuted, CanSetGoogleCredentialPathCommandExecute);
            CheckAssignedPartsSheetCommand = new LambdaCommand(OnCheckAssignedPartsSheetCommandExecuted, CanCheckAssignedPartsSheetCommandExecute);
            CheckConnectionStringCommand = new LambdaCommand(OnCheckConnectionStringCommandExecuted, CanCheckConnectionStringCommandExecute);
            EditColumnProfilesCommand = new LambdaCommand(OnEditColumnProfilesCommandExecuted, CanEditColumnProfilesCommandExecute);

            _DataSource = AppSettings.Instance.DataSource;
            _QualificationSourcePath = new SettingsItem(AppSettings.Instance.QualificationSourcePath ?? "");
            _GoogleCredentialPath = new SettingsItem(AppSettings.Instance.GoogleCredentialPath ?? "");
            _AssignedPartsSheet = new SettingsItem(AppSettings.Instance.AssignedPartsSheet ?? "");
            _ConnectionString = new SettingsItem(AppSettings.Instance.ConnectionString ?? "");
            _InstantUpdateOnMainWindow = AppSettings.Instance.InstantUpdateOnMainWindow;
            Role = AppSettings.Instance.User ??= User.Viewer;

            Task.Run(() => CheckAllSettings());
        }

        private async Task CheckAllSettings()
        {
            _ = CheckQualificationSourceTableAsync();
            _ = CheckConnectionStringAsync();
            await CheckGoogleCredentialPathAsync();
            _ = CheckAssignedPartsSheetAsync();
        }

        #region Свойства

        public List<DataSource> DataSourceTypes { get; set; } = new() 
        {
            new DataSource(DataSource.Types.Database),
            new DataSource(DataSource.Types.Excel),
        };

        private string _StatusText = string.Empty;
        /// <summary> Текст статусбара </summary>
        public string StatusText
        {
            get => _StatusText;
            set => Set(ref _StatusText, value);
        }

        private double _Progress = 1;
        /// <summary> Значение прогрессбара </summary>
        public double Progress
        {
            get => _Progress;
            set
            {
                Set(ref _Progress, value);
                OnPropertyChanged(nameof(ProgressBarVisibility));
            }
        }

        private double _ProgressMaxValue = 1;
        /// <summary> Максимальное значение прогрессбара </summary>
        public double ProgressMaxValue
        {
            get => _ProgressMaxValue;
            set
            {
                Set(ref _ProgressMaxValue, value);
                OnPropertyChanged(nameof(ProgressBarVisibility));
            }
        }

        private Visibility _ProgressBarVisibility = Visibility.Hidden;
        /// <summary> Видимость прогрессбара </summary>
        public Visibility ProgressBarVisibility
        {
            get => _ProgressBarVisibility;
            set => Set(ref _ProgressBarVisibility, value);
        }


        private DataSource _DataSource;
        /// <summary> Описание </summary>
        public DataSource DataSource
        {
            get => _DataSource;
            set => Set(ref _DataSource, value);
        }


        private SettingsItem _QualificationSourcePath;
        /// <summary> Путь к таблице с разрядами</summary>
        public SettingsItem QualificationSourcePath
        {
            get => _QualificationSourcePath;
            set => Set(ref _QualificationSourcePath, value);
        }

        private SettingsItem _GoogleCredentialPath;
        /// <summary> Путь к файлу с отчетами </summary>
        public SettingsItem GoogleCredentialPath
        {
            get => _GoogleCredentialPath;
            set => Set(ref _GoogleCredentialPath, value);
        }

        private SettingsItem _AssignedPartsSheet;
        /// <summary> Путь к директирии с суточными отчетами </summary>
        public SettingsItem AssignedPartsSheet
        {
            get => _AssignedPartsSheet;
            set => Set(ref _AssignedPartsSheet, value);
        }

        private SettingsItem _ConnectionString;
        /// <summary> Строка подключения к БД </summary>
        public SettingsItem ConnectionString
        {
            get => _ConnectionString;
            set => Set(ref _ConnectionString, value);
        }


        private User _Role;
        /// <summary> Роль пользователя </summary>
        public User Role
        {
            get => _Role;
            set => Set(ref _Role, value);
        }


        private bool _InstantUpdateOnMainWindow;
        /// <summary> Описание </summary>
        public bool InstantUpdateOnMainWindow
        {
            get => _InstantUpdateOnMainWindow;
            set => Set(ref _InstantUpdateOnMainWindow, value);
        }


        #endregion

        #region Команды

        #region SetQualificationSourceTable
        public ICommand SetQualificationSourceTableCommand { get; }
        private void OnSetQualificationSourceTableCommandExecuted(object p)
        {
            OpenFileDialog dlg = new()
            {
                Filter = "Книга Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx"
            };
            if (dlg.ShowDialog() != true) return;
            QualificationSourcePath.Value = dlg.FileName;
            Task.Run(() => CheckQualificationSourceTableAsync());
        }
        private static bool CanSetQualificationSourceTableCommandExecute(object p) => true;
        #endregion

        #region SetReportsTable
        public ICommand SetGoogleCredentialPathCommand { get; }
        private void OnSetGoogleCredentialPathCommandExecuted(object p)
        {
            OpenFileDialog dlg = new()
            {
                Filter = "JSON (*.json)|*.json",
                DefaultExt = "json"
            };
            if (dlg.ShowDialog() != true) return;
            GoogleCredentialPath.Value = dlg.FileName;
            Task.Run(() => CheckGoogleCredentialPathAsync());
        }
        private static bool CanSetGoogleCredentialPathCommandExecute(object p) => true;
        #endregion

        #region SetAssignedPartsSheet
        public ICommand CheckAssignedPartsSheetCommand { get; }
        private async void OnCheckAssignedPartsSheetCommandExecuted(object p)
        {
            await CheckAssignedPartsSheetAsync();
        }

        private static bool CanCheckAssignedPartsSheetCommandExecute(object p) => true;
        #endregion

        #region EditColumnProfiles
        public ICommand EditColumnProfilesCommand { get; }
        private void OnEditColumnProfilesCommandExecuted(object p)
        {
            ColumnProfilesWindow window = new() { Owner = Application.Current.MainWindow };
            if (window.ShowDialog() == true && window.DataContext is ColumnProfilesWindowViewModel vm)
            {
                AppSettings.Instance.ColumnProfiles = vm.Profiles.ToList();
                AppSettings.Save();
            }
        }
        private static bool CanEditColumnProfilesCommandExecute(object p) => true;
        #endregion

        #region CheckConnectionString
        public ICommand CheckConnectionStringCommand { get; }
        private void OnCheckConnectionStringCommandExecuted(object p)
        {
            Task.Run(() => CheckConnectionStringAsync());
        }
        private static bool CanCheckConnectionStringCommandExecute(object p) => true;
        #endregion


        #endregion

        private async Task CheckQualificationSourceTableAsync()
        {
            await Task.Run(() =>
            {
                QualificationSourcePath.Status = Status.Sync;
                QualificationSourcePath.Tip = Constants.StatusTips.Checking;
                if (File.Exists(QualificationSourcePath.Value))
                {
                    QualificationSourcePath.Status = Status.Ok;
                    QualificationSourcePath.Tip = Constants.StatusTips.Ok;
                    return;
                }
                QualificationSourcePath.Status = Status.Error;
                QualificationSourcePath.Tip = Constants.StatusTips.NoFile;
            });
        }

        private async Task CheckGoogleCredentialPathAsync()
        {
            await Task.Run(() =>
            {
                GoogleCredentialPath.Status = Status.Sync;
                GoogleCredentialPath.Tip = Constants.StatusTips.Checking;
                switch (GoogleCredentialPath.Value.CheckFileAccess())
                {
                    case FileCheckResult.Success:
                        GoogleCredentialPath.Status = Status.Ok;
                        GoogleCredentialPath.Tip = Constants.StatusTips.Ok;
                        break;
                    case FileCheckResult.FileNotFound:
                        GoogleCredentialPath.Status = Status.Error;
                        GoogleCredentialPath.Tip = Constants.StatusTips.NoFile;
                        break;
                    case FileCheckResult.AccessDenied:
                        GoogleCredentialPath.Status = Status.Warning;
                        GoogleCredentialPath.Tip = Constants.StatusTips.AccessError;
                        break;
                    case FileCheckResult.FileInUse:
                        GoogleCredentialPath.Status = Status.Warning;
                        GoogleCredentialPath.Tip = Constants.StatusTips.FileInUse;
                        break;
                    case FileCheckResult.GeneralError:
                        GoogleCredentialPath.Status = Status.Error;
                        GoogleCredentialPath.Tip = Constants.StatusTips.GeneralError;
                        break;
                    case FileCheckResult.InvalidPath:
                        GoogleCredentialPath.Status = Status.Error;
                        GoogleCredentialPath.Tip = Constants.StatusTips.InvalidPath;
                        break;
                }
            });
        }

        private async Task CheckAssignedPartsSheetAsync()
        {
            AssignedPartsSheet.Status = Status.Sync;
            AssignedPartsSheet.Tip = Constants.StatusTips.Checking;
            if (GoogleCredentialPath.Status != Status.Ok)
            {
                AssignedPartsSheet.Status = Status.Error;
                AssignedPartsSheet.Tip = Constants.StatusTips.InvalidPath;
                return;
            }
            if (string.IsNullOrEmpty(AssignedPartsSheet.Value))
            {
                AssignedPartsSheet.Status = Status.Error;
                AssignedPartsSheet.Tip = Constants.StatusTips.NotSet;
                return;
            }
            try
            {
                var gs = new GoogleSheet(GoogleCredentialPath.Value, AssignedPartsSheet.Value);
                _ = await gs.GetSpreadsheetAsync();
                AssignedPartsSheet.Status = Status.Ok;
                AssignedPartsSheet.Tip = Constants.StatusTips.Ok;

            }
            catch (InvalidOperationException ex) when (ex.HResult == -2146233079)
            {
                AssignedPartsSheet.Status = Status.Error;
                AssignedPartsSheet.Tip = "Некорректный файл с идентификационными данными";
            }
            catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                AssignedPartsSheet.Status = Status.Error;
                AssignedPartsSheet.Tip = "Таблица с таким идентификатором не найдена";
            }
            catch (Exception ex)
            {
                MessageBoxWindow.Show($"Ошибка: {ex.Message}", "Ошибка проверки", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CheckConnectionStringAsync()
        {
            await Task.Run(() =>
            {
                ConnectionString.Status = Status.Sync;
                ConnectionString.Tip = Constants.StatusTips.Checking;
                var res = ConnectionString.Value.CheckDbConnection();
                switch (res.Status)
                {
                    case DbResult.Ok:
                        ConnectionString.Status = Status.Ok;
                        break;
                    case DbResult.AuthError:
                        ConnectionString.Status = Status.Error;
                        break;
                    case DbResult.Error:
                        ConnectionString.Status = Status.Error;
                        break;
                }
                ConnectionString.Tip = res.Error ?? res.Value ?? "";
            });
        }
    }
}