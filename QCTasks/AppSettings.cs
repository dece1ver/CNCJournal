using Newtonsoft.Json;
using System.IO;

namespace QCTasks;

public sealed class AppSettings
{
    // ── Singleton ──────────────────────────────────────────────────────────
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

    // ── Пути ──────────────────────────────────────────────────────────────
    public const string BasePath = @"C:\ProgramData\dece1ver\QCTasks";
    public static readonly string ConfigFilePath = Path.Combine(BasePath, "config.json");

    // ── Поля конфига ──────────────────────────────────────────────────────
    public string CredentialsFile { get; set; } = "";
    public string SheetId { get; set; } = "";
    public string SqlConnectionString { get; set; } = "";

    // ── Валидация ─────────────────────────────────────────────────────────
    /// <summary>Все обязательные поля заполнены.</summary>
    [JsonIgnore]
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(CredentialsFile) &&
        !string.IsNullOrWhiteSpace(SheetId);

    // ── Конструктор приватный ─────────────────────────────────────────────
    private AppSettings() { }

    // ── Загрузка ──────────────────────────────────────────────────────────
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

    /// <summary>
    /// Перечитывает конфиг с диска и обновляет текущий Singleton.
    /// Вызывать после сохранения из окна настроек.
    /// </summary>
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
        Formatting = Formatting.Indented
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