using libeLog;
using libeLog.Base;
using remeLog.Views;
using System;
using System.Windows.Input;

namespace remeLog.ViewModels
{
    public class PeriodsComparisonWindowViewModel : ViewModel
    {
        public PeriodsComparisonWindowViewModel(DateTime defaultFromDate, DateTime defaultToDate)
        {
            _FromDate1 = defaultFromDate.AddYears(-1);
            _ToDate1 = defaultToDate.AddYears(-1);
            _FromDate2 = defaultFromDate;
            _ToDate2 = defaultToDate;
            _Label1 = DefaultLabel(_FromDate1, _ToDate1);
            _Label2 = DefaultLabel(_FromDate2, _ToDate2);
            _Status = "";

            ConfirmCommand = new LambdaCommand(OnConfirmCommandExecuted, CanConfirmCommandExecute);
        }

        private static string DefaultLabel(DateTime from, DateTime to) =>
            from.Date == to.Date ? from.ToString("dd.MM.yyyy") : $"{from:dd.MM.yyyy} - {to:dd.MM.yyyy}";

        private DateTime _FromDate1;
        public DateTime FromDate1
        {
            get => _FromDate1;
            set { if (Set(ref _FromDate1, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        private DateTime _ToDate1;
        public DateTime ToDate1
        {
            get => _ToDate1;
            set { if (Set(ref _ToDate1, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        private string _Label1;
        public string Label1
        {
            get => _Label1;
            set => Set(ref _Label1, value);
        }

        private DateTime _FromDate2;
        public DateTime FromDate2
        {
            get => _FromDate2;
            set { if (Set(ref _FromDate2, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        private DateTime _ToDate2;
        public DateTime ToDate2
        {
            get => _ToDate2;
            set { if (Set(ref _ToDate2, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        private string _Label2;
        public string Label2
        {
            get => _Label2;
            set => Set(ref _Label2, value);
        }

        private string _Status;
        public string Status
        {
            get => _Status;
            set => Set(ref _Status, value);
        }

        #region Confirm
        public ICommand ConfirmCommand { get; }
        private void OnConfirmCommandExecuted(object p)
        {
            if (p is not PeriodsComparisonWindow w) return;

            w.FromDate1 = FromDate1;
            w.ToDate1 = ToDate1;
            w.Label1 = string.IsNullOrWhiteSpace(Label1) ? DefaultLabel(FromDate1, ToDate1) : Label1.Trim();
            w.FromDate2 = FromDate2;
            w.ToDate2 = ToDate2;
            w.Label2 = string.IsNullOrWhiteSpace(Label2) ? DefaultLabel(FromDate2, ToDate2) : Label2.Trim();
            w.DialogResult = true;
            w.Close();
        }
        private bool CanConfirmCommandExecute(object p) => FromDate1 <= ToDate1 && FromDate2 <= ToDate2;
        #endregion
    }
}
