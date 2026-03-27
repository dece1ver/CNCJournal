using Newtonsoft.Json;
using System.IO;

namespace QCTasks;

public sealed class AppSettings
{
    private static readonly Lazy<AppSettings> _instance = new(LoadInternal);

    public static AppSettings Instance => _instance.Value;

    public const string BasePath = @"C:\ProgramData\dece1ver\QCTasks";
    public static readonly string ConfigFilePath = Path.Combine(BasePath, "config.json");

    public string CredentialsFile { get; set; } = "";
    public string SheetId { get; set; } = "";

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        Formatting = Formatting.Indented
    };

    private AppSettings() { }

    /// <summary>
    /// Загрузка конфига
    /// </summary>
    private static AppSettings LoadInternal()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
                return CreateDefault();

            var json = File.ReadAllText(ConfigFilePath);
            var cfg = JsonConvert.DeserializeObject<AppSettings>(json);

            return cfg ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    /// <summary>
    /// Сохранение конфига
    /// </summary>
    public void Save()
    {
        Directory.CreateDirectory(BasePath);

        var json = JsonConvert.SerializeObject(this, _jsonSettings);

        var tempFile = ConfigFilePath + ".tmp";
        File.WriteAllText(tempFile, json);

        File.Copy(tempFile, ConfigFilePath, true);
        File.Delete(tempFile);
    }

    /// <summary>
    /// Создание дефолтного конфига
    /// </summary>
    private static AppSettings CreateDefault()
    {
        var cfg = new AppSettings();
        Directory.CreateDirectory(BasePath);

        var json = JsonConvert.SerializeObject(cfg, _jsonSettings);
        File.WriteAllText(ConfigFilePath, json);

        return cfg;
    }
}