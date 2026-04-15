using libeLog;
using libeLog.Base;
using remeLog.Views;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace remeLog.ViewModels
{
    internal class ExportOperatorDailogWindowViewModel : ViewModel, IDataErrorInfo
    {
        public ExportOperatorDailogWindowViewModel()
        {
            ExportCommand = new LambdaCommand(OnExportCommandExecuted, CanExportCommandExecute);
            Types = new string[] { "От", "До" };
            Type = Types[0];
            _CountText = "5";
        }

        string _countError = null!;

        public string[] Types { get; }

        public string Type { get; set; }



        private string _CountText;
        /// <summary> Описание </summary>
        public string CountText
        {
            get => _CountText;
            set => Set(ref _CountText, value);
        }

        public int Count { get; set; }

        private bool _OnlySerialParts;
        /// <summary> Только серийная продукция </summary>
        public bool OnlySerialParts
        {
            get => _OnlySerialParts;
            set => Set(ref _OnlySerialParts, value);
        }

        private bool _IncludeExcludedlParts;
        /// <summary> Включить исключённые записи </summary>
        public bool IncludeExcludedlParts
        {
            get => _IncludeExcludedlParts;
            set => Set(ref _IncludeExcludedlParts, value);
        }


        #region ExportCommand
        public ICommand ExportCommand { get; }

        public string Error => null!;

        public string this[string columnName]
        {
            get
            {
                switch (columnName)
                {
                    case nameof(CountText):
                        if (int.TryParse(CountText, out var count))
                        {
                            _countError = null!;
                            Count = count;
                        } else
                        {
                            Count = 0;
                            _countError = "Невалидные данные";
                        }
                        break;
                    default:
                        break;
                }
                return _countError;
            }
        }

        private static void OnExportCommandExecuted(object p)
        {
            if (p is ExportOperatorReportDialogWindow w) w.DialogResult = true;
        }
        private  bool CanExportCommandExecute(object p) => string.IsNullOrEmpty(_countError);
        #endregion
    }
}
