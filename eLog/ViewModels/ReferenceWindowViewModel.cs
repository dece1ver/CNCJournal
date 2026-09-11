using libeLog.Base;
using libeLog.Infrastructure.Threading;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using static libeLog.Infrastructure.Threading.ThreadTolerances;

namespace eLog.ViewModels
{
    /// <summary> Справочники: допуски метрической резьбы и среднего диаметра d2 </summary>
    internal class ReferenceWindowViewModel : ViewModel
    {
        public ReferenceWindowViewModel()
        {
            MetricPitches = new[] { 0.2, 0.25, 0.3, 0.35, 0.4, 0.45, 0.5, 0.6, 0.7, 0.75, 0.8, 1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0, 5.5, 6.0 };
            MetricDiameters = StandardDiameters;
            Grades = new[] { 3, 4, 5, 6, 7, 8, 9, 10 };

            _MetricDiameter = MetricDiameters.Contains(10.0) ? 10.0 : MetricDiameters[0];
            _MetricPitch = 1.5;
            _MetricPosition = "g";
            _MetricGrade = 6;

            _MeanDiameter = StandardDiameters.Contains(24.0) ? 24.0 : StandardDiameters[0];
            RefreshMeanPitches(selectPreferred: true);
            RefreshMeanFields(selectPreferred: true);
            RefreshMeanResult();
            RefreshMeanSourceRows();
            RefreshMetricResults();
        }

        #region Навигация

        public string[] Sections => new[] { "Метрическая резьба", "Средний диаметр d2" };

        private int _SelectedSection;
        public int SelectedSection
        {
            get => _SelectedSection;
            set
            {
                if (Set(ref _SelectedSection, value))
                {
                    OnPropertyChanged(nameof(IsMetricSection));
                    OnPropertyChanged(nameof(IsMeanSection));
                }
            }
        }

        public bool IsMetricSection => SelectedSection == 0;
        public bool IsMeanSection => SelectedSection == 1;

        #endregion

        #region Раздел 1. Метрическая резьба (готовая + инструмент)

        public double[] MetricDiameters { get; }
        public double[] MetricPitches { get; }
        public int[] Grades { get; }

        private bool _MetricIsInternal;
        /// <summary> false — наружная резьба, true — внутренняя </summary>
        public bool MetricIsInternal
        {
            get => _MetricIsInternal;
            set
            {
                if (Set(ref _MetricIsInternal, value))
                {
                    OnPropertyChanged(nameof(MetricPositions));
                    OnPropertyChanged(nameof(SelectedThreadKind));
                    MetricPosition = value ? "H" : "g";
                    RefreshMetricResults();
                }
            }
        }

        public string[] ThreadKinds { get; } = new[] { "Наружная", "Внутренняя" };

        public string SelectedThreadKind
        {
            get => MetricIsInternal ? "Внутренняя" : "Наружная";
            set
            {
                if (value is not null)
                    MetricIsInternal = value == "Внутренняя";
            }
        }

        public string[] MetricPositions => MetricIsInternal
            ? new[] { "E", "F", "G", "H" }
            : new[] { "d", "e", "f", "g", "h" };

        private string _MetricPosition = "g";
        public string MetricPosition
        {
            get => _MetricPosition;
            set { if (Set(ref _MetricPosition, value)) RefreshMetricResults(); }
        }

        private int _MetricGrade;
        public int MetricGrade
        {
            get => _MetricGrade;
            set { if (Set(ref _MetricGrade, value)) RefreshMetricResults(); }
        }

        private double _MetricDiameter;
        public double MetricDiameter
        {
            get => _MetricDiameter;
            set { if (Set(ref _MetricDiameter, value)) RefreshMetricResults(); }
        }

        private double _MetricPitch;
        public double MetricPitch
        {
            get => _MetricPitch;
            set { if (Set(ref _MetricPitch, value)) RefreshMetricResults(); }
        }

        private string _MetricFinishedValue = "";
        /// <summary> Готовая резьба по ГОСТ 16093: пределы </summary>
        public string MetricFinishedValue
        {
            get => _MetricFinishedValue;
            private set => Set(ref _MetricFinishedValue, value);
        }

        private string _MetricFinishedDetails = "";
        public string MetricFinishedDetails
        {
            get => _MetricFinishedDetails;
            private set => Set(ref _MetricFinishedDetails, value);
        }

        private string _MetricToolValue = "";
        /// <summary> Инструмент/заготовка по ГОСТ 19257 (отверстие) / 19258 (стержень): пределы </summary>
        public string MetricToolValue
        {
            get => _MetricToolValue;
            private set => Set(ref _MetricToolValue, value);
        }

        private string _MetricToolDetails = "";
        public string MetricToolDetails
        {
            get => _MetricToolDetails;
            private set => Set(ref _MetricToolDetails, value);
        }

        private string _MetricToolNote = "";
        public string MetricToolNote
        {
            get => _MetricToolNote;
            private set => Set(ref _MetricToolNote, value);
        }

        private static string F3(double v) => v.ToString("F3");

        private void RefreshMetricResults()
        {
            try
            {
                if (!Enum.TryParse<ThreadPosition>(MetricPosition, out var pos))
                {
                    MetricFinishedValue = "—";
                    MetricFinishedDetails = "";
                    MetricToolValue = "—";
                    MetricToolDetails = "";
                    MetricToolNote = "";
                    return;
                }

                var fin = GetGost16093(MetricDiameter, MetricPitch, pos, MetricGrade);
                if (fin is { } f)
                {
                    MetricFinishedValue = $"{F3(f.Min)} … {F3(f.Max)}";
                    MetricFinishedDetails = MetricIsInternal
                        ? $"EI={FmtDev(f.EsEi)}   TD1={F3(f.Tolerance)}"
                        : $"es={FmtDev(f.EsEi)}   Td={F3(f.Tolerance)}";
                }
                else
                {
                    MetricFinishedValue = "—";
                    MetricFinishedDetails = "Нет данных для степени точности.";
                }

                if (MetricIsInternal)
                {
                    var nominal = GetHoleNominalForField(MetricDiameter, MetricPitch, pos, MetricGrade);
                    if (nominal is { } n)
                    {
                        var r = GetGost19257(MetricDiameter, MetricPitch, pos, MetricGrade);
                        if (double.IsNaN(r.D1max))
                        {
                            MetricToolValue = $"{F3(n.Nominal)}  (+{F3(n.Dev)})";
                            MetricToolDetails = "Верхний предел не нормирован.";
                        }
                        else
                        {
                            MetricToolValue = $"{F3(r.D1min)} … {F3(r.D1max)}";
                            MetricToolDetails = $"Сверло {F3(n.Nominal)}  +{F3(n.Dev)}";
                        }
                    }
                    else
                    {
                        MetricToolValue = "—";
                        MetricToolDetails = "Нет данных для поля/степени.";
                    }
                    MetricToolNote = "";
                }
                else
                {
                    var nominal = GetRodNominalForField(MetricDiameter, MetricPitch, pos, MetricGrade);
                    if (nominal is { } n)
                    {
                        var r = GetGost19258(MetricDiameter, MetricPitch, pos, MetricGrade);
                        if (double.IsNaN(r.dmin))
                        {
                            MetricToolValue = $"{F3(n.Nominal)}  ({F3(n.Dev)})";
                            MetricToolDetails = "Нижний предел не нормирован.";
                        }
                        else
                        {
                            MetricToolValue = $"{F3(r.dmin)} … {F3(r.dmax)}";
                            MetricToolDetails = $"Стержень {F3(n.Nominal)}  {F3(n.Dev)}";
                        }
                    }
                    else
                    {
                        MetricToolValue = "—";
                        MetricToolDetails = "Нет данных для поля/степени.";
                    }
                    MetricToolNote = IsRiseExtrapolated(MetricPitch)
                        ? "При шаге более 2 мм подъём витка ГОСТ 19258 не нормирован — значение экстраполировано."
                        : "";
                }
            }
            catch
            {
                MetricFinishedValue = "—";
                MetricFinishedDetails = "";
                MetricToolValue = "—";
                MetricToolDetails = "";
                MetricToolNote = "";
            }
        }

        private static string FmtDev(double v) => v > 0 ? $"+{v:F3}" : v.ToString("F3");

        #endregion

        #region Раздел 2. Средний диаметр d2 наружной резьбы

        private double _MeanDiameter;
        public double MeanDiameter
        {
            get => _MeanDiameter;
            set
            {
                if (Set(ref _MeanDiameter, value))
                {
                    RefreshMeanPitches(selectPreferred: true);
                    RefreshMeanFields(selectPreferred: true);
                    RefreshMeanResult();
                    RefreshMeanSourceRows();
                }
            }
        }

        private double[] _MeanPitches = Array.Empty<double>();
        public double[] MeanPitches
        {
            get => _MeanPitches;
            private set => Set(ref _MeanPitches, value);
        }

        private double _MeanPitch;
        public double MeanPitch
        {
            get => _MeanPitch;
            set
            {
                if (Set(ref _MeanPitch, value))
                {
                    RefreshMeanFields(selectPreferred: true);
                    RefreshMeanResult();
                    RefreshMeanSourceRows();
                }
            }
        }

        private string[] _MeanFields = Array.Empty<string>();
        public string[] MeanFields
        {
            get => _MeanFields;
            private set => Set(ref _MeanFields, value);
        }

        private string _MeanField = "6g";
        public string MeanField
        {
            get => _MeanField;
            set
            {
                if (Set(ref _MeanField, value))
                {
                    RefreshMeanResult();
                    RefreshMeanSourceRows();
                }
            }
        }

        private string _MeanResult = "";
        public string MeanResult
        {
            get => _MeanResult;
            private set => Set(ref _MeanResult, value);
        }

        private string _MeanDetails = "";
        public string MeanDetails
        {
            get => _MeanDetails;
            private set => Set(ref _MeanDetails, value);
        }

        /// <summary> Строка исходной таблицы es/ei для спойлера </summary>
        public sealed record MeanSourceRow(double Pitch, string Field, int Es, int Ei, bool IsCurrent);

        private ObservableCollection<MeanSourceRow> _MeanSourceRows = new();
        /// <summary> Исходные es/ei для диапазона текущего диаметра </summary>
        public ObservableCollection<MeanSourceRow> MeanSourceRows
        {
            get => _MeanSourceRows;
            private set => Set(ref _MeanSourceRows, value);
        }

        private void RefreshMeanSourceRows()
        {
            int r = DiameterRangeIndex(MeanDiameter);
            int pk = (int)Math.Round(MeanPitch * 100);
            MeanSourceRows = new ObservableCollection<MeanSourceRow>(
                PitchesForDiameter(MeanDiameter)
                    .SelectMany(p => FieldsForDiameterAndPitch(MeanDiameter, p)
                        .Select(f => (p, f)))
                    .Select(t => GetMeanD2(MeanDiameter, t.p, t.f) is { } v
                        ? new MeanSourceRow(t.p, t.f, v.Es, v.Ei,
                            (int)Math.Round(t.p * 100) == pk && t.f == MeanField)
                        : null)
                    .Where(x => x is not null)
                    .Cast<MeanSourceRow>()
                    .OrderBy(x => x.Pitch)
                    .ThenBy(x => Array.IndexOf(ThreadTolerances.MeanFields, x.Field)));
        }

        private void RefreshMeanPitches(bool selectPreferred)
        {
            var pitches = PitchesForDiameter(MeanDiameter);
            MeanPitches = pitches;
            if (pitches.Length == 0) return;
            if (selectPreferred)
            {
                var std = StandardPitch(MeanDiameter);
                MeanPitch = pitches.Contains(std) && std > 0 ? std : pitches[0];
            }
            else if (!pitches.Contains(MeanPitch)) MeanPitch = pitches[0];
        }

        private void RefreshMeanFields(bool selectPreferred)
        {
            var fields = FieldsForDiameterAndPitch(MeanDiameter, MeanPitch);
            MeanFields = fields;
            if (fields.Length == 0) return;
            if (selectPreferred)
                MeanField = fields.Contains("6g") ? "6g" : fields.Contains(MeanField) ? MeanField : fields[0];
            else if (!fields.Contains(MeanField)) MeanField = fields[0];
        }

        private void RefreshMeanResult()
        {
            var r = GetMeanD2(MeanDiameter, MeanPitch, MeanField);
            if (r is { } v)
            {
                MeanResult = $"{F3(v.Min)} … {F3(v.Max)}";
                MeanDetails = $"d2 ном. {F3(v.Nominal)}   es={v.Es} мкм   ei={v.Ei} мкм";
            }
            else
            {
                MeanResult = "—";
                MeanDetails = "Нет данных для выбранного сочетания.";
            }
        }

        #endregion
    }
}
