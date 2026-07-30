using Newtonsoft.Json;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
        [JsonIgnore]
        public static HashSet<string> SerialParts
        {
            get => Core.DomainSettings.SerialParts;
            set => Core.DomainSettings.SerialParts = value;
        }

        [JsonIgnore]
        public static readonly string[] ShiftTypes = new string[] { "День", "Ночь" };

        [JsonIgnore]
        public List<(string Reason, bool RequireComment)> SetupReasons
        {
            get => Core.DomainSettings.SetupReasons;
            set => Core.DomainSettings.SetupReasons = value;
        }
        [JsonIgnore]
        public List<(string Reason, bool RequireComment)> MachiningReasons
        {
            get => Core.DomainSettings.MachiningReasons;
            set => Core.DomainSettings.MachiningReasons = value;
        }
        [JsonIgnore]
        public List<string> UnspecifiedDowntimesReasons = new();
        [JsonIgnore]
        public static RemeLogFeature EnabledFeatures
        {
            get => Core.DomainSettings.EnabledFeatures;
            set => Core.DomainSettings.EnabledFeatures = value;
        }
        [JsonIgnore]
        public static bool FeaturesExplicitlySet
        {
            get => Core.DomainSettings.FeaturesExplicitlySet;
            set => Core.DomainSettings.FeaturesExplicitlySet = value;
        }

        [JsonIgnore]
        public static double MaxSetupLimit
        {
            get => Core.DomainSettings.MaxSetupLimit;
            set => Core.DomainSettings.MaxSetupLimit = value;
        }
        [JsonIgnore]
        public static Dictionary<string, double> MaxSetupLimits
        {
            get => Core.DomainSettings.MaxSetupLimits;
            set => Core.DomainSettings.MaxSetupLimits = value;
        }
        [JsonIgnore]
        public static double FallbackMaxSetupLimitValue
        {
            get => Core.DomainSettings.FallbackMaxSetupLimitValue;
            set => Core.DomainSettings.FallbackMaxSetupLimitValue = value;
        }
        [JsonIgnore]
        public static double LongSetupLimit
        {
            get => Core.DomainSettings.LongSetupLimit;
            set => Core.DomainSettings.LongSetupLimit = value;
        }
        [JsonIgnore]
        public static string NcArchivePath
        {
            get => Core.DomainSettings.NcArchivePath;
            set => Core.DomainSettings.NcArchivePath = value;
        }
        [JsonIgnore]
        public static string NcIntermediatePath
        {
            get => Core.DomainSettings.NcIntermediatePath;
            set => Core.DomainSettings.NcIntermediatePath = value;
        }
        [JsonIgnore]
        public static string[] Administrators
        {
            get => Core.DomainSettings.Administrators;
            set => Core.DomainSettings.Administrators = value;
        }
        [JsonIgnore]
        public static string[] Users { get; set; } = Array.Empty<string>();
        [JsonIgnore]
        public static string[] CncOperations
        {
            get => Core.DomainSettings.CncOperations;
            set => Core.DomainSettings.CncOperations = value;
        }
        [JsonIgnore]
        public static string[] EngineerComments
        {
            get => Core.DomainSettings.EngineerComments;
            set => Core.DomainSettings.EngineerComments = value;
        }
        [JsonIgnore]
        public static DateTime[] Holidays
        {
            get => Core.DomainSettings.Holidays;
            set => Core.DomainSettings.Holidays = value;
        }
        [JsonIgnore]
        public static string? PcaReportPath
        {
            get => Core.DomainSettings.PcaReportPath;
            set => Core.DomainSettings.PcaReportPath = value;
        }
        [JsonIgnore]
        public static string AiIp
        {
            get => Core.DomainSettings.AiIp;
            set => Core.DomainSettings.AiIp = value;
        }
        public const int AiPort = 5050;
        [JsonIgnore]
        public static string AiModel
        {
            get => Core.DomainSettings.AiModel;
            set => Core.DomainSettings.AiModel = value;
        }
        /// <summary>
        /// Версия схемы БД, зафиксированная в cnc_remelog_config при последнем "Обновлении БД"
        /// (любой сборкой). Читается при каждом обновлении настроек.
        /// </summary>
        [JsonIgnore]
        public static int SchemaVersion
        {
            get => Core.DomainSettings.SchemaVersion;
            set => Core.DomainSettings.SchemaVersion = value;
        }
        /// <summary>
        /// Версия схемы, на которую рассчитана ЭТА сборка. Бампать вместе с любым изменением
        /// структуры БД (новые обязательные столбцы, смена семантики существующих и т.п.) —
        /// защита от старой сборки поверх более новой БД: если SchemaVersion в БД выше этого
        /// значения, работа с данными блокируется (см. MainWindowViewModel.LoadPartsAsync).
        /// </summary>
        // 2 — переопределение причин отклонений аналитиком: 8 столбцов в parts
        //     (SetupReasonOverride и т.д.). Старая сборка не знает про этот слой и при
        //     сохранении затрёт отметку мастера напрямую, как раньше.
        public const int RequiredSchemaVersion = 2;
        [JsonIgnore]
        public static int PartsHistoryMaxRecords { get; set; } = 5;
        [JsonIgnore]
        public static int PartsHistoryMaxDaysBack { get; set; } = 720;
        public bool AiThinkingEnabled { get; set; } = false;

        public List<string> MachineInspectionCalendarSelectedMachines { get; set; } = new();


        /// <summary> Режим отладки </summary>
        public bool DebugMode
        {
            get => Core.DomainSettings.DebugMode;
            set => Core.DomainSettings.DebugMode = value;
        }
        /// <summary> Источник информации </summary>
        public DataSource DataSource { get; set; }

        /// <summary> Путь к файлу с разрядами </summary>
        public string? QualificationSourcePath { get; set; }

        /// <summary> Путь к файлу c доступом к гугл таблице </summary>
        public string? GoogleCredentialPath { get; set; }

        /// <summary> ID таблицы СЗН </summary>
        public string? AssignedPartsSheet { get; set; }
        /// <summary> Строка подключения к БД </summary>
        public string? ConnectionString
        {
            get => Core.DomainSettings.ConnectionString;
            set => Core.DomainSettings.ConnectionString = value;
        }

        public bool InstantUpdateOnMainWindow { get; set; }

        public User? User { get; set; }

        /// <summary> Пользовательские профили видимости колонок PartsInfoWindow </summary>
        public List<ColumnProfile> ColumnProfiles { get; set; } = new();


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