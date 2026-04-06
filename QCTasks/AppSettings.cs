using Newtonsoft.Json;
using QCTasks.Models;
using System.Collections.Generic;
using System.IO;

namespace QCTasks;

public sealed class AppSettings
{
    private static AppSettings? _instance;
    private static readonly object _lock = new();

    public static AppSettings Instance
    {
        get
        {
            if (_instance is null)
                lock (_lock)
                    _instance ??= LoadInternal();
            return _instance;
        }
    }

    public const string BasePath = @"C:\ProgramData\dece1ver\QCTasks";
    public static readonly string ConfigFilePath = Path.Combine(BasePath, "config.json");

    public string CredentialsFile { get; set; } = "";
    public string SheetId { get; set; } = "";

    public string SqlConnectionString { get; set; } = "";

    public string SmtpAddress { get; set; } = "";
    public int SmtpPort { get; set; } = 25;
    public string SmtpUsername { get; set; } = "";
    public string PathToRecipients { get; set; } = "";

    /// <summary>
    /// Имя переменной окружения пользователя, хранящей SMTP-пароль.
    /// Пароль не пишется в конфиг — только имя переменной.
    /// </summary>
    public string SmtpPasswordEnvVar { get; set; } = "NOTIFY_SMTP_PWD";

    /// <summary>Адреса, которым уходит письмо при отклонении детали.</summary>
    public List<string> RejectionNotifyRecipients { get; set; } = new();

    [JsonIgnore]
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(CredentialsFile) &&
        !string.IsNullOrWhiteSpace(SheetId);

    [JsonIgnore]
    public bool IsSmtpConfigured =>
        !string.IsNullOrWhiteSpace(SmtpAddress) &&
        !string.IsNullOrWhiteSpace(SmtpUsername);

    /// <summary> Путь к локальному списку получателей уведомлений </summary>
    [JsonIgnore] public static readonly string LocalMailRecieversFile = Path.Combine(BasePath, "recievers");

    /// <summary> Текущий пользователь </summary>
    [JsonIgnore] public static QcUser? CurrentUser { get; set; }

    private AppSettings() { }

    private static AppSettings LoadInternal()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
                return CreateDefault();

            var json = File.ReadAllText(ConfigFilePath);
            return JsonConvert.DeserializeObject<AppSettings>(json) ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    /// <summary>Перечитывает конфиг с диска. Вызывать после сохранения настроек.</summary>
    public static AppSettings Reload()
    {
        lock (_lock)
        {
            _instance = LoadInternal();
            return _instance;
        }
    }

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    /// <summary>Атомарное сохранение через tmp-файл.</summary>
    public void Save()
    {
        Directory.CreateDirectory(BasePath);
        var json = JsonConvert.SerializeObject(this, _jsonSettings);
        var tmpPath = ConfigFilePath + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Copy(tmpPath, ConfigFilePath, overwrite: true);
        File.Delete(tmpPath);
    }

    private static AppSettings CreateDefault()
    {
        var cfg = new AppSettings();
        Directory.CreateDirectory(BasePath);
        File.WriteAllText(ConfigFilePath,
            JsonConvert.SerializeObject(cfg, _jsonSettings));
        return cfg;
    }
}