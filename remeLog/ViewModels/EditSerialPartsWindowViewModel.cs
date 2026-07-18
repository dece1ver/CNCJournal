using libeLog;
using libeLog.Base;
using libeLog.Models;
using remeLog.Infrastructure;
using remeLog.Infrastructure.Types;
using remeLog.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using libeLog.Views;

namespace remeLog.ViewModels
{
    public class EditSerialPartsWindowViewModel : ViewModel
    {
        public CancellationTokenSource _cts = new();
        public EditSerialPartsWindowViewModel()
        {
            SaveSerialPartsCommand = new LambdaCommand(OnSaveSerialPartsCommandExecuted, CanSaveSerialPartsCommandExecute);
            DeletePartCommand = new LambdaCommand(OnDeletePartCommandExecuted, CanDeletePartCommandExecute);
            LoadPartsCommand = new LambdaCommand(OnLoadPartsCommandExecuted, CanLoadPartsCommandExecute);
            AddOperationCommand = new LambdaCommand(OnAddOperationCommandExecuted, CanAddOperationCommandExecute);
            RenameOperationCommand = new LambdaCommand(OnRenameOperationCommandExecuted, CanRenameOperationCommandExecute);
            DeleteOperationCommand = new LambdaCommand(OnDeleteOperationCommandExecuted, CanDeleteOperationCommandExecute);
            AddSetupCommand = new LambdaCommand(OnAddSetupCommandExecuted, CanAddSetupCommandExecute);
            DeleteSetupCommand = new LambdaCommand(OnDeleteSetupCommandExecuted, CanDeleteSetupCommandExecute);
            SetNewSetupNormativeCommand = new LambdaCommand(OnSetNewSetupNormativeCommandExecuted, CanSetNewSetupNormativeCommandExecute);
            SetNewProductionNormativeCommand = new LambdaCommand(OnSetNewProductionNormativeCommandExecuted, CanSetNewProductionNormativeCommandExecute);
            SetNewProductionNormativeCommand = new LambdaCommand(OnSetNewProductionNormativeCommandExecuted, CanSetNewProductionNormativeCommandExecute);
            RemoveNormativeCommand = new LambdaCommand(OnRemoveNormativeCommandExecuted, CanRemoveNormativeCommandExecute);

            _Status = "";
            _SerialParts = new ObservableCollection<SerialPart>();
            SerialParts.CollectionChanged += OnSerialPartsCollectionChanged!;
            _ = Init();
        }

        async Task Init()
        {
            InProgress = true;
            _ = Task.Run(StartBackgroundCheckLoop);
            await LoadSerialPartsAsync();
            InProgress = false;
        }

        private ObservableCollection<SerialPart> _SerialParts;
        public ObservableCollection<SerialPart> SerialParts
        {
            get => _SerialParts;
            set
            {
                if (_SerialParts != null)
                    _SerialParts.CollectionChanged -= OnSerialPartsCollectionChanged!;

                if (Set(ref _SerialParts!, value))
                {
                    _SerialParts.CollectionChanged += OnSerialPartsCollectionChanged!;
                    SubscribeToSerialParts(_SerialParts);
                    OnPropertyChanged(nameof(IsSaveEnabled));
                }
            }
        }

        private CncOperation? _draggedOperation;
        public CncOperation? DraggedOperation
        {
            get => _draggedOperation;
            set => Set(ref _draggedOperation, value);
        }

        private string _Status;
        public string Status
        {
            get => _Status;
            set => Set(ref _Status, value);
        }

        private bool _IsSaveEnabled;
        public bool IsSaveEnabled
        {
            get => _IsSaveEnabled;
            set => Set(ref _IsSaveEnabled, value);
        }

        private bool _InProgress;
        public bool InProgress
        {
            get => _InProgress;
            set => Set(ref _InProgress, value);
        }

        private Overlay _Overlay = new(false);

        public Overlay Overlay
        {
            get => _Overlay;
            set => Set(ref _Overlay, value);
        }

