using libeLog;
using libeLog.Base;
using libeLog.Extensions;
using remeLog.Infrastructure;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using libeLog.Views;

namespace remeLog.ViewModels
{
    class LongSetupsViewModel : ViewModel
    {
        DateTime startDate;
        DateTime endDate;
        CancellationTokenSource _cts = new();
        public LongSetupsViewModel(ObservableCollection<Part> parts)
        {
            _Parts = parts;
            startDate = Parts.Select(p => p.ShiftDate).Min();
            endDate = Parts.Select(p => p.ShiftDate).Max();

            SavePartsCommand = new LambdaCommand(OnSavePartsCommandExecuted, CanSavePartsCommandExecute);
            UpdatePartsCommand = new LambdaCommand(OnUpdatePartsCommandExecuted, CanUpdatePartsCommandExecute);
        }

        private ObservableCollection<Part> _Parts;
        /// <summary> Изготовления </summary>
        public ObservableCollection<Part> Parts
        {
            get => _Parts;
            set
            {
                Set(ref _Parts, value);
            }
        }

        private Part? _SelectedPart;
        /// <summary> Выбранная деталь </summary>
        public Part? SelectedPart
        {
            get => _SelectedPart;
            set => Set(ref _SelectedPart, value);
        }

        private bool _InProgress;
        /// <summary> Загрузка информации </summary>
        public bool InProgress
        {
            get => _InProgress;
            set => Set(ref _InProgress, value);
        }

        private string _Status = string.Empty;
        /// <summary> Статус </summary>
        public string Status
        {
            get => _Status;
            set => Set(ref _Status, value);
        }

        #region SaveParts
        public ICommand SavePartsCommand { get; }
        private async void OnSavePartsCommandExecuted(object p)
        {
            await UpdatePartsAsync();
        }
        private bool CanSavePartsCommandExecute(object p) => !InProgress;
        #endregion

        #region UpdateParts
        public ICommand UpdatePartsCommand { get; }
        private async void OnUpdatePartsCommandExecuted(object p)
        {
            await LoadPartsAsync();
        }
        private bool CanUpdatePartsCommandExecute(object p) => !InProgress;
        #endregion

        async Task UpdatePartsAsync()
        {
            try
            {
                InProgress = true;
                foreach (var part in Parts.Where(p => p.NeedUpdate))
                {
                    await part.UpdatePartAsync();
                }
                await LoadPartsAsync();
            }
            catch (Exception ex)
            {
                MessageBoxWindow.Show(ex.Message);
            }
            finally
            {
                InProgress = false;
            }
        }

        async Task LoadPartsAsync()
        {
            InProgress = true;
            _cts.Cancel();
            _cts = new();
            var ct = _cts.Token;
            Parts = await Database.ReadPartsByGuids(Parts.Select(p => p.Guid), ct);
            InProgress = false;
        }
    }
}
