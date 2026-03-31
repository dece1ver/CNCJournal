using eLog.Infrastructure.Extensions;
using eLog.Models;
using libeLog.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using JsonException = System.Text.Json.JsonException;

namespace eLog.Infrastructure
{
    /// <summary>
    /// Настройки приложения
    /// </summary>
    public class AppSettings : INotifyPropertyChanged
    {
        private static readonly SettingsManager _settingsManager = new SettingsManager();
        private AppSettings()
        {

        }

        [JsonIgnore] private static AppSettings? _Instance;
        [JsonIgnore] public static AppSettings Instance => _Instance ??= new AppSettings();
        /// <summary> Имя переменной окружения пользователя, хранящей SMTP-пароль. </summary>
        [JsonIgnore] public const string SmtpPasswordEnvVar = "NOTIFY_SMTP_PWD";
        /// <summary> Директория для хранения всякого </summary>
        [JsonIgnore] public const string BasePath = "C:\\ProgramData\\dece1ver\\eLog";

        /// <summary> Локальный путь для бэкапа таблицы. </summary>
        [JsonIgnore] public static readonly string XlReservedPath = Path.Combine(BasePath, "backup.xlsm");

        /// <summary> Путь к файлу конфигурации </summary>
        [JsonIgnore] public static readonly string ConfigFilePath = Path.Combine(BasePath, "config.json");

        /// <summary> Путь к файлу конфигурации </summary>
        [JsonIgnore] public static readonly string ConfigBackupPath = Path.Combine(BasePath, "config.backup.json");

        /// <summary> Путь к файлу конфигурации </summary>
        [JsonIgnore] public static readonly string ConfigTempPath = Path.Combine(BasePath, "config.temp.json");

        /// <summary> Путь к локальному списку заказов </summary>
        [JsonIgnore] public static readonly string LocalOrdersFile = Path.Combine(BasePath, "orders.xlsx");

        /// <summary> Путь к резервному списку заказов </summary>
        [JsonIgnore] public static readonly string BackupOrdersFile = Path.Combine(BasePath, "orders-backup.xlsx");

        /// <summary> Путь к локальному списку получателей уведомлений </summary>
        [JsonIgnore] public static readonly string LocalMailRecieversFile = Path.Combine(BasePath, "recievers");

        /// <summary> Список получателей писем о долгой наладке </summary>
        [JsonIgnore] public static List<string> LongSetupsMailRecievers = new();
        /// <summary> Список получателей писем о поиске инструмента </summary>
        [JsonIgnore] public static List<string> ToolSearchMailRecievers = new();

        /// <summary> Путь к файлу логов </summary>
        [JsonIgnore] public static readonly string LogFile = Path.Combine(BasePath, "log");



        private Machine? _Machine;
        private string _UpdatePath;
        private string _GoogleCredentialsPath;
        private string _GsId;
        private bool _WiteToGs;
        private string _OrdersSourcePath;
        private string[] _OrderQualifiers;
        private DeepObservableCollection<Operator> _Operators;
        private string _CurrentShift;
        private DeepObservableCollection<Part> _Parts;
        private bool _IsShiftStarted;
        private Operator? _CurrentOperator;
        private StorageType _StorageType;
        private string _ConnectionString;
        private bool _DebugMode;
        private string _SmtpAddress;
        private int _SmtpPort;
        private string _SmtpUsername;
        private string _PathToRecievers;


        

        /// <summary> Текущий станок </summary>
        public Machine? Machine
        {
            get => _Machine;
            set => Set(ref _Machine, value);
        }

        /// <summary> Путь к общей таблице </summary>
        public string UpdatePath
        {
            get => _UpdatePath;
            set => Set(ref _UpdatePath, value);
        }

        /// <summary> Путь к файлу с данными для google api </summary>
        public string GoogleCredentialsPath
        {
            get => _GoogleCredentialsPath;
            set => Set(ref _GoogleCredentialsPath, value);
        }

        /// <summary> ID гугл таблицы </summary>
        public string GsId
        {
            get => _GsId;
            set => Set(ref _GsId, value);
        }


        
        /// <summary> Писать ли в гугл таблицу </summary>
        public bool WiteToGs
        {
            get => _WiteToGs;
            set => Set(ref _WiteToGs, value);
        }