        #region SaveSerialParts
        public ICommand SaveSerialPartsCommand { get; }
        private async void OnSaveSerialPartsCommandExecuted(object p)
        {
            if (string.IsNullOrEmpty(AppSettings.Instance.ConnectionString))
            {
                Status = "Детали не могут быть сохранены т.к. строка подключения не настроена";
                return;
            }
            InProgress = true;
            await Task.Run(async () =>
            {
                foreach (var part in SerialParts.Where(sp => sp.IsModified))
                {
                    await libeLog.Infrastructure.Database.SaveSerialPartAsync(part, AppSettings.Instance.ConnectionString, new Progress<string>(p => Status = p));
                }
            });
            
            await LoadSerialPartsAsync();
            InProgress = false;
            Status = "Обновление завершено";
            await Task.Delay(3000);
            Status = "";
        }

        private bool CanSaveSerialPartsCommandExecute(object p) => !InProgress && IsSaveEnabled;
        #endregion

        #region LoadParts
        public ICommand LoadPartsCommand { get; }
        private async void OnLoadPartsCommandExecuted(object p)
        {
            InProgress = true;
            await LoadSerialPartsAsync();
            InProgress = false;
        }
        private bool CanLoadPartsCommandExecute(object p) => !InProgress;
        #endregion

        #region DeletePart
        public ICommand DeletePartCommand { get; }
        private void OnDeletePartCommandExecuted(object p)
        {
            if (!Util.HasFeature(RemeLogFeature.AdvancedEdit)) { ShowMessage("Нет прав на выполнение операции"); return; }
            if (p is not SerialPart serialPart) 
                return; 
            if (MessageBoxWindow.Show(
                "Это действие необратимо.\nУдалить деталь из списка серийных вместе со всеми операциями и историей нормативов?", 
                serialPart.PartName, 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Warning,
                MessageBoxDefaultButton.No) == MessageBoxResult.Yes)
            {
                ShowMessage($"Удаление детали: {serialPart.PartName}");
            }
            else
            {
                ShowMessage("Отмена");
            }
        }
        private bool CanDeletePartCommandExecute(object p) => !InProgress;
        #endregion

        #region AddOperation
        public ICommand AddOperationCommand { get; }
        private void OnAddOperationCommandExecuted(object p)
        {
            if (p is not SerialPart serialPart)
                return;

            const string baseName = "Новая операция";
            int index = 1;
            string uniqueName;

            var existingNames = new HashSet<string>(serialPart.Operations.Select(op => op.Name));

            do
            {
                uniqueName = index == 1 ? baseName : $"{baseName} {index}";
                index++;
            } while (existingNames.Contains(uniqueName));

            serialPart.Operations.Add(new CncOperation(uniqueName)
            {
                Setups = new() { new CncSetup { Number = 1 } }
            });
            ShowMessage($"К детали: {serialPart.PartName} добавлена операция: {uniqueName}");
        }



        private bool CanAddOperationCommandExecute(object p) => !InProgress;
        #endregion

        #region AddSetup
        public ICommand AddSetupCommand { get; }
        private void OnAddSetupCommandExecuted(object p)
        {
            if (p is not CncOperation cncOperation)
                return;

            var setupNumbers = cncOperation.Setups
                .Select(s => s.Number)
                .OrderBy(n => n)
                .ToList();

            byte nextNumber = 1;

            for (byte i = 1; i <= byte.MaxValue; i++)
            {
                if (!setupNumbers.Contains(i))
                {
                    nextNumber = i;
                    break;
                }
            }

            if (nextNumber == 0)
            {
                ShowMessage("Превышение доступного числа установок.");
                return;
            }

            cncOperation.Setups.Add(new CncSetup
            {
                Number = nextNumber
            });
            ShowMessage($"Добавлена {nextNumber} установка");
        }
        private bool CanAddSetupCommandExecute(object p) => !InProgress;
        #endregion

