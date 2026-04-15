using Newtonsoft.Json;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using remeLog.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using JsonException = System.Text.Json.JsonException;

namespace remeLog.Infrastructure
{
    /// <summary>
    /// Настройки приложения
    /// </summary>
    public class AppSettings
    {
        private AppSettings()
        {

        }

        [JsonIgnore] private static AppSettings? _Instance;
        [JsonIgnore] public static AppSettings Instance => _Instance ??= new AppSettings();

        /// <summary> Директория для хранения всякого </summary>
        [JsonIgnore] public const string BasePath = "C:\\ProgramData\\dece1ver\\remeLog";

        /// <summary> Путь к файлу конфигурации </summary>
        [JsonIgnore] public static readonly string ConfigFilePath = Path.Combine(BasePath, "config.json");

        /// <summary> Путь к файлу конфигурации </summary>
        [JsonIgnore] public static readonly string ConfigBackupPath = Path.Combine(BasePath, "config.backup.json");

        /// <summary> Путь к файлу конфигурации </summary>
        [JsonIgnore] public static readonly string ConfigTempPath = Path.Combine(BasePath, "config.temp.json");

        /// <summary> Путь к файлу логов </summary>
        [JsonIgnore] public static readonly string LogFile = Path.Combine(BasePath, "log");

        /// <summary> Нормализованные имена серийных деталей </summary>
        [JsonIgnore] public static HashSet<string> SerialParts { get; set; } = new();

        [JsonIgnore]
        public static readonly string[] ShiftTypes = new string[] { "День", "Ночь" };

        [JsonIgnore]
        public List<(string Reason, bool RequireComment)> SetupReasons { get; set; } = new();
        [JsonIgnore]
        public List<(string Reason, bool RequireComment)> MachiningReasons { get; set; } = new();
        [JsonIgnore]
        public List<string> UnspecifiedDowntimesReasons = new();
        [JsonIgnore]
        public static double MaxSetupLimit { get; set; } = 2;
        [JsonIgnore]
        public static Dictionary<string, double> MaxSetupLimits { get; set; } = new();
        [JsonIgnore]
        public static double FallbackMaxSetupLimitValue { get; set; } = 1.5;
        [JsonIgnore]
        public static double LongSetupLimit { get; set; } = 240;
        [JsonIgnore]
        public static string NcArchivePath { get; set; } = "";
        [JsonIgnore]
        public static string NcIntermediatePath { get; set; } = "";
        [JsonIgnore]
        public static string[] Administrators { get; set; } = Array.Empty<string>();
        [JsonIgnore]
        public static string[] Users { get; set; } = Array.Empty<string>();
        [JsonIgnore]
        public static string[] CncOperations { get; set; } = Array.Empty<string>();
        [JsonIgnore]
        public static string? PcaReportPath { get; set; }

        /// <summary> Режим отладки </summary>
        public bool DebugMode { get; set; }
        /// <summary> Источник информации </summary>
        public DataSource DataSource { get; set; }

        /// <summary> Путь к файлу с разрядами </summary>
        public string? QualificationSourcePath { get; set; }

        /// <summary> Путь к файлу c доступом к гугл таблице </summary>
        public string? GoogleCredentialPath { get; set; }

        /// <summary> ID таблицы СЗН </summary>
        public string? AssignedPartsSheet { get; set; }
        /// <summary> Строка подключения к БД </summary>
        public string? ConnectionString { get; set; }

        public bool InstantUpdateOnMainWindow { get; set; }

        public User? User { get; set; }


        /// <summary> Создает конфиг с параметрами по-умолчанию </summary>
        private void CreateBaseConfig()
        {
            if (File.Exists(ConfigFilePath)) File.Delete(ConfigFilePath);
            if (File.Exists(ConfigBackupPath)) File.Delete(ConfigBackupPath);
            if (!Directory.Exists(BasePath))
            {
                try
                {
                    Directory.CreateDirectory(BasePath);
                }
                catch (Exception ex)
                {
                    Util.WriteLog(ex, "Не удалось создать директорию для данных приложения.");
                }
            }
            DataSource = new DataSource(DataSource.Types.Database);
            InstantUpdateOnMainWindow = false;
            QualificationSourcePath = "";
            GoogleCredentialPath = "";
            AssignedPartsSheet = "";
            ConnectionString = "";
            User = null;
            Util.WriteLog("Параметры заполнены, сохранение.");
            Save();
            Util.WriteLog("Сохранение завершено.");
        }