        /// <summary> Путь к таблице с номенклатурой </summary>
        public string OrdersSourcePath
        {
            get => _OrdersSourcePath;
            set => Set(ref _OrdersSourcePath, value);
        }

        /// <summary> Квалификаторы заказов </summary>
        public string[] OrderQualifiers
        {
            get => _OrderQualifiers;
            set => Set(ref _OrderQualifiers, value);
        }

        /// <summary> Список операторов </summary>
        public DeepObservableCollection<Operator> Operators
        {
            get => _Operators;
            set => Set(ref _Operators, value);
        }

        /// <summary> Текущий оператор </summary>
        public Operator? CurrentOperator
        {
            get => _CurrentOperator;
            set
                => Set(ref _CurrentOperator, value);
        }
        /// <summary> Запущена ли смена </summary>
        public bool IsShiftStarted
        {
            get => _IsShiftStarted;
            set => Set(ref _IsShiftStarted, value);
        }
        /// <summary> Текущая смена </summary>
        public string CurrentShift
        {
            get => _CurrentShift;
            set => Set(ref _CurrentShift, value);
        }
        /// <summary> Список деталей </summary>
        public DeepObservableCollection<Part> Parts
        {
            get => _Parts;
            set => Set(ref _Parts, value);
        }

        /// <summary> Тип хранения </summary>
        public StorageType StorageType
        {
            get => _StorageType;
            set => Set(ref _StorageType, value);
        }

        /// <summary> Строка подключения к БД </summary>
        public string ConnectionString
        {
            get => _ConnectionString;
            set => Set(ref _ConnectionString, value);
        }


        /// <summary> Адрес SMTP сервера </summary>
        public string SmtpAddress
        {
            get => _SmtpAddress;
            set => Set(ref _SmtpAddress, value);
        }

        /// <summary> Порт SMTP </summary>
        public int SmtpPort
        {
            get => _SmtpPort;
            set => Set(ref _SmtpPort, value);
        }


        /// <summary> Отправитель </summary>
        public string SmtpUsername
        {
            get => _SmtpUsername;
            set => Set(ref _SmtpUsername, value);
        }

        /// <summary> Путь к файлу с получателями </summary>
        public string PathToRecievers
        {
            get => _PathToRecievers;
            set => Set(ref _PathToRecievers, value);
        }


        private int _TimerForNotify;
        /// <summary> Таймер до уведомления (в часах) </summary>
        public int TimerForNotify
        {
            get => _TimerForNotify;
            set => Set(ref _TimerForNotify, value);
        }


        private bool _EnableWriteShiftHandover;
        /// <summary> Передача смены </summary>
        public bool EnableWriteShiftHandover
        {
            get => _EnableWriteShiftHandover;
            set => Set(ref _EnableWriteShiftHandover, value);
        }


        private List<ShiftHandOverInfo> _NotWritedShiftHandovers;
        /// <summary> Не записанные передачи смен </summary>
        public List<ShiftHandOverInfo> NotWritedShiftHandovers
        {
            get => _NotWritedShiftHandovers;
            set => Set(ref _NotWritedShiftHandovers, value);
        }


        private List<string> _NotSendedToolComments;
        /// <summary> Не отправленные комментарии по поиску инструмента </summary>
        public List<string> NotSendedToolComments 
        {
            get => _NotSendedToolComments;
            set => Set(ref _NotSendedToolComments, value);
        }


        private List<string> _SearchToolTypes;
        /// <summary> Типы инструмента при поиске </summary>
        public List<string> SearchToolTypes
        {
            get => _SearchToolTypes;
            set => Set(ref _SearchToolTypes, value);
        }


        /// <summary> Режим отладки </summary>
        public bool DebugMode
        {
            get => _DebugMode;
            set => Set(ref _DebugMode, value);
        }

        private List<Machine> _Machines;
        /// <summary> Станки </summary>
        public List<Machine> Machines
        {
            get => _Machines;
            set => Set(ref _Machines, value);
        }

        private HashSet<string> _SerialParts;
        /// <summary> Серийные детали </summary>
        public HashSet<string> SerialParts
        {
            get => _SerialParts;
            set => Set(ref _SerialParts, value);
        }