        #region RenameOperation
        public ICommand RenameOperationCommand { get; }
        private void OnRenameOperationCommandExecuted(object p)
        {
            if (!Util.HasFeature(RemeLogFeature.AdvancedEdit)) { ShowMessage("Нет прав на выполнение операции"); return; }

            if (p is FrameworkElement fe && fe.DataContext is CncOperation cncOperation)
            {
                using (Overlay = new())
                {
                    var parentPart = SerialParts.FirstOrDefault(sp => sp.Operations.Contains(cncOperation));
                    var dlg = new UserInputDialogWindow(parentPart?.PartName ?? "Ввод", $"Введите новое название операции '{cncOperation.Name}':", cncOperation.Name, focusAndSelect: true, useOperationsContexMenu: true)
                    {
                        Owner = Window.GetWindow(fe)
                    };

                    if (dlg.ShowDialog() != true)
                    {
                        ShowMessage("Отмена");
                        return;
                    }

                    if (parentPart?.Operations.Any(x => x.Name == dlg.UserInput) == true)
                    {
                        ShowMessage("Операция с таким названием уже существует");
                        return;
                    }
                    cncOperation.Name = dlg.UserInput;
                }
            }
        }
        private bool CanRenameOperationCommandExecute(object p) => !InProgress;
        #endregion

        #region DeleteOperation
        public ICommand DeleteOperationCommand { get; }
        private void OnDeleteOperationCommandExecuted(object p)
        {
            if (!Util.HasFeature(RemeLogFeature.AdvancedEdit)) { ShowMessage("Нет прав на выполнение операции"); return; }

            if (p is FrameworkElement fe && fe.DataContext is CncOperation cncOperation)
            {
                var parentPart = SerialParts.FirstOrDefault(sp => sp.Operations.Contains(cncOperation));
                if (parentPart == null)
                    return;
                if (MessageBoxWindow.Show($"Удалить операцию '{cncOperation.Name}'?", parentPart.PartName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxDefaultButton.No) == MessageBoxResult.Yes)
                {
                    parentPart.Operations.Remove(cncOperation);
                    ShowMessage("Операция удалена");
                }
                else
                {
                    ShowMessage("Отмена");
                }
            }
        }
        private bool CanDeleteOperationCommandExecute(object p) => !InProgress;
        #endregion

        #region DeleteSetup
        public ICommand DeleteSetupCommand { get; }
        private void OnDeleteSetupCommandExecuted(object p)
        {
            if (!Util.HasFeature(RemeLogFeature.AdvancedEdit)) { ShowMessage("Нет прав на выполнение операции"); return; }

            if (p is FrameworkElement fe && fe.DataContext is CncSetup cncSetup)
            {
                var parentOperation = SerialParts.SelectMany(sp => sp.Operations).FirstOrDefault(sp => sp.Setups.Contains(cncSetup));
                if (parentOperation == null)
                    return;
                if (MessageBoxWindow.Show($"Удалить {cncSetup.Number} установ?", parentOperation.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxDefaultButton.No) == MessageBoxResult.Yes)
                {
                    parentOperation.Setups.Remove(cncSetup);
                    ShowMessage("Операция удалена");
                }
                else
                {
                    ShowMessage("Отмена");
                }
            }
        }
        private bool CanDeleteSetupCommandExecute(object p) => !InProgress;
        #endregion

