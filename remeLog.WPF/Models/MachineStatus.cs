using libeLog.Base;
using libeLog.Infrastructure;
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace remeLog.Models
{
    public class MachineStatus : ViewModel
    {

        private string _Mode = "N/A";
        /// <summary> Режим </summary>
        public string Mode
        {
            get => _Mode;
            set
            {
                Set(ref _Mode, value);
                OnPropertyChanged(nameof(IndicatorColor));
            }
        }

        private string _Status = "N/A";
        /// <summary> Статус </summary>
        public string Status
        {
            get => _Status;
            set
            {
                Set(ref _Status, value);
                OnPropertyChanged(nameof(IndicatorColor));
            }
        }

        private bool _IsOperating;
        /// <summary> В работе </summary>
        public bool IsOperating
        {
            get => _IsOperating;
            set
            {
                Set(ref _IsOperating, value);
                OnPropertyChanged(nameof(IndicatorColor));
            }
        }

        private int _Speed;
        /// <summary> Обороты шпинделя </summary>
        public int Speed
        {
            get => _Speed;
            set
            {
                Set(ref _Speed, value);
            }
        }


        private double _MaxRpm;
        /// <summary> Описание </summary>
        public double MaxRpm
        {
            get => _MaxRpm;
            set => Set(ref _MaxRpm, value);
        }


        private double _Feed;
        /// <summary> Минутная подача </summary>
        public double Feed
        {
            get => _Feed;
            set
            {
                Set(ref _Feed, value);
            }
        }


        private string _FirstAxisName = "N/A";
        /// <summary> Имя первой оси </summary>
        public string FirstAxisName
        {
            get => _FirstAxisName;
            set => Set(ref _FirstAxisName, value);
        }


        private string _SecondAxisName = "N/A";
        /// <summary> Имя второй оси </summary>
        public string SecondAxisName
        {
            get => _SecondAxisName;
            set => Set(ref _SecondAxisName, value);
        }


        private string _ThirdAxisName = "N/A";
        /// <summary> Имя третьей оси </summary>
        public string ThirdAxisName
        {
            get => _ThirdAxisName;
            set => Set(ref _ThirdAxisName, value);
        }


        private string _FourthAxisName = "N/A";
        /// <summary> Имя четвертой оси </summary>
        public string FourthAxisName
        {
            get => _FourthAxisName;
            set => Set(ref _FourthAxisName, value);
        }


        private string _FivethAxisName = "N/A";
        /// <summary> Имя пятой оси </summary>
        public string FivethAxisName
        {
            get => _FivethAxisName;
            set => Set(ref _FivethAxisName, value);
        }


        private double _FirstRelativeAxisValue;
        /// <summary> Координата первой оси </summary>
        public double FirstRelativeAxisValue
        {
            get => _FirstRelativeAxisValue;
            set
            {
                if (Set(ref _FirstRelativeAxisValue, value))
                {
                    OnPropertyChanged(nameof(CutSpeed));
                }
            }
        }


        private double _SecondRelativeAxisValue;
        /// <summary> Координата второй оси </summary>
        public double SecondRelativeAxisValue
        {
            get => _SecondRelativeAxisValue;
            set => Set(ref _SecondRelativeAxisValue, value);
        }


        private double _ThirdRelativeAxisValue;
        /// <summary> Координата третьей оси </summary>
        public double ThirdRelativeAxisValue
        {
            get => _ThirdRelativeAxisValue;
            set => Set(ref _ThirdRelativeAxisValue, value);
        }


        private double _FourthRelativeAxisValue;
        /// <summary> Координата четвертой оси </summary>
        public double FourthRelativeAxisValue
        {
            get => _FourthRelativeAxisValue;
            set => Set(ref _FourthRelativeAxisValue, value);
        }


        private double _FivethRelativeAxisValue;
        /// <summary> Координата пятой оси </summary>
        public double FivethRelativeAxisValue
        {
            get => _FivethRelativeAxisValue;
            set => Set(ref _FivethRelativeAxisValue, value);
        }

        private double _FirstAbsoluteAxisValue;
        /// <summary> Координата первой оси </summary>
        public double FirstAbsoluteAxisValue
        {
            get => _FirstAbsoluteAxisValue;
            set
            {
                if (Set(ref _FirstAbsoluteAxisValue, value))
                {
                    OnPropertyChanged(nameof(CutSpeed));
                }
            }
        }

        private double _SecondAbsoluteAxisValue;
        /// <summary> Координата второй оси </summary>
        public double SecondAbsoluteAxisValue
        {
            get => _SecondAbsoluteAxisValue;
            set => Set(ref _SecondAbsoluteAxisValue, value);
        }


        private double _ThirdAbsoluteAxisValue;
        /// <summary> Координата третьей оси </summary>
        public double ThirdAbsoluteAxisValue
        {
            get => _ThirdAbsoluteAxisValue;
            set => Set(ref _ThirdAbsoluteAxisValue, value);
        }


        private double _FourthAbsoluteAxisValue;
        /// <summary> Координата четвертой оси </summary>
        public double FourthAbsoluteAxisValue
        {
            get => _FourthAbsoluteAxisValue;
            set => Set(ref _FourthAbsoluteAxisValue, value);
        }


        private double _FivethAbsoluteAxisValue;
        /// <summary> Координата пятой оси </summary>
        public double FivethAbsoluteAxisValue
        {
            get => _FivethAbsoluteAxisValue;
            set => Set(ref _FivethAbsoluteAxisValue, value);
        }

        public double FeedPerRevolution => Feed / Speed;

        public double CutSpeed => Math.PI * FirstAbsoluteAxisValue * Speed / 1000;

        private double _FirstMachineAxisValue;
        /// <summary> Координата первой оси </summary>
        public double FirstMachineAxisValue
        {
            get => _FirstMachineAxisValue;
            set
            {
                if (Set(ref _FirstMachineAxisValue, value))
                {
                    OnPropertyChanged(nameof(CutSpeed));
                }
            }
        }


        private double _SecondMachineAxisValue;
        /// <summary> Координата второй оси </summary>
        public double SecondMachineAxisValue
        {
            get => _SecondMachineAxisValue;
            set => Set(ref _SecondMachineAxisValue, value);
        }


        private double _ThirdMachineAxisValue;
        /// <summary> Координата третьей оси </summary>
        public double ThirdMachineAxisValue
        {
            get => _ThirdMachineAxisValue;
            set => Set(ref _ThirdMachineAxisValue, value);
        }


        private double _FourthMachineAxisValue;
        /// <summary> Координата четвертой оси </summary>
        public double FourthMachineAxisValue
        {
            get => _FourthMachineAxisValue;
            set => Set(ref _FourthMachineAxisValue, value);
        }


        private double _FivethMachineAxisValue;
        /// <summary> Координата пятой оси </summary>
        public double FivethMachineAxisValue
        {
            get => _FivethMachineAxisValue;
            set => Set(ref _FivethMachineAxisValue, value);
        }

        /// <summary>
        /// Цвет индикатора
        /// </summary>
        public Brush IndicatorColor
        {
            get
            {
                if (Alarms.Count > 0) return Brushes.Red;
                if (IsOperating)
                {
                    return Status == "STRT" && Mode == "Memory" ? Brushes.Green : Brushes.Yellow;
                }
                return Brushes.Gray;
            }
        }


        private double _FirstDistanceToGoAxisValue;
        /// <summary> Координата первой оси </summary>
        public double FirstDistanceToGoAxisValue
        {
            get => _FirstDistanceToGoAxisValue;
            set
            {
                if (Set(ref _FirstDistanceToGoAxisValue, value))
                {
                    OnPropertyChanged(nameof(CutSpeed));
                }
            }
        }


        private double _SecondDistanceToGoAxisValue;
        /// <summary> Координата второй оси </summary>
        public double SecondDistanceToGoAxisValue
        {
            get => _SecondDistanceToGoAxisValue;
            set => Set(ref _SecondDistanceToGoAxisValue, value);
        }


        private double _ThirdDistanceToGoAxisValue;
        /// <summary> Координата третьей оси </summary>
        public double ThirdDistanceToGoAxisValue
        {
            get => _ThirdDistanceToGoAxisValue;
            set => Set(ref _ThirdDistanceToGoAxisValue, value);
        }


        private double _FourthDistanceToGoAxisValue;
        /// <summary> Координата четвертой оси </summary>
        public double FourthDistanceToGoAxisValue
        {
            get => _FourthDistanceToGoAxisValue;
            set => Set(ref _FourthDistanceToGoAxisValue, value);
        }


        private double _FivethDistanceToGoAxisValue;
        /// <summary> Координата пятой оси </summary>
        public double FivethDistanceToGoAxisValue
        {
            get => _FivethDistanceToGoAxisValue;
            set => Set(ref _FivethDistanceToGoAxisValue, value);
        }


        private int? _UsedPrograms;
        /// <summary> Использовано программ </summary>
        public int? UsedPrograms
        {
            get => _UsedPrograms;
            set
            {
                if (Set(ref _UsedPrograms, value))
                {
                    OnPropertyChanged(nameof(TotalPrograms));
                }
            }
        }

        private int? _UnusedPrograms;
        /// <summary> Неиспользовано программ </summary>
        public int? UnusedPrograms
        {
            get => _UnusedPrograms;
            set => Set(ref _UnusedPrograms, value);
        }

        public int? TotalPrograms => UsedPrograms + UnusedPrograms;

        private int? _UsedMem;
        /// <summary> Использовано памяти </summary>
        public int? UsedMem
        {
            get => _UsedMem;
            set
            {
                if (Set(ref _UsedMem, value))
                {
                    OnPropertyChanged(nameof(TotalMem));
                }
            }
        }

        private int? _UnusedMem;
        /// <summary> Неиспользовано памяти </summary>
        public int? UnusedMem
        {
            get => _UnusedMem;
            set => Set(ref _UnusedMem, value);
        }

        public int? TotalMem => UsedMem + UnusedMem;


        private List<string> _Alarms = new();
        /// <summary> Ошибки </summary>
        public List<string> Alarms
        {
            get => _Alarms;
            set
            {
                if (Set(ref _Alarms, value))
                {
                    OnPropertyChanged(nameof(IndicatorColor));
                }
            }
        }


        private int _Spindle1Load;
        /// <summary> Нагрузка на 1 шпиндель </summary>
        public int Spindle1Load
        {
            get => _Spindle1Load;
            set => Set(ref _Spindle1Load, value);
        }


        private string _ProgramNumber;
        /// <summary> Описание </summary>
        public string ProgramNumber
        {
            get => _ProgramNumber;
            set => Set(ref _ProgramNumber, value);
        }


        private string _ProgramName;
        /// <summary> Описание </summary>
        public string ProgramName
        {
            get => _ProgramName;
            set => Set(ref _ProgramName, value);
        }
    }
}