        /// <summary> Создает конфиг с параметрами по-умолчанию </summary>
        private void CreateBaseConfig()
        {
            if (File.Exists(ConfigFilePath)) File.Move(ConfigFilePath, ConfigFilePath + $".mvd{DateTime.Now.Ticks}");
            if (File.Exists(ConfigBackupPath)) File.Move(ConfigBackupPath, ConfigBackupPath + $".mvd{DateTime.Now.Ticks}");
            if (!Directory.Exists(BasePath)) Directory.CreateDirectory(BasePath);
            Machines = new();
            Machine = null;
            Operators = new DeepObservableCollection<Operator>();
            CurrentShift = Text.DayShift;
            Parts = new DeepObservableCollection<Part>();
            UpdatePath = string.Empty;
            OrdersSourcePath = string.Empty;
            OrderQualifiers = new[]
            {
                Text.WithoutOrderItem,
                "УЧ",
                "ФЛ",
                "БП",
                "СУ",
                "УУ",
                "ЗУ",
                "СЛ",
            };
            SearchToolTypes = new List<string>() { "Н/Д" };
            StorageType = new StorageType(StorageType.Types.Excel);
            ConnectionString = string.Empty;
            IsShiftStarted = false;
            TimerForNotify = 4;
            EnableWriteShiftHandover = true;
            PathToRecievers = "";
            NotWritedShiftHandovers = new();
            NotSendedToolComments = new();
            var settings = new JsonSerializerSettings()
            {
                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };
            var json = JsonConvert.SerializeObject(Instance, settings);
            File.WriteAllText(ConfigFilePath, json);
        }

        /// <summary>
        /// Читает конфиг, если возникает исключение, то создает конфиг по-умолчанию и читает его.
        /// </summary>
        public void ReadConfig()
        {
            if (!File.Exists(ConfigFilePath) && File.Exists(ConfigBackupPath))
            {
                File.Copy(ConfigBackupPath, ConfigFilePath, true);
            }
            else if (!File.Exists(ConfigFilePath) && !File.Exists(ConfigBackupPath))
            {
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

                LongSetupsMailRecievers = Util.GetMailReceivers(Util.ReceiversType.LongSetup);
                ToolSearchMailRecievers = Util.GetMailReceivers(Util.ReceiversType.ToolSearch);

                GoogleCredentialsPath ??= "";
                GsId ??= "";
                OrdersSourcePath ??= "";
                OrderQualifiers ??= new[]
                {
                    Text.WithoutOrderItem,
                    "УЧ",
                    "ФЛ",
                    "БП",
                    "СУ",
                    "УУ",
                    "ЗУ",
                    "СЛ",
                };
                SearchToolTypes ??= new List<string> { "Н/Д" };
                CurrentOperator ??= new()
                {
                    LastName = "Бабохин",
                    FirstName = "Кирилл",
                    Patronymic = "Георгиевич"
                };

                Operators ??= new() { CurrentOperator };
                CurrentShift ??= "День";
                Parts ??= new();
                ConnectionString ??= "";
                SmtpAddress ??= "";
                SmtpUsername ??= "";
                PathToRecievers ??= "";
                NotSendedToolComments ??= new();
                if (string.IsNullOrWhiteSpace(Instance.UpdatePath)) Instance.UpdatePath = Database.TryGetUpdatePath();
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
                        Debug.WriteLine(msg);
                        Task.Run(() => Util.WriteLogAsync(msg));
                        CreateBaseConfig();
                    }
                    catch (Exception ex)
                    {
                        var msg = "Неизвестная ошибка при чтении резервного файла конфигурации, установка конфигурации по умолчанию.";
                        Debug.WriteLine(msg);
                        Task.Run(() => Util.WriteLogAsync(ex, msg));
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

        
        /// <summary>
        /// Обновляет конфиг
        /// </summary>
        public static void Save()
        {
            _settingsManager.ScheduleSave();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string PropertyName = null!)
        {
            var handlers = PropertyChanged;
            if (handlers is null) return;

            var invokationList = handlers.GetInvocationList();
            var args = new PropertyChangedEventArgs(PropertyName);

            foreach (var action in invokationList)
            {
                if (action.Target is DispatcherObject dispatcherObject)
                {
                    dispatcherObject.Dispatcher.Invoke(action, this, args);
                }
                else
                {
                    action.DynamicInvoke(this, args);
                }
            }
        }

        protected virtual bool Set<T>(ref T field, T value, [CallerMemberName] string PropertyName = null!)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(PropertyName);
            _settingsManager.ScheduleSave();
            return true;
        }
    }
}