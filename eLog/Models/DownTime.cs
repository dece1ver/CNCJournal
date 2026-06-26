using eLog.Infrastructure;
using eLog.Infrastructure.Extensions;
using libeLog;
using libeLog.Extensions;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace eLog.Models
{
    public class DownTime : INotifyPropertyChanged, IDataErrorInfo
    {
        [JsonConstructor]
        public DownTime(Part part, Types type, DateTime startTime, DateTime endTime, string toolType = "", string comment = "", bool? isSuccess = null)
        {
            _ParentPart = part;
            _Type = type;
            _StartTime = startTime;
            _StartTimeText = _StartTime.ToString(Constants.DateTimeFormat);
            _EndTime = endTime;
            _EndTimeText = _EndTime.ToString(Constants.DateTimeFormat);
            _ToolType = toolType;
            _Comment = comment;
        }

        public DownTime(Part part, Types type, string toolType = "", string comment = "")
        {
            _ParentPart = part;
            _Type = type;
            var now = DateTime.Now;
            _StartTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            _StartTimeText = StartTime.ToString(Constants.DateTimeFormat);
            _EndTime = DateTime.MinValue;
            _EndTimeText = string.Empty;
            _ToolType = toolType;
            _Comment = comment;
        }

        /// <summary>
        /// Конструктор копирования
        /// </summary>
        /// <param name="part">Деталь к которой относится простой</param>
        /// <param name="downTime">Источник</param>

        public DownTime(Part part, DownTime downTime)
        {
            _ParentPart = part;
            _Type = downTime.Type;
            _StartTime = downTime.StartTime;
            _StartTimeText = downTime.StartTimeText;
            _EndTime = downTime.EndTime;
            _EndTimeText = downTime.EndTimeText;
            _ToolType = downTime.ToolType;
            _Comment = downTime.Comment;
            _IsSuccess = downTime.IsSuccess;
        }

        private Types _Type;
        private DateTime _StartTime;
        private DateTime _EndTime;
        private string _StartTimeText;
        private string _EndTimeText;
        private Part _ParentPart;

        public Part ParentPart
        {
            get => _ParentPart;
            set
            {
                if (Set(ref _ParentPart, value))
                    OnPropertyChanged(nameof(Relation));
            }
        }

        public enum Types
        {
            CreateNcProgram,
            Maintenance,
            ToolSearching,
            ToolChanging,
            Mentoring,
            ContactingDepartments,
            FixtureMaking,
            HardwareFailure,
            PartialSetup // не для отметки вручную
        }

        public enum Relations
        {
            Setup, Machining, None
        }

        public Types Type
        {
            get => _Type;
            set
            {
                if (Set(ref _Type, value))
                    OnPropertyChanged(nameof(Name));
            }
        }

        public Relations Relation
        {
            get
            {
                if (StartTime == DateTime.MinValue && StartTime < ParentPart.StartSetupTime) return Relations.None;
                return ParentPart.SetupIsFinished && _StartTime >= ParentPart.StartMachiningTime
                    ? Relations.Machining
                    : Relations.Setup;
            }
        }


        private bool _NeedToSend = true;
        /// <summary> Нужно ли отправлять уведомление </summary>
        public bool NeedToSend
        {
            get => _NeedToSend;
            set => Set(ref _NeedToSend, value);
        }


        public string Name => Type switch
        {
            Types.CreateNcProgram => Text.DownTimes.CreateNcProgram,
            Types.Maintenance => Text.DownTimes.Maintenance,
            Types.ToolSearching => Text.DownTimes.ToolSearching,
            Types.ToolChanging => Text.DownTimes.ToolChanging,
            Types.Mentoring => Text.DownTimes.Mentoring,
            Types.ContactingDepartments => Text.DownTimes.ContactingDepartments,
            Types.FixtureMaking => Text.DownTimes.FixtureMaking,
            Types.HardwareFailure => Text.DownTimes.HardwareFailure,
            Types.PartialSetup => Text.DownTimes.PartialSetup, // не для отметки вручную
            _ => throw new ArgumentOutOfRangeException()
        };

        public DateTime StartTime
        {
            get => _StartTime;
            private set
            {
                if (!Set(ref _StartTime, value)) return;
                if (_StartTime == DateTime.MinValue || _StartTime < ParentPart.StartSetupTime) return;
                OnPropertyChanged(nameof(EndTimeText));
                OnPropertyChanged(nameof(Relation));
                OnPropertyChanged(nameof(Time));
                OnPropertyChanged(nameof(InProgress));
                UpdateError();
                OnPropertyChanged(nameof(HasError));
            }
        }

        [JsonIgnore]
        public string StartTimeText
        {
            get => _StartTimeText;
            set
            {
                _StartTimeText = value;
                StartTime = DateTime.TryParseExact(_StartTimeText, Constants.DateTimeFormat, null, DateTimeStyles.None, out var startTime)
                    ? startTime
                    : DateTime.MinValue;
                UpdateError();
                OnPropertyChanged(nameof(HasError));
            }
        }

        public DateTime EndTime
        {
            get => _EndTime;
            private set
            {
                if (!Set(ref _EndTime, value)) return;
                OnPropertyChanged(nameof(StartTimeText));
                OnPropertyChanged(nameof(Time));
                OnPropertyChanged(nameof(InProgress));
                UpdateError();
                OnPropertyChanged(nameof(HasError));
            }
        }

        [JsonIgnore]
        public string EndTimeText
        {
            get => _EndTimeText;
            set
            {
                _EndTimeText = value;
                EndTime = DateTime.TryParseExact(_EndTimeText, Constants.DateTimeFormat, null, DateTimeStyles.None, out var endTime)
                    ? endTime
                    : DateTime.MinValue;
                UpdateError();
                OnPropertyChanged(nameof(HasError));
            }
        }


        private string _ToolType;
        /// <summary> Тип инструмента (только для поиска инструмента) </summary>
        public string ToolType
        {
            get => _ToolType;
            set => Set(ref _ToolType, value);
        }


        private string _Comment;
        /// <summary> Комментарий к простою </summary>
        public string Comment
        {
            get => _Comment;
            set => Set(ref _Comment, value);
        }

        private bool? _IsSuccess;
        /// <summary> Нашел ли инструмент </summary>
        public bool? IsSuccess
        {
            get => _IsSuccess;
            set => Set(ref _IsSuccess, value);
        }


        /// <summary>
        /// Время простоя с учетом перерывов
        /// </summary>
        [JsonIgnore] public TimeSpan Time => EndTime - StartTime - DateTimes.GetBreaksBetween(StartTime, EndTime);
        [JsonIgnore] public bool InProgress => EndTime < StartTime;

        public bool HasError { get; private set; }

        public string this[string columnName]
        {
            get
            {
                string error = null!;
                switch (columnName)
                {
                    case nameof(StartTimeText):
                        {
                            if (StartTime == DateTime.MinValue)
                                error = "Некорректно указано время начала";
                            else if (StartTime < ParentPart.StartSetupTime)
                            {
                                error = "Время начала простоя не может быть раньше времени запуска детали.";
                            }
                            else if (EndTime != DateTime.MinValue && EndTime <= StartTime)
                            {
                                error = "Время начала простоя раньше времени завершения.";
                            }
                            break;
                        }
                    case nameof(EndTimeText):
                        if (EndTime != DateTime.MinValue && EndTime <= StartTime)
                            error = "Время завершения простоя не может быть раньше времени начала.";
                        else if (Relation is Relations.Setup && ParentPart.SetupIsFinished &&
                                 EndTime > ParentPart.StartMachiningTime)
                        {
                            error =
                                "Время завершения простоя не может быть позже времени завершения наладки, т.к. он открыт в наладке";
                        }
                        else if (Relation is Relations.Machining && ParentPart.IsFinished == Part.State.Finished &&
                                 EndTime > ParentPart.EndMachiningTime)
                        {
                            error = "Время завершения простоя не может быть позже времени завершения изготовления детали.";
                        }
                        else if (Relation is Relations.Machining && ParentPart.IsFinished == Part.State.InProgress &&
                                 EndTime > DateTime.Now)
                        {
                            error = "Время завершения простоя не может быть позже текущего времени, т.к. изготовление детали не завершено.";
                        }
                        else if (Time <= TimeSpan.Zero && EndTime != DateTime.MinValue)
                        {
                            error = "Данный простой не имеет длительности, введены либо одинаквые значения, либо простой полностью перекрывается перерывом.";
                        }
                        break;
                }
                Debug.WriteLine(error);
                HasError = error != null;
                return error!;
            }
        }
        [JsonIgnore] public string Error => null!;

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
                    dispatcherObject.Dispatcher.Invoke(action, this, args);
                else
                {
                    action.DynamicInvoke(this, args);
                }
            }
            //AppSettings.Save();
        }

        protected virtual bool Set<T>(ref T field, T value, [CallerMemberName] string PropertyName = null!)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(PropertyName);
            return true;
        }

        public void UpdateError()
        {
            HasError = false;
            if (StartTime == DateTime.MinValue)
                HasError = true;
            else if (StartTime < ParentPart.StartSetupTime)
            {
                HasError = true;
            }
            else if (EndTime != DateTime.MinValue && EndTime <= StartTime)
            {
                HasError = true;
            }
            else if (EndTime != DateTime.MinValue && EndTime <= StartTime)
            {
                HasError = true;
            }
            else if (Relation is Relations.Setup && ParentPart.SetupIsFinished &&
                     EndTime > ParentPart.StartMachiningTime)
            {
                HasError = true;
            }
            else if (Relation is Relations.Machining && ParentPart.IsFinished == Part.State.Finished &&
                     EndTime > ParentPart.EndMachiningTime)
            {
                HasError = true;
            }
            else if (Relation is Relations.Machining && ParentPart.IsFinished == Part.State.InProgress &&
                     EndTime > DateTime.Now)
            {
                HasError = true;
            }
            else if (Time <= TimeSpan.Zero && EndTime != DateTime.MinValue)
            {
                HasError = true;
            }
        }
    }
}