        /// <summary>
        /// Читает конфиг, если возникает исключение, то создает конфиг по-умолчанию и читает его.
        /// </summary>
        public void ReadConfig()
        {
            if (!File.Exists(ConfigFilePath) && File.Exists(ConfigBackupPath))
            {
                Util.WriteLog("Основной файл конфигурации отсутствует, копирование из резервного.");
                try
                {
                    File.Copy(ConfigBackupPath, ConfigFilePath, true);
                } 
                catch (Exception ex) 
                {
                    Util.WriteLog(ex, "Не удалось скопировать резервный файл конфигурации.");
                }
            }
            else if (!File.Exists(ConfigFilePath) && !File.Exists(ConfigBackupPath))
            {
                Util.WriteLog("Файл конфигурации отсутствует, создание нового.");
                CreateBaseConfig();
            }
            var json = File.ReadAllText(ConfigFilePath);
            try
            {
                var settings = new JsonSerializerSettings()
                {
                    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                JsonConvert.PopulateObject(json, Instance, settings);
            }
            catch (Exception exception)
            {
                switch (exception)
                {
                    case Newtonsoft.Json.JsonException:
                        Util.WriteLog("Некорректный файл конфигурации.");
                        break;
                    default:
                        Util.WriteLog(exception, "Ошибка при чтении конфигурации.");
                        break;
                }

                if (File.Exists(ConfigFilePath)) File.Copy(ConfigFilePath, Path.Combine(BasePath, $"{DateTime.Now:dd-mm-yyyy-hh-mm-ss}_r"), true);

                if (File.Exists(ConfigBackupPath))
                {
                    try
                    {
                        JsonDocument.Parse(File.ReadAllText(ConfigBackupPath));
                        File.Copy(ConfigBackupPath, ConfigFilePath, true);
                    }
                    catch (JsonException)
                    {
                        var msg = "Резервный файл конфигурации некорректен, установка конфигурации по умолчанию.";
                        Util.WriteLog(msg);
                        CreateBaseConfig();
                    }
                    catch (Exception ex)
                    {
                        var msg = "Неизвестная ошибка при чтении резервного файла конфигурации, установка конфигурации по умолчанию.";
                        Util.WriteLog(ex, msg);
                        CreateBaseConfig();
                    }
                }
                else
                {
                    CreateBaseConfig();
                }
                ReadConfig();
            }
        }

        /// <summary> Сохраняет конфиг </summary>
        public static void Save()
        {

            var settings = new JsonSerializerSettings()
            {
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };
            try
            {
                if (File.Exists(ConfigFilePath)) File.Copy(ConfigFilePath, ConfigTempPath, true);
                if (File.Exists(ConfigFilePath)) File.Delete(ConfigFilePath);
                var json = JsonConvert.SerializeObject(Instance, Formatting.Indented, settings);
                File.WriteAllText(ConfigFilePath, json);
                if (File.Exists(ConfigTempPath)) File.Delete(ConfigTempPath);
                try
                {
                    JsonDocument.Parse(json);
                    File.Copy(ConfigFilePath, ConfigBackupPath, true);
                }
                catch (JsonException ex)
                {
                    var msg = "Записан некорректный файл конфигурации, восстановление";
                    Util.WriteLog(ex, msg);
                    File.Copy(ConfigBackupPath, ConfigFilePath, true);
                }
                catch (Exception ex)
                {
                    var msg = "Неизвестная ошибка при создании бэкапа конфигурации";
                    Util.WriteLog(ex, msg);
                }

            }
            catch (UnauthorizedAccessException)
            {
                var msg = "Ошибка при сохранении файла конфигурации (Доступ запрещен).";
                Util.WriteLog(msg);
                if (!File.Exists(ConfigFilePath) && File.Exists(ConfigTempPath)) File.Copy(ConfigTempPath, ConfigFilePath, true);
            }
            catch (IOException)
            {
                var msg = "Ошибка при сохранении файла конфигурации (Ошибка ввода/вывода).";
                Util.WriteLog(msg);
                if (!File.Exists(ConfigFilePath) && File.Exists(ConfigTempPath)) File.Copy(ConfigTempPath, ConfigFilePath, true);
            }
            catch (Exception ex)
            {
                var msg = "Ошибка при сохранении файла конфигурации (Неизвестная ошибка).";
                Util.WriteLog(ex, msg);
                try
                {
                    if (File.Exists(ConfigTempPath)) File.Copy(ConfigTempPath, ConfigFilePath, true);
                }
                catch { }

                if (File.Exists(ConfigTempPath)) File.Copy(ConfigTempPath, Path.Combine(BasePath, $"{DateTime.Now:dd-mm-yyyy-hh-mm-ss}_w"), true);
                if (File.Exists(ConfigFilePath)) Debug.WriteLine("Восстановлен бэкап конфигурации.");
            }
        }
    }
}