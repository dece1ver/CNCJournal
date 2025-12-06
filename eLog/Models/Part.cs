using eLog.Infrastructure;
using libeLog;
using libeLog.Extensions;
using libeLog.Models;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace eLog.Models
{
    /// <summary>
    /// Информация о детали.
    /// Статусные свойства выводятся из временных свойств. По-умолчанию завершающие временные свойства присваиваются в DateTime.MinValue. После присваивания времен деталь считается завершенной.
    /// </summary>
    public class Part : INotifyPropertyChanged
    {
        private string _Name;
        private string _Number;
        private string _Order;
        private byte _Setup;
        private int _TotalCount;
        private string _Shift;
        private double _FinishedCount;
        private int _DefectiveCount;
        private double _SetupTimePlan;
        private double _SingleProductionTimePlan;
        private Operator _Operator;
        private TimeSpan _MachineTime;
        private DateTime _StartSetupTime;
        private DateTime _StartMachiningTime;
        private DateTime _EndMachiningTime;
        private bool _LongSetupNotifySended;
        private bool _NonConformingWorkpiece;
        private bool _NoProgram;
        private bool _NoDocumentation;
        private bool _LackOfSkills;
        private bool _InsufficientTools;
        private bool _InsufficientEquipment;
        private bool _NeedTechnicalHelp;
        private bool _NeedSeniorHelp;
        private DeepObservableCollection<DownTime> _DownTimes;
        private int _Id;
        private bool _IsSynced;
        private string _OperatorComments;
        private PartTaskInfo _TaskInfo;
        private bool _IsTaskStatusWritten;
        private bool _IsSerial;

        public Guid Guid { get; init; }

        public enum State { Finished, PartialSetup, InProgress }

        public enum PartTaskInfo { NoData, InList, NotInList }

        /// <summary> Наименование </summary>
        public string Name
        {
            get => _Name;
            set => Set(ref _Name, value);
        }

        /// <summary> Обозначение </summary>
        public string Number
        {
            get => _Number;
            set => Set(ref _Number, value);
        }

        /// <summary> Заказ </summary>
        public string Order
        {
            get => _Order;
            set => Set(ref _Order, value);
        }

        /// <summary> Текущий установ </summary>
        public byte Setup
        {
            get => _Setup;
            set => Set(ref _Setup, value);
        }

        /// <summary> Количество брака </summary>
        public int DefectiveCount
        {
            get => _DefectiveCount;
            set => Set(ref _DefectiveCount, value);
        }

        /// <summary> Количество по заказу</summary>
        public int TotalCount
        {
            get => _TotalCount;
            set => Set(ref _TotalCount, value);
        }

        /// <summary> Смена </summary>
        public string Shift
        {
            get => _Shift;
            set => Set(ref _Shift, value);
        }

        /// <summary> Оператор </summary>
        public Operator Operator
        {
            get => _Operator;
            set => Set(ref _Operator, value);
        }

        /// <summary> Отправлено ли уведомление о длительной наладке </summary>
        public bool LongSetupNotifySended
        {
            get => _LongSetupNotifySended;
            set => Set(ref _LongSetupNotifySended, value);
        }

        /// <summary> Несоответствующая заготовка </summary>
        public bool NonConformingWorkpiece
        {
            get => _NonConformingWorkpiece;
            set => Set(ref _NonConformingWorkpiece, value);
        }

        /// <summary> Отсутствует программа </summary>
        public bool NoProgram
        {
            get => _NoProgram;
            set => Set(ref _NoProgram, value);
        }

        /// <summary> Отсутствует документация </summary>
        public bool NoDocumentation
        {
            get => _NoDocumentation;
            set => Set(ref _NoDocumentation, value);
        }

        /// <summary> Недостаток навыков </summary>
        public bool LackOfSkills
        {
            get => _LackOfSkills;
            set => Set(ref _LackOfSkills, value);
        }

        /// <summary> Недостаточно инструментов </summary>
        public bool InsufficientTools
        {
            get => _InsufficientTools;
            set => Set(ref _InsufficientTools, value);
        }

        /// <summary> Недостаточно оборудования </summary>
        public bool InsufficientEquipment
        {
            get => _InsufficientEquipment;
            set => Set(ref _InsufficientEquipment, value);
        }

        /// <summary> Нужна техническая помощь </summary>
        public bool NeedTechnicalHelp
        {
            get => _NeedTechnicalHelp;
            set => Set(ref _NeedTechnicalHelp, value);
        }

        /// <summary> Нужна помощь старшего </summary>
        public bool NeedSeniorHelp
        {
            get => _NeedSeniorHelp;
            set => Set(ref _NeedSeniorHelp, value);
        }

        /// <summary> Количество выпущено</summary>
        public double FinishedCount
        {
            get => _FinishedCount;
            set
            {
                Set(ref _FinishedCount, value);
                OnPropertyChanged(nameof(TotalCountInfo));
                OnPropertyChanged(nameof(IsStarted));
                OnPropertyChanged(nameof(InProduction));
                OnPropertyChanged(nameof(EndDetailInfo));
                OnPropertyChanged(nameof(NeedMasterAttention));
                OnPropertyChanged(nameof(MasterIsHelping));
            }
        }

        /// <summary> Информация о наличии детали в списке заданий </summary>
        public PartTaskInfo TaskInfo
        {
            get => _TaskInfo;
            set
            {
                if (Set(ref _TaskInfo, value))
                {
                    OnPropertyChanged(nameof(IsTaskStatusWritten));
                    OnPropertyChanged(nameof(NeedToSyncTask));
                }
            }
        }

        /// <summary> Записан ли статус в список </summary>
        public bool IsTaskStatusWritten
        {
            get => _IsTaskStatusWritten;
            set
            {
                if (Set(ref _IsTaskStatusWritten, value))
                {
                    OnPropertyChanged(nameof(TaskInfo));
                    OnPropertyChanged(nameof(NeedToSyncTask));
                }
            }
        }

        /// <summary> Описание количества деталей </summary>
        [JsonIgnore]
        public string TotalCountInfo => $"{FinishedCount}{(DefectiveCount > 0 ? $" (+{DefectiveCount})" : "")} / {TotalCount} шт";

        /// <summary> Плановое время наладки. Может быть присвоено только при инициализации (предполагается получение из БД). </summary>
        public double SetupTimePlan
        {
            get => _SetupTimePlan;
            set => Set(ref _SetupTimePlan, value);
        }

        /// <summary> Плановое штучное время. Может быть присвоено только при инициализации (предполагается получение из БД). </summary>
        public double SingleProductionTimePlan
        {
            get => _SingleProductionTimePlan;
            set => Set(ref _SingleProductionTimePlan, value);
        }

        /// <summary> Фактическое машинное время, фиксируемое оператором. В расчетах выработки не участвует </summary>
        public TimeSpan MachineTime
        {
            get => _MachineTime;
            set
            {
                Set(ref _MachineTime, value);
            }
        }

        /// <summary> Список простоев </summary>
        public DeepObservableCollection<DownTime> DownTimes
        {
            get => _DownTimes;
            set
            {
                if (Set(ref _DownTimes, value))
                {
                    OnPropertyChanged(nameof(LastDownTime));
                    OnPropertyChanged(nameof(LastDownTimeName));
                    OnPropertyChanged(nameof(DownTimesIsClosed));
                    OnPropertyChanged(nameof(EndSetupInfo));
                    OnPropertyChanged(nameof(EndDetailInfo));
                    OnPropertyChanged(nameof(NeedMasterAttention));
                    OnPropertyChanged(nameof(MasterIsHelping));
                }
            }
        }

        private bool _NeedMasterAttention;
        /// <summary> Описание </summary>
        public bool NeedMasterAttention
        {
            get => _NeedMasterAttention;
            set => Set(ref _NeedMasterAttention, value);
        }

        private bool _MasterIsHelping;
        /// <summary> Мастер помогает </summary>
        public bool MasterIsHelping
        {
            get => _MasterIsHelping;
            set => Set(ref _MasterIsHelping, value);
        }

        /// <summary> Id присваивается после записи в таблицу. Нужен для поиска в таблице при редактировании.</summary>
        public int Id
        {
            get => _Id;
            set
            {
                Set(ref _Id, value);
                OnPropertyChanged(nameof(Title));
            }
        }

        /// <summary> Время начала наладки </summary>
        public DateTime StartSetupTime
        {
            get => _StartSetupTime;
            set
            {
                Set(ref _StartSetupTime, value);
                OnPropertyChanged(nameof(EndSetupInfo));
                OnPropertyChanged(nameof(SetupIsNotFinished));
                OnPropertyChanged(nameof(SetupCanBeClosed));
                OnPropertyChanged(nameof(IsStarted));
                OnPropertyChanged(nameof(CanBeFinished));
                OnPropertyChanged(nameof(InProduction));
                OnPropertyChanged(nameof(EndDetailInfo));
                OnPropertyChanged(nameof(Title));
            }
        }

        /// <summary> Время завершения наладки / время начала изготовления </summary>
        public DateTime StartMachiningTime
        {
            get => _StartMachiningTime;
            set
            {
                Set(ref _StartMachiningTime, value);
                OnPropertyChanged(nameof(EndSetupInfo));
                OnPropertyChanged(nameof(SetupIsNotFinished));
                OnPropertyChanged(nameof(SetupCanBeClosed));
                OnPropertyChanged(nameof(IsFinished));
                OnPropertyChanged(nameof(IsStarted));
                OnPropertyChanged(nameof(CanBeFinished));
                OnPropertyChanged(nameof(InProduction));
                OnPropertyChanged(nameof(EndDetailInfo));
                OnPropertyChanged(nameof(Title));
            }
        }

        /// <summary> Время завершения изготовления </summary>
        public DateTime EndMachiningTime
        {
            get => _EndMachiningTime;
            set
            {
                Set(ref _EndMachiningTime, value);
                OnPropertyChanged(nameof(SetupIsNotFinished));
                OnPropertyChanged(nameof(SetupCanBeClosed));
                OnPropertyChanged(nameof(CanBeFinished));
                OnPropertyChanged(nameof(IsFinished));
                OnPropertyChanged(nameof(IsStarted));
                OnPropertyChanged(nameof(InProduction));
                OnPropertyChanged(nameof(TotalCountInfo));
                OnPropertyChanged(nameof(EndSetupInfo));
                OnPropertyChanged(nameof(EndDetailInfo));
                OnPropertyChanged(nameof(Title));
            }
        }


        private ObservableCollection<MasterReaction> _MasterReactions;
        /// <summary> Реакции мастера </summary>
        public ObservableCollection<MasterReaction> MasterReactions
        {
            get => _MasterReactions;
            set => Set(ref _MasterReactions, value);
        }

        /// <summary>
        /// Является ли деталь серийной
        /// </summary>
        public bool IsSerial
        {
            get => _IsSerial;
            set
            {
                Set(ref _IsSerial, value);
                OnPropertyChanged(nameof(Title));
            }
        }



        /// <summary> Информация об окончании наладки </summary>
        [JsonIgnore]
        public string EndSetupInfo
        {
            get
            {
                var downTimes = DownTimes.Where(d => d.Relation == DownTime.Relations.Setup);
                double downTimesMinutes = 0;
                foreach (var downTime in downTimes)
                {
                    var partialBreaks = DateTimes.GetPartialBreakBetween(downTime.StartTime, downTime.EndTime);

                    if (downTime.Time.TotalMinutes > 0) downTimesMinutes += downTime.Time.TotalMinutes - partialBreaks;
                }
                var endSetupTime = SetupIsFinished ? StartMachiningTime : StartSetupTime
                    .AddMinutes(SetupTimePlan)
                    .AddMinutes(downTimesMinutes)
                    .Add(DateTimes.GetBreaksBetween(StartSetupTime, StartSetupTime.AddMinutes(SetupTimePlan), false));

                if (StartSetupTime == StartMachiningTime) return "Без наладки";
                var result = endSetupTime > StartSetupTime ? endSetupTime.ToString(Constants.DateTimeFormat) : "-";
                if (result == "-") return result;

                var breaks = DateTimes.GetBreaksBetween(StartSetupTime, endSetupTime);
                var breaksInfo = breaks.Ticks > 0 ? $" + перерывы: {breaks.TotalMinutes} мин" : string.Empty;

                var downTimesInfo = downTimesMinutes > 0
                    ? $" + простои: {downTimesMinutes} мин"
                    : string.Empty;
                var planInfo = SetupTimePlan > 0 && !DownTimes.Any(dt => dt.Type == DownTime.Types.PartialSetup)
                    ? $" (Плановое: норматив {SetupTimePlan} мин{breaksInfo}{downTimesInfo})"
                    : string.Empty;
                var productivity = SetupTimePlan > 0 && !DownTimes.Any(dt => dt.Type == DownTime.Types.PartialSetup)
                    ? $" ({SetupTimePlan / SetupTimeFact.TotalMinutes * 100:N0}%)"
                    : string.Empty;

                switch (IsFinished)
                {
                    case State.PartialSetup:
                        result += " (Неполная наладка)";
                        break;
                    case State.InProgress when FullSetupTimeFact.Ticks > 0 && !DownTimes.Any(dt => dt.Type == DownTime.Types.PartialSetup):
                        result += productivity + downTimesInfo;
                        break;
                    case State.InProgress when FullSetupTimeFact.Ticks > 0 && DownTimes.Any(dt => dt.Type == DownTime.Types.PartialSetup):
                        result += " (Неполная наладка)";
                        break;
                    case State.InProgress when FullSetupTimeFact.Ticks < 0 && !DownTimes.Any(dt => dt.Type == DownTime.Types.PartialSetup):
                        result += planInfo;
                        break;
                    case State.InProgress when FullSetupTimeFact.Ticks < 0 && DownTimes.Any(dt => dt.Type == DownTime.Types.PartialSetup):
                        result += " (Неполная наладка)";
                        break;
                    case State.Finished when DownTimes.Any(dt => dt.Type == DownTime.Types.PartialSetup):
                        result += " (Неполная наладка)";
                        break;
                    case State.Finished:
                        result += productivity + downTimesInfo;
                        break;
                }
                return $"{result}{(breaks.Ticks > 0 && SetupTimeFact.Ticks > 0 && IsFinished is not State.PartialSetup && !DownTimes.Any(dt => dt.Type == DownTime.Types.PartialSetup) ? breaksInfo : string.Empty)}";
            }
        }

        /// <summary> Информация о завершении изготовления </summary>
        [JsonIgnore]
        public string EndDetailInfo
        {
            get
            {
                if (EndMachiningTime == DateTime.MinValue && SingleProductionTimePlan == 0) return "-";
                var downTimes = DownTimes.Where(d => d.Relation == DownTime.Relations.Machining);
                double downTimesMinutes = 0;
                foreach (var downTime in downTimes)
                {
                    var partialBreaks = DateTimes.GetPartialBreakBetween(downTime.StartTime, downTime.EndTime);

                    if (downTime.Time.TotalMinutes > 0) downTimesMinutes += downTime.Time.TotalMinutes - partialBreaks;
                }
                var totalCount = TotalCount > 1 ? TotalCount - 1 : TotalCount;
                var startMachiningTime = SetupIsFinished
                    ? StartMachiningTime
                    : StartSetupTime
                        .AddMinutes(SetupTimePlan)
                        .Add(DateTimes.GetBreaksBetween(StartSetupTime, StartSetupTime.AddMinutes(SetupTimePlan), true));

                var endMachiningTime = EndMachiningTime >= StartMachiningTime
                    ? EndMachiningTime
                    : startMachiningTime
                        .AddMinutes(totalCount * SingleProductionTimePlan)
                        .Add(DateTimes.GetBreaksBetween(startMachiningTime, startMachiningTime.AddMinutes(totalCount * SingleProductionTimePlan), false));

                var result = endMachiningTime >= startMachiningTime && EndSetupInfo != "-" ? endMachiningTime.ToString(Constants.DateTimeFormat) : "-";
                if (result == "-") return result;
                var breaks = DateTimes.GetBreaksBetween(startMachiningTime, endMachiningTime);
                var breaksInfo = breaks.Ticks > 0 ? $" + перерывы: {breaks.TotalMinutes} мин" : string.Empty;
                var downTimesInfo = downTimesMinutes > 0
                    ? $" + простои: {downTimesMinutes} мин"
                    : string.Empty;
                var planInfo = SingleProductionTimePlan > 0
                    ? $" (Плановое: {totalCount} шт по {SingleProductionTimePlan} мин{breaksInfo})"
                    : string.Empty;
                var finishedCount = StartMachiningTime > StartSetupTime ? FinishedCount - 1 : FinishedCount;
                var productivity = SingleProductionTimePlan > 0
                    ? finishedCount * SingleProductionTimePlan / ProductionTimeFact.TotalMinutes * 100
                    : 0;
                var productivityInfo = productivity > 0 ? $" ({productivity:N0}%)" : string.Empty;
                switch (IsFinished)
                {
                    case not State.InProgress when FinishedCount > 0:
                        result += productivityInfo + downTimesInfo;
                        break;
                    case State.InProgress:
                        result += planInfo;
                        break;
                    case State.PartialSetup when FinishedCount == 0:
                        result = "Без изготовления";
                        break;
                    case State.Finished:
                        result += productivity + downTimesInfo;
                        break;
                }
                return $"{result}{(breaks.Ticks > 0 && ProductionTimeFact.Ticks > 0 && IsFinished is not State.InProgress ? breaksInfo : string.Empty)}";
            }
        }

        /// <summary> Фактическое время наладки </summary>
        [JsonIgnore] public TimeSpan FullSetupTimeFact => StartMachiningTime - StartSetupTime;

        /// <summary> Ненужное свойство потому-что не разобрался с разметкой и хотел спать </summary>
        [JsonIgnore] public bool NeedToSyncTask => TaskInfo == PartTaskInfo.InList && !IsTaskStatusWritten;

        /// <summary> Фактическое время наладки с учетом перерывов </summary>
        [JsonIgnore]
        public TimeSpan SetupTimeFact
        {
            get
            {
                double downTimesMinutes = 0;
                foreach (var downTime in DownTimes.Where(x => x.Relation is DownTime.Relations.Setup))
                {
                    downTimesMinutes += downTime.Time.TotalMinutes;
                }
                var breaks = DateTimes.GetBreaksBetween(StartSetupTime, StartMachiningTime);
                var downTimes = TimeSpan.FromMinutes(downTimesMinutes);
                return FullSetupTimeFact
                    - DateTimes.GetBreaksBetween(StartSetupTime, StartMachiningTime)
                    - TimeSpan.FromMinutes(downTimesMinutes);
            }
        }

        /// <summary> Полное название детали (наименование + обозначение) </summary>
        [JsonIgnore] public string FullName => $"{Name} {Number}".Trim();

        [JsonIgnore]
        public string Title
        {
            get
            {
                if (IsFinished == State.InProgress) return FullName.TrimLen(65);
                var suffix = IsSerial ? " [Cерийная продукция]" : "";
                var partName = FullName.TrimLen(75);
                return $"{partName}".Trim();
            }
        }

        [JsonIgnore]
        public bool InProgress => IsFinished == State.InProgress;

        public string OperatorComments
        {
            get => _OperatorComments;
            set => Set(ref _OperatorComments, value);
        }

        /// <summary>Фактическое время изготовления </summary>
        [JsonIgnore] public TimeSpan FullProductionTimeFact => EndMachiningTime - StartMachiningTime;

        /// <summary> Время изготовления с учетом перерывов </summary>
        [JsonIgnore]
        public TimeSpan ProductionTimeFact
        {
            get
            {
                double downTimesMinutes = 0;
                foreach (var downTime in DownTimes.Where(x => x.Relation is DownTime.Relations.Machining))
                {
                    //var partialBreaks = DateTimes.GetPartialBreakBetween(downTime.StartTime, downTime.EndTime);
                    //downTimesMinutes += downTime.Time.TotalMinutes - partialBreaks;
                    downTimesMinutes += downTime.Time.TotalMinutes;
                }
                return FullProductionTimeFact - DateTimes.GetBreaksBetween(StartMachiningTime, EndMachiningTime)
                                              - TimeSpan.FromMinutes(downTimesMinutes);
            }
        }

        /// <summary>
        /// Может ли быть завершена деталь.
        /// True если время начала изготовления больше или равно времени начала наладки и если время начала изготовления раньше текущего.
        /// </summary>
        [JsonIgnore] public bool CanBeFinished => StartSetupTime <= StartMachiningTime && DateTime.Now > StartMachiningTime && DownTimesIsClosed;

        /// <summary>
        /// Статус изготовления детали.
        /// Finished если время завершения изготовления больше, чем время начала наладки, также если завершена наладка (SetupIsFinished == true) и количество выпущенных деталей больше 0.
        /// PartialSetup если время завершения изготовления больше равно времени завершения наладки и количество выпущенных деталей 0.
        /// </summary>
        [JsonIgnore]
        public State IsFinished
        {
            get
            {
                if (EndMachiningTime >= StartMachiningTime && SetupIsFinished && FinishedCount > 0 &&
                    MachineTime > TimeSpan.Zero && !DownTimes.Any(dt => dt.Type == DownTime.Types.PartialSetup)) return State.Finished;
                if (EndMachiningTime == DateTime.MinValue && FinishedCount == 0) return State.InProgress;
                if (EndMachiningTime == StartMachiningTime && SetupIsFinished && FinishedCount is >= 0 and < 1) return State.PartialSetup;
                if (DownTimes.Any(dt => dt.Type == DownTime.Types.PartialSetup)) return State.PartialSetup;
                return State.InProgress;
            }
        }

        /// <summary>
        /// Завершена ли наладка.
        /// True если нет незавершенных простоев.
        /// </summary>
        [JsonIgnore] public bool DownTimesIsClosed => LastDownTime is null || !LastDownTime.InProgress;

        /// <summary>
        /// Последний простой
        /// </summary>
        [JsonIgnore] public DownTime? LastDownTime => DownTimes.LastOrDefault();

        /// <summary>
        /// Имя последнего простоя в кавычках, потому что XAML почему-то не жрет &quot;
        /// </summary>
        [JsonIgnore] public string LastDownTimeName => LastDownTime is null ? string.Empty : $"\"{LastDownTime.Name}\"";

        /// <summary>
        /// Завершена ли наладка.
        /// True если время завершения наладки больше или равно времени начала наладки.
        /// </summary>
        [JsonIgnore] public bool SetupIsFinished => StartMachiningTime >= StartSetupTime;

        /// <summary>
        /// Идет ли изготовление.
        /// True если время завершена наладка, но не завершено изготовление.
        /// </summary>
        [JsonIgnore] public bool InProduction => SetupIsFinished && IsFinished == State.InProgress;

        /// <summary>
        /// Не завершена ли наладка. Нужно для привязок разметки.
        /// Инвертированное значение свойства SetupIsFinished.
        /// </summary>
        [JsonIgnore] public bool SetupIsNotFinished => !SetupIsFinished;

        /// <summary>
        /// Может ли быть завершена наладка. Нужно для привязок разметки.
        /// </summary>
        [JsonIgnore] public bool SetupCanBeClosed => SetupIsNotFinished && DownTimesIsClosed;

        /// <summary>
        /// Не завершено ли изготовление. Нужно для привязок разметки.
        /// Инвертированное значение свойства IsFinished.
        /// </summary>
        [JsonIgnore] public bool IsStarted => IsFinished == State.InProgress;

        /// <summary>
        /// Записана ли актуальная информация о детали в таблицу
        /// </summary>
        public bool IsSynced
        {
            get => _IsSynced;
            set
            {
                Set(ref _IsSynced, value);
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(EndSetupInfo));
                OnPropertyChanged(nameof(TaskInfo));
                OnPropertyChanged(nameof(IsTaskStatusWritten));
            }
        }

        /// <summary>
        /// Информация о детали.
        /// Статусные свойства выводятся из временных свойств. По-умолчанию завершающие временные свойства присваиваются в DateTime.MinValue. После присваивания времен деталь считается завершенной.
        /// </summary>
        /// <param name="name">Наименование</param>
        /// <param name="number">Обозначение</param>
        /// <param name="setup">Текущая установка</param>
        /// <param name="order">Заказ</param>
        /// <param name="totalCount">Количество</param>
        /// <param name="setupTimePlan">Плановое время наладки</param>
        /// <param name="singleProductionTimePlan">Плановое штучное время</param>
        public Part(string name, string number, byte setup, string order, int totalCount, double setupTimePlan, double singleProductionTimePlan)
        {
            Guid = Guid.NewGuid();
            _Name = name;
            _Number = number;
            _Setup = setup;
            _Order = order;
            FinishedCount = -1;
            TotalCount = totalCount;
            SetupTimePlan = setupTimePlan;
            SingleProductionTimePlan = singleProductionTimePlan;
            _Operator = AppSettings.Instance.CurrentOperator ?? throw new NullReferenceException("Была запущена деталь без оператора");
            Id = -1;
            _DownTimes = new DeepObservableCollection<DownTime>();
            _MasterReactions = new ObservableCollection<MasterReaction>();
            _OperatorComments = string.Empty;
            _Shift = string.Empty;
            DownTimes.CollectionChanged += DownTimes_CollectionChanged!;
        }

        public Part()
        {
            Guid = Guid.NewGuid();
            _Name = string.Empty;
            _Number = string.Empty;
            _Order = string.Empty;
            StartSetupTime = DateTime.Now.Rounded();
            _DownTimes = new DeepObservableCollection<DownTime>();
            _MasterReactions = new ObservableCollection<MasterReaction>();
            Id = -1;
            _OperatorComments = string.Empty;
            _Operator = AppSettings.Instance.CurrentOperator!;
            _Shift = string.Empty;
            DownTimes.CollectionChanged += DownTimes_CollectionChanged!;
        }

        /// <summary>
        /// Конструктор копирования
        /// </summary>
        /// <param name="part">Источник</param>
        public Part(Part part)
        {
            Guid = part.Guid;
            _Name = part.Name;
            _Number = part.Number;
            _Setup = part.Setup;
            _Order = part.Order;
            _TotalCount = part.TotalCount;
            _FinishedCount = part.FinishedCount;
            _DefectiveCount = part.DefectiveCount;
            _StartSetupTime = part.StartSetupTime;
            _StartMachiningTime = part.StartMachiningTime;
            _EndMachiningTime = part.EndMachiningTime;
            _MachineTime = part.MachineTime;
            _LongSetupNotifySended = part.LongSetupNotifySended;
            _DownTimes = new DeepObservableCollection<DownTime>();
            _MasterReactions = new ObservableCollection<MasterReaction>();
            _SetupTimePlan = part.SetupTimePlan;
            _SingleProductionTimePlan = part.SingleProductionTimePlan;
            foreach (var downTime in part.DownTimes)
            {
                _DownTimes.Add(new DownTime(this, downTime));
            }
            foreach (var masterReaction in part.MasterReactions ?? new ObservableCollection<MasterReaction>())
            {
                _MasterReactions.Add(new MasterReaction(masterReaction));
            }
            _Id = part.Id;
            _OperatorComments = part.OperatorComments;
            _Operator = part.Operator;
            _Shift = part.Shift;
            _IsSynced = part.IsSynced;
            DownTimes.CollectionChanged += DownTimes_CollectionChanged!;
        }

        private void DownTimes_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(DownTimes));
            OnPropertyChanged(nameof(LastDownTime));
            OnPropertyChanged(nameof(LastDownTimeName));
            OnPropertyChanged(nameof(DownTimesIsClosed));
            OnPropertyChanged(nameof(EndSetupInfo));
            Debug.WriteLine($"Сработал {MethodBase.GetCurrentMethod()?.Name}");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string PropertyName = null!)
        {
            Debug.WriteLine($"{MethodBase.GetCurrentMethod()?.Name} для {PropertyName}");
            var handlers = PropertyChanged;
            if (handlers is null) return;

            var invokationList = handlers.GetInvocationList();
            var args = new PropertyChangedEventArgs(PropertyName);

            foreach (var action in invokationList)
            {
                if (action.Target is DispatcherObject dispatcherObject)
                    dispatcherObject.Dispatcher.Invoke(action, this, args);
                else
                {
                    action.DynamicInvoke(this, args);
                }
            }

            if (PropertyName != nameof(DownTimes)) return;
            Debug.WriteLine("Дополнительные вызовы OnPropertyChanged при срабатывании на Part.DownTimes");
            OnPropertyChanged(nameof(LastDownTime));
            OnPropertyChanged(nameof(LastDownTimeName));
            OnPropertyChanged(nameof(DownTimesIsClosed));
            OnPropertyChanged(nameof(EndSetupInfo));
            //AppSettings.Save();
        }

        protected virtual bool Set<T>(ref T field, T value, [CallerMemberName] string PropertyName = null!)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(PropertyName);
            return true;
        }

        public void NotifyTaskStatus()
        {
            OnPropertyChanged(nameof(TaskInfo));
            OnPropertyChanged(nameof(IsTaskStatusWritten));
            OnPropertyChanged(nameof(NeedToSyncTask));
        }
    }
}