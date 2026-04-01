using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Enums;
using Microsoft.Win32;
using QCTasks.Commands;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace QCTasks.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private ObservableCollection<string> _recipients;
    private string _recipientsFile;
    private string _credentialsFile;
    private string _sheetId;
    private string _sqlConnectionString;
    private string _smtpAddress;
    private string _smtpPortText;
    private string _smtpUsername;
    private string _smtpPasswordEnvVar;
    private string? _errorMessage;
    private bool _saved;

    public string CredentialsFile
    {
        get => _credentialsFile;
        set { Set(ref _credentialsFile, value); ClearError(); }
    }

    public string SheetId
    {
        get => _sheetId;
        set { Set(ref _sheetId, value); ClearError(); }
    }

    public string SqlConnectionString
    {
        get => _sqlConnectionString;
        set => Set(ref _sqlConnectionString, value);
    }

    public string SmtpAddress
    {
        get => _smtpAddress;
        set => Set(ref _smtpAddress, value);
    }

    public string SmtpPortText
    {
        get => _smtpPortText;
        set { Set(ref _smtpPortText, value); ClearError(); }
    }

    public string SmtpUsername
    {
        get => _smtpUsername;
        set => Set(ref _smtpUsername, value);
    }

    public string SmtpPasswordEnvVar
    {
        get => _smtpPasswordEnvVar;
        set => Set(ref _smtpPasswordEnvVar, value);
    }

    public string RecipientsFile
    {
        get => _recipientsFile;
        set { Set(ref _recipientsFile, value); ClearError(); }
    }
    public ObservableCollection<string> Recipients
    {
        get => _recipients;
        set { Set(ref _recipients, value); ClearError(); }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set 
        { 
            Set(ref _errorMessage, value); 
            OnPropertyChanged(nameof(HasError)); 
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool Saved { get => _saved; private set => Set(ref _saved, value); }
    public static string ConfigPath => AppSettings.ConfigFilePath;

    /// <summary>Живая подсказка: найден ли пароль в переменной окружения.</summary>
    public string PasswordHint =>
        string.IsNullOrWhiteSpace(SmtpPasswordEnvVar)
            ? "Укажите имя переменной окружения"
            : SmtpSender.GetPasswordFromEnv(SmtpPasswordEnvVar) is { Length: > 0 }
                ? " ✓  Пароль найден в переменной окружения"
                : " ✗  Переменная окружения не задана или пуста";

    /// <summary>Найдены ли получатели в указанном файле.</summary>
    public string RecipientsHint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RecipientsFile)) return "Путь к файлу не указан";
            if (!File.Exists(RecipientsFile)) return "Путь к файлу не существует";
            if (Recipients.Count == 0)
            {
                return "В указанном файле получатели не найдены";
            } else
            {
                return $"Найдено получателей: {Recipients.Count}\n{string.Join('\n', Recipients)}";
            }
        }
    }

    public ICommand BrowseCredentialsCommand { get; }
    public ICommand BrowseRecipientsCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public Action? CloseAction { get; set; }

    public SettingsViewModel()
    {
        var cfg = AppSettings.Instance;

        _recipients = cfg.RejectionNotifyRecipients.ToObservableCollection();
        _recipientsFile = cfg.PathToRecipients;
        _credentialsFile = cfg.CredentialsFile;
        _sheetId = cfg.SheetId;
        _sqlConnectionString = cfg.SqlConnectionString;
        _smtpAddress = cfg.SmtpAddress;
        _smtpPortText = cfg.SmtpPort.ToString();
        _smtpUsername = cfg.SmtpUsername;
        _smtpPasswordEnvVar = cfg.SmtpPasswordEnvVar;
        RecipientsFile = cfg.PathToRecipients;
        BrowseCredentialsCommand = new RelayCommand(BrowseCredentials);
        BrowseRecipientsCommand = new RelayCommand(BrowseRecipients,
            () => true);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseAction?.Invoke());
        OnPropertyChanged(nameof(RecipientsHint));
    }

    private void BrowseCredentials()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Файл учётных данных Google",
            Filter = "JSON-файлы (*.json)|*.json|Все файлы (*.*)|*.*",
        };
        if (!string.IsNullOrWhiteSpace(CredentialsFile) && File.Exists(CredentialsFile))
            dlg.InitialDirectory = Path.GetDirectoryName(CredentialsFile);

        if (dlg.ShowDialog() == true)
            CredentialsFile = dlg.FileName;
    }

    private void BrowseRecipients()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Файл с получателями",
            Filter = "Все файлы (*.*)|*.*",
        };
        if (!string.IsNullOrWhiteSpace(RecipientsFile) && File.Exists(RecipientsFile))
            dlg.InitialDirectory = Path.GetDirectoryName(RecipientsFile);

        if (dlg.ShowDialog() == true)
            RecipientsFile = dlg.FileName;

        var types = new[]
        {
            ReceiversType.ProductionSupervisors,
            ReceiversType.ProcessEngineeringDepartment,
            ReceiversType.QualityControl
        };
        Recipients = types.SelectMany(t => Utils.ReadReceiversFromFile(t, RecipientsFile))
            .Distinct()
            .ToObservableCollection();
        OnPropertyChanged(nameof(RecipientsHint));
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(CredentialsFile))
        {
            ErrorMessage = "Укажите путь к файлу учётных данных Google.";
            return;
        }
        if (!File.Exists(CredentialsFile))
        {
            ErrorMessage = $"Файл учётных данных не найден:\n{CredentialsFile}";
            return;
        }
        if (string.IsNullOrWhiteSpace(SheetId))
        {
            ErrorMessage = "Укажите ID таблицы Google Sheets.";
            return;
        }
        if (!string.IsNullOrWhiteSpace(SmtpPortText) &&
            (!int.TryParse(SmtpPortText, out int port) || port is < 1 or > 65535))
        {
            ErrorMessage = "Порт SMTP должен быть числом от 1 до 65535.";
            return;
        }

        try
        {
            var cfg = AppSettings.Instance;
            cfg.CredentialsFile = CredentialsFile.Trim();
            cfg.SheetId = SheetId.Trim();
            cfg.SqlConnectionString = SqlConnectionString.Trim();
            cfg.SmtpAddress = SmtpAddress.Trim();
            cfg.SmtpPort = int.TryParse(SmtpPortText, out int p) ? p : 25;
            cfg.SmtpUsername = SmtpUsername.Trim();
            cfg.SmtpPasswordEnvVar = SmtpPasswordEnvVar.Trim();
            cfg.RejectionNotifyRecipients = Recipients.ToList();
            cfg.PathToRecipients = RecipientsFile;
            cfg.Save();

            AppSettings.Reload();
            Saved = true;
            CloseAction?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка сохранения: {ex.Message}";
        }
    }

    private void ClearError() => ErrorMessage = null;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        if (name == nameof(SmtpPasswordEnvVar))
            OnPropertyChanged(nameof(PasswordHint));
        return true;
    }
}