        #region SetNewSetupNormative
        public ICommand SetNewSetupNormativeCommand { get; }
        private void OnSetNewSetupNormativeCommandExecuted(object p)
        {
            if (!Util.HasFeature(RemeLogFeature.AdvancedEdit)) { ShowMessage("Нет прав на выполнение операции"); return; }

            if (p is FrameworkElement fe && fe.DataContext is CncSetup cncSetup)
            {
                var parentOperation = SerialParts.SelectMany(sp => sp.Operations).FirstOrDefault(sp => sp.Setups.Contains(cncSetup));
                if (parentOperation == null)
                    return;
                var dlg = new UserInputDialogWindow($"{parentOperation}: {cncSetup.Number} установ", "Введите новый норматив на наладку:", expectedType: typeof(double), focusAndSelect: true, checkBox: new(false, "Подтвержденный"))
                {
                    Owner = Window.GetWindow(fe)
                };
                if (dlg.ShowDialog() == true && double.TryParse(dlg.UserInput, out double normative))
                {
                    if (normative <= 0)
                    {
                        ShowMessage("Значение должно быть больше 0");
                        return;
                    }
                    cncSetup.Normatives.Add(new NormativeEntry() { Type = NormativeEntry.NormativeType.Setup, Value = normative, EffectiveFrom = DateTime.Now, IsApproved = dlg.OptionalCheckBox is { IsChecked: true } });
                    ShowMessage("Установлен новый норматив на наладку");
                }
                else
                {
                    ShowMessage("Отмена");
                }
            }
        }
        private bool CanSetNewSetupNormativeCommandExecute(object p) => !InProgress;
        #endregion

        #region SetNewProductionNormative
        public ICommand SetNewProductionNormativeCommand { get; }

        private void OnSetNewProductionNormativeCommandExecuted(object p)
        {
            if (!Util.HasFeature(RemeLogFeature.AdvancedEdit)) { ShowMessage("Нет прав на выполнение операции"); return; }

            if (p is FrameworkElement fe && fe.DataContext is CncSetup cncSetup)
            {
                var parentOperation = SerialParts.SelectMany(sp => sp.Operations).FirstOrDefault(sp => sp.Setups.Contains(cncSetup));
                if (parentOperation == null)
                    return;
                var dlg = new UserInputDialogWindow($"{parentOperation}: {cncSetup.Number} установ", "Введите новый норматив на изготовление:", expectedType: typeof(double), focusAndSelect: true, checkBox: new(false, "Подтвержденный"))
                {
                    Owner = Window.GetWindow(fe)
                };
                if (dlg.ShowDialog() == true && double.TryParse(dlg.UserInput, out double normative))
                {
                    if (normative <= 0)
                    {
                        ShowMessage("Значение должно быть больше 0");
                        return;
                    }
                    cncSetup.Normatives.Add(new NormativeEntry() { Type = NormativeEntry.NormativeType.Production, Value = normative, EffectiveFrom = DateTime.Now, IsApproved = dlg.OptionalCheckBox is { IsChecked: true } });
                    ShowMessage("Установлен новый норматив на изготовление");
                }
                else
                {
                    ShowMessage("Отмена");
                }
            }
        }
        private bool CanSetNewProductionNormativeCommandExecute(object p) => !InProgress;
        #endregion

        #region RemoveNormative
        public ICommand RemoveNormativeCommand { get; }
        private async void OnRemoveNormativeCommandExecuted(object p)
        {
            if (!Util.HasFeature(RemeLogFeature.AdvancedEdit)) { ShowMessage("Нет прав на выполнение операции"); return; }

            if (p is FrameworkElement fe && fe.DataContext is NormativeEntry normative)
            {
                try
                {
                    InProgress = true;
                    if (MessageBoxWindow.Show("Удалить норматив?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxDefaultButton.No) != MessageBoxResult.Yes) return;
                    await Database.RemoveNormativeAsync(normative);
                    await LoadSerialPartsAsync();
                }
                catch (Exception ex)
                {
                    MessageBoxWindow.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    InProgress = false;
                }
            }
        }
        private bool CanRemoveNormativeCommandExecute(object p) => true;
        #endregion

        public void MoveOperation(CncOperation targetOperation, bool isAbove)
        {
            if (!Util.HasFeature(RemeLogFeature.AdvancedEdit)) { ShowMessage("Нет прав на выполнение операции"); return; }

            if (DraggedOperation == null || targetOperation == null) return;

            var parentPart = SerialParts.FirstOrDefault(p =>
                p.Operations.Contains(DraggedOperation) &&
                p.Operations.Contains(targetOperation));

            if (parentPart == null) return;

            var operations = parentPart.Operations;
            int oldIndex = operations.IndexOf(DraggedOperation);
            int newIndex = operations.IndexOf(targetOperation);

            if (!isAbove) newIndex++;
            if (newIndex > oldIndex) newIndex--;

            if (oldIndex != newIndex)
            {
                operations.Move(oldIndex, newIndex);
                UpdateOrderIndexes(operations);
            }
        }

        private static void UpdateOrderIndexes(IList<CncOperation> operations)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                operations[i].OrderIndex = i;
            }
        }

        private async Task LoadSerialPartsAsync()
        {
            if (string.IsNullOrEmpty(AppSettings.Instance.ConnectionString))
            {
                Status = "Детали не могут быть загружены т.к. строка подключения не настроена";
                return;
            }

            var parts = await Task.Run(() =>
            {
                var loaded = libeLog.Infrastructure.Database
                    .GetSerialPartsAsync(AppSettings.Instance.ConnectionString,
                        new Progress<string>(p => Status = p))
                    .GetAwaiter().GetResult();

                foreach (var part in loaded)
                {
                    foreach (var operation in part.Operations)
                    {
                        foreach (var setup in operation.Setups)
                        {
                            setup.UpdateDependentProperties();
                        }
                    }
                    part.AcceptChanges();
                }
                return loaded;
            });

            SerialParts = new ObservableCollection<SerialPart>(parts);
            ValidateSerialParts();
        }


        private async void OnSerialPartsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (SerialPart p in e.NewItems)
                    SubscribeToSerialPart(p);

            if (e.OldItems != null)
                foreach (SerialPart p in e.OldItems)
                {
                    UnsubscribeFromSerialPart(p);
                    if (p.Id != -1)
                    {
                        await Database.DeleteSerialPartAsync(p.Id, new Progress<string>(p => Status = p));
                    }
                }
            ValidateSerialParts();
        }

        private void SubscribeToSerialParts(ObservableCollection<SerialPart> SerialParts)
        {
            foreach (var op in SerialParts)
                SubscribeToSerialPart(op);
        }

        private void SubscribeToSerialPart(SerialPart op)
        {
            op.PropertyChanged += OnSerialPartPropertyChanged!;
        }

        private void UnsubscribeFromSerialPart(SerialPart op)
        {
            op.PropertyChanged -= OnSerialPartPropertyChanged!;
        }

        private void OnSerialPartPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            ValidateSerialParts();
            OnPropertyChanged(nameof(IsSaveEnabled));
        }

        private void ValidateSerialParts()
        {
            foreach (var p in SerialParts)
            {
                string error = ValidatePart(p);
                if (!string.IsNullOrEmpty(error))
                {
                    Status = error;
                    IsSaveEnabled = false;
                    return;
                }
            }
            IsSaveEnabled = true;
            Status = "";
        }

        private string ValidatePart(SerialPart part)
        {
            if (string.IsNullOrWhiteSpace(part.PartName))
                return "Ошибка: Название детали не может быть пустым.";

            if (SerialParts.Count(o => o.PartName == part.PartName) > 1)
                return $"Ошибка: Деталь '{part.PartName}' уже существует.";

            if (part.Operations.Any(op => !op.Setups.Any()))
            {
                return $"Ошибка: Деталь '{part.PartName}' содержит операцию без установов.";
            }

            var allSetups = part.Operations.SelectMany(op => op.Setups);
            if (allSetups.Any(s => !s.SetupNormatives.Any()
                                || !s.ProductionNormatives.Any()))
            {
                return $"Ошибка: Деталь '{part.PartName}' содержит установы без норматива.";
            }
            OnPropertyChanged(nameof(IsSaveEnabled));
            return null!;
        }
        private void ShowMessage(string message, int delay = 3000)
        {
            Task.Run(async () =>
            {
                Status = message;
                await Task.Delay(delay);
                if (Status == message) Status = string.Empty;
            });
        }

        private async Task StartBackgroundCheckLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                ValidateSerialParts();
                await Task.Delay(3000, _cts.Token);
            }
        }
    }
}
