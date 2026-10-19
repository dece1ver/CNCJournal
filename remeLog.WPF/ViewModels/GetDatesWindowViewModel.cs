using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using libeLog;
using System.Windows.Input;
using libeLog.Base;
using remeLog.Views;

namespace remeLog.ViewModels
{
    public class GetDatesWindowViewModel : ViewModel
    {
        private ObservableCollection<DateTime> _Dates;
        private DateTime? _SelectedDate;
        private DateTime _NewDate;
        private string _Status;
        private bool _IsInputValid;


        public GetDatesWindowViewModel(DateTime dateTime)
        {
            _Dates = new ObservableCollection<DateTime>();
            _NewDate = dateTime;
            _Status = "";

            AddDateCommand = new LambdaCommand(OnAddDateCommandExecuted, CanAddDateCommandExecute);
            RemoveDateCommand = new LambdaCommand(OnRemoveDateCommandExecuted, CanRemoveDateCommandExecute);
            ConfirmCommand = new LambdaCommand(OnConfirmCommandExecuted, CanConfirmCommandExecute);

            AddDate(DateTime.Today);

            UpdateValidState();
        }


        public ObservableCollection<DateTime> Dates
        {
            get => _Dates;
            set => Set(ref _Dates, value);
        }

        public DateTime? SelectedDate
        {
            get => _SelectedDate;
            set
            {
                if (Set(ref _SelectedDate, value))
                {
                    UpdateValidState();
                    CommandManager.InvalidateRequerySuggested();
                }               
            }
        }

        public DateTime NewDate
        {
            get => _NewDate;
            set => Set(ref  _NewDate, value);
        }
        /// <summary> Статус </summary>
        public string Status
        {
            get => _Status;
            set => Set(ref _Status, value);
        }

        public bool IsInputValid
        {
            get => _IsInputValid;
            set => Set(ref _IsInputValid, value);
        }

        #region AddDate
        public ICommand AddDateCommand { get; }
        private void OnAddDateCommandExecuted(object p)
        {
            AddDate(NewDate);
        }
        private bool CanAddDateCommandExecute(object p) => true;
        #endregion

        #region RemoveDate
        public ICommand RemoveDateCommand { get; }
        private void OnRemoveDateCommandExecuted(object p)
        {
            RemoveSelectedDate();
        }
        private bool CanRemoveDateCommandExecute(object p) => SelectedDate.HasValue;
        #endregion


        #region Confirm
        public ICommand ConfirmCommand { get; }
        private void OnConfirmCommandExecuted(object p)
        {
            if (p is GetDatesWindow w)
            {
                w.DialogResult = true;
                w.Dates = Dates;
                w.Close();
            }
        }
        private bool CanConfirmCommandExecute(object p) => IsInputValid;
        #endregion


        public void AddDate(DateTime date)
        {
            if (Dates.Select(d => d.Date).Contains(date.Date))
            {
                Status = "Дата уже существует в списке";
                return;
            }
            foreach (var existingDate in Dates)
            {
                if (existingDate.Date == date.Date)
                {
                    Status = "Дата уже существует в списке";
                    return;
                }
            }

            Dates.Add(date);
            Status = $"Добавлена дата: {date.ToShortDateString()}";
            UpdateValidState();
        }

        private void RemoveSelectedDate()
        {
            if (SelectedDate.HasValue)
            {
                var date = SelectedDate.Value;
                Dates.Remove(date);
                Status = $"Удалена дата: {date.ToShortDateString()}";
                UpdateValidState();
            }
        }

        private void UpdateValidState()
        {
            IsInputValid = Dates.Count > 0;
        }
    }
}
