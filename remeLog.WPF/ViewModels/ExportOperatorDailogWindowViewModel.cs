using libeLog;
using libeLog.Base;
using remeLog.Views;
using System.Windows.Input;

namespace remeLog.ViewModels
{
    internal class ExportOperatorDailogWindowViewModel : ViewModel
    {
        public ExportOperatorDailogWindowViewModel()
        {
            ExportCommand = new LambdaCommand(OnExportCommandExecuted, CanExportCommandExecute);
        }

        private bool _IncludeSmallBatch;
        /// <summary>
        /// Включить штучные партии (по регламенту: м/в &lt; 3 мин и ≤ 10 шт,
        /// либо м/в ≥ 3 мин и ≤ 5 шт). По умолчанию выключено — изготовление
        /// считается без штучных.
        /// </summary>
        public bool IncludeSmallBatch
        {
            get => _IncludeSmallBatch;
            set => Set(ref _IncludeSmallBatch, value);
        }

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

        private static void OnExportCommandExecuted(object p)
        {
            if (p is ExportOperatorReportDialogWindow w) w.DialogResult = true;
        }
        private static bool CanExportCommandExecute(object p) => true;
        #endregion
    }
}
