using Microsoft.Win32;
using QCTasks.Commands;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace QCTasks.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private string _credentialsFile;
    private string _sheetId;
    private string _sqlConnectionString;
    private string? _errorMessage;
    private bool _saved;

    public string CredentialsFile
    {
        get => _credentialsFile;
        set
        {
            SetField(ref _credentialsFile, value);
            ErrorMessage = null;
        }
    }

    public string SheetId
    {
        get => _sheetId;
        set
        {
            SetField(ref _sheetId, value);
            ErrorMessage = null;
        }
    }

    public string SqlConnectionString
    {
        get => _sqlConnectionString;
        set => SetField(ref _sqlConnectionString, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            SetField(ref _errorMessage, value);
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>true — настройки были сохранены (сигнал для MainViewModel на перезагрузку)</summary>
    public bool Saved
    {
        get => _saved;
        private set => SetField(ref _saved, value);
    }

    public ICommand BrowseCredentialsCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    // Команда закрытия окна — устанавливается из code-behind
    public Action? CloseAction { get; set; }

    public SettingsViewModel()
    {
        var cfg = AppSettings.Instance;
        _credentialsFile = cfg.CredentialsFile;
        _sheetId = cfg.SheetId;
        _sqlConnectionString = cfg.SqlConnectionString;

        BrowseCredentialsCommand = new RelayCommand(BrowseCredentials);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseAction?.Invoke());
    }

    private void BrowseCredentials()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите файл учётных данных Google",
            Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*"
        };

        if (!string.IsNullOrWhiteSpace(CredentialsFile) &&
            File.Exists(CredentialsFile))
            dlg.InitialDirectory = Path.GetDirectoryName(CredentialsFile);

        if (dlg.ShowDialog() == true)
            CredentialsFile = dlg.FileName;
    }

    private void Save()
    {
        // Валидация
        if (string.IsNullOrWhiteSpace(CredentialsFile))
        {
            ErrorMessage = "Укажите путь к файлу учётных данных.";
            return;
        }
        if (!File.Exists(CredentialsFile))
        {
            ErrorMessage = "Файл учётных данных не найден.";
            return;
        }
        if (string.IsNullOrWhiteSpace(SheetId))
        {
            ErrorMessage = "Укажите ID таблицы Google Sheets.";
            return;
        }

        try
        {
            var cfg = AppSettings.Instance;
            cfg.CredentialsFile = CredentialsFile.Trim();
            cfg.SheetId = SheetId.Trim();
            cfg.SqlConnectionString = SqlConnectionString.Trim();
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}