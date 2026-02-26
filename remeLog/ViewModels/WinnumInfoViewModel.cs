using System;
using System.Collections.Generic;
using System.Linq;
using libeLog.Base;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore;
using remeLog.Infrastructure.Winnum.Data;
using System.Collections.ObjectModel;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using remeLog.Infrastructure;
using libeLog;
using System.Windows.Input;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Data;

namespace remeLog.ViewModels
{
    public class WinnumInfoViewModel : ViewModel
    {
        public DataTable? TimelineData { get; set; }

        public WinnumInfoViewModel(string generalInfo, string ncProgramFolder, List<PriorityTagDuration> priorityTagDurations, List<TimeInterval> timeIntervals, DataTable? timeline = null)
        {
            OpenArchiveNcProgramFolderCommand = new LambdaCommand(OnOpenArchiveNcProgramFolderCommandExecuted, CanOpenArchiveNcProgramFolderCommandExecute);
            OpenIntermediateNcProgramFolderCommand = new LambdaCommand(OnOpenIntermediateNcProgramFolderCommandExecuted, CanOpenIntermediateNcProgramFolderCommandExecute);
            OpenWinnumNcProgramFolderCommand = new LambdaCommand(OnOpenWinnumNcProgramFolderCommandExecuted, CanOpenWinnumNcProgramFolderCommandExecute);

            GeneralInfo = generalInfo;
            NcArchiveProgramFolder = AppSettings.NcArchivePath;
            NcIntermediateProgramFolder = AppSettings.NcIntermediatePath;
            NcWinnumProgramFolder = ncProgramFolder;
            PriorityTagDurations = priorityTagDurations;
            TimeIntervals = timeIntervals;

            TimelineData = timeline;

            var series = new ObservableCollection<ISeries>();

            var minDate = timeIntervals.Min(x => x.Start);
            var maxDate = timeIntervals.Max(x => x.End);
            (minDate, maxDate) = Util.GetRoundedHourBounds(minDate, maxDate);

            foreach (var interval in timeIntervals)
            {
                series.Add(new StepLineSeries<DateTimePoint>
                {
                    Values = new[]
                    {
                        new DateTimePoint(interval.Start, 1),
                        new DateTimePoint(interval.End, 1)
                    },
                    Fill = new SolidColorPaint(SKColors.Green.WithAlpha(90)),
                    Stroke = null,
                    GeometryFill = null,
                    GeometryStroke = null,
                    XToolTipLabelFormatter = _ => "",
                    YToolTipLabelFormatter = point =>
                    {
                        if (point.Model?.Value == 0) return "";
                        var x = point.Model?.DateTime ?? DateTime.MinValue;
                        return point.Index switch
                        {
                            0 => $"Начало: {x:HH:mm:ss}",
                            1 => $"Завершение: {x:HH:mm:ss}",
                            _ => $"{x:HH:mm:ss}"
                        };
                    },
                    
                });
            }

            Intervals = series;

            XAxes = new Axis[]
            {
                new DateTimeAxis(TimeSpan.FromHours(1), date => date.ToString("HH:mm:ss"))
                {
                    Name = "Изготовление по ЭЖ",
                    MinStep = 0.1,
                    LabelsRotation = 45,
                    TextSize = 12,
                    NameTextSize = 14,
                    SeparatorsPaint = new SolidColorPaint(SKColors.Gray.WithAlpha(50)),
                    ShowSeparatorLines = true
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "Программа выполняется",
                    NameTextSize = 14,
                    Labels = new List<string>(),
                    MaxLimit = 10
                }
            };
        }

        #region OpenArchiveNcProgramFolder
        public ICommand OpenArchiveNcProgramFolderCommand { get; }
        private void OnOpenArchiveNcProgramFolderCommandExecuted(object p)
        {
            TryOpenDirectory(NcArchiveProgramFolder);
        }
        private bool CanOpenArchiveNcProgramFolderCommandExecute(object p) => !string.IsNullOrWhiteSpace(NcArchiveProgramFolder);
        #endregion

        #region OpenIntermediateNcProgramFolder
        public ICommand OpenIntermediateNcProgramFolderCommand { get; }
        private void OnOpenIntermediateNcProgramFolderCommandExecuted(object p)
        {
            TryOpenDirectory(NcIntermediateProgramFolder);
        }
        private bool CanOpenIntermediateNcProgramFolderCommandExecute(object p) => !string.IsNullOrWhiteSpace(NcIntermediateProgramFolder);
        #endregion

        #region OpenWinnumNcProgramFolder
        public ICommand OpenWinnumNcProgramFolderCommand { get; }
        private void OnOpenWinnumNcProgramFolderCommandExecuted(object p)
        {
            TryOpenDirectory(NcWinnumProgramFolder, true);
        }
        private bool CanOpenWinnumNcProgramFolderCommandExecute(object p) => !string.IsNullOrWhiteSpace(NcWinnumProgramFolder);
        #endregion

        public ObservableCollection<ISeries> Intervals { get; set; }

        public string GeneralInfo { get; set; }
        public string NcArchiveProgramFolder { get; set; }
        public string NcIntermediateProgramFolder { get; set; }
        public string NcWinnumProgramFolder { get; set; }

        public List<PriorityTagDuration> PriorityTagDurations { get; set; }
        public List<TimeInterval> TimeIntervals { get; set; }
        public Axis[] XAxes { get; set; }
        public Axis[] YAxes { get; set; }

        void TryOpenDirectory(string path, bool tryParent = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                else if (tryParent && Directory.GetParent(path) is DirectoryInfo parent)
                {
                    Process.Start(new ProcessStartInfo(parent.FullName) { UseShellExecute = true });
                }
                else 
                {
                    MessageBox.Show("Не открывается :с", "Ошибочка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибочка вышла", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
