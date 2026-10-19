using libeLog;
using libeLog.Base;
using libeLog.Views;
using remeLog.Infrastructure;
using remeLog.Infrastructure.Types;
using remeLog.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace remeLog.ViewModels
{
    /// <summary> Один столбец в чеклисте редактора профиля </summary>
    public class ColumnProfileEditorItem : ViewModel
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int DefaultWidth { get; }

        private bool _IsChecked;
        public bool IsChecked
        {
            get => _IsChecked;
            set => Set(ref _IsChecked, value);
        }

        private int _Width;
        /// <summary> Ширина столбца, px. Равна DefaultWidth, если в профиле нет переопределения. </summary>
        public int Width
        {
            get => _Width;
            set => Set(ref _Width, value);
        }

        public ColumnProfileEditorItem(string id, string displayName, bool isChecked, int defaultWidth, int width)
        {
            Id = id;
            DisplayName = displayName;
            _IsChecked = isChecked;
            DefaultWidth = defaultWidth;
            _Width = width;
        }
    }

    internal class ColumnProfilesWindowViewModel : ViewModel
    {
        public ColumnProfilesWindowViewModel()
        {
            Profiles = new ObservableCollection<ColumnProfile>(
                AppSettings.Instance.ColumnProfiles.Select(p => new ColumnProfile
                {
                    Name = p.Name,
                    ColumnIds = new List<string>(p.ColumnIds),
                    ColumnWidths = new Dictionary<string, double>(p.ColumnWidths),
                }));

            AddProfileCommand = new LambdaCommand(OnAddProfileCommandExecuted, CanAddProfileCommandExecute);
            RenameProfileCommand = new LambdaCommand(OnRenameProfileCommandExecuted, CanRenameProfileCommandExecute);
            DeleteProfileCommand = new LambdaCommand(OnDeleteProfileCommandExecuted, CanDeleteProfileCommandExecute);
            SelectAllColumnsCommand = new LambdaCommand(OnSelectAllColumnsCommandExecuted, CanEditColumnsCommandExecute);
            DeselectAllColumnsCommand = new LambdaCommand(OnDeselectAllColumnsCommandExecuted, CanEditColumnsCommandExecute);
            ApplyRolePresetCommand = new LambdaCommand(OnApplyRolePresetCommandExecuted, CanEditColumnsCommandExecute);

            SelectedProfile = Profiles.FirstOrDefault();
        }

        public ObservableCollection<ColumnProfile> Profiles { get; }

        private ColumnProfile? _SelectedProfile;
        public ColumnProfile? SelectedProfile
        {
            get => _SelectedProfile;
            set
            {
                Set(ref _SelectedProfile, value);
                LoadColumnsForSelectedProfile();
            }
        }

        public ObservableCollection<ColumnProfileEditorItem> Columns { get; } = new();

        private void LoadColumnsForSelectedProfile()
        {
            Columns.Clear();
            if (SelectedProfile == null) return;
            foreach (var id in PartColumnMeta.ColumnOrder.Where(id => id != "Problems"))
            {
                var meta = PartColumnMeta.Map[id];
                var defaultWidth = (int)Math.Round(ColumnWidthDefaults.GetDefault(id));
                var width = SelectedProfile.ColumnWidths.TryGetValue(id, out var overrideWidth)
                    ? (int)Math.Round(overrideWidth)
                    : defaultWidth;
                var item = new ColumnProfileEditorItem(id, meta.DisplayName, SelectedProfile.ColumnIds.Contains(id), defaultWidth, width);
                item.PropertyChanged += (s, e) =>
                {
                    if (SelectedProfile == null) return;
                    switch (e.PropertyName)
                    {
                        case nameof(ColumnProfileEditorItem.IsChecked):
                            if (item.IsChecked)
                            {
                                if (!SelectedProfile.ColumnIds.Contains(item.Id))
                                    SelectedProfile.ColumnIds.Add(item.Id);
                            }
                            else
                            {
                                SelectedProfile.ColumnIds.Remove(item.Id);
                            }
                            break;
                        case nameof(ColumnProfileEditorItem.Width):
                            if (item.Width > 0 && item.Width != item.DefaultWidth)
                                SelectedProfile.ColumnWidths[item.Id] = item.Width;
                            else
                                SelectedProfile.ColumnWidths.Remove(item.Id);
                            break;
                    }
                };
                Columns.Add(item);
            }
        }

        #region EditColumns
        /// <summary> Массово отметить/снять все колонки либо применить набор встроенной роли как отправную точку </summary>
        public ICommand SelectAllColumnsCommand { get; }
        private void OnSelectAllColumnsCommandExecuted(object p)
        {
            foreach (var item in Columns) item.IsChecked = true;
        }

        public ICommand DeselectAllColumnsCommand { get; }
        private void OnDeselectAllColumnsCommandExecuted(object p)
        {
            foreach (var item in Columns) item.IsChecked = false;
        }

        /// <summary> Параметр — имя значения enum User ("Master"/"Engineer") </summary>
        public ICommand ApplyRolePresetCommand { get; }
        private void OnApplyRolePresetCommandExecuted(object p)
        {
            if (p is not string str || !Enum.TryParse(str, out User role)) return;
            var roleColumnIds = PartColumnMeta.GetColumnIdsForRole(role);
            foreach (var item in Columns) item.IsChecked = roleColumnIds.Contains(item.Id);
        }

        private bool CanEditColumnsCommandExecute(object p) => SelectedProfile != null;
        #endregion

        #region AddProfile
        public ICommand AddProfileCommand { get; }
        private void OnAddProfileCommandExecuted(object p)
        {
            var dlg = new UserInputDialogWindow("Новый профиль", "Введите название профиля столбцов:") { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.UserInput)) return;
            if (Profiles.Any(pr => pr.Name.Equals(dlg.UserInput, System.StringComparison.OrdinalIgnoreCase)))
            {
                MessageBoxWindow.Show("Профиль с таким названием уже существует", "Профили столбцов", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var profile = new ColumnProfile { Name = dlg.UserInput.Trim(), ColumnIds = new List<string>(), ColumnWidths = new Dictionary<string, double>() };
            Profiles.Add(profile);
            SelectedProfile = profile;
        }
        private static bool CanAddProfileCommandExecute(object p) => true;
        #endregion

        #region RenameProfile
        public ICommand RenameProfileCommand { get; }
        private void OnRenameProfileCommandExecuted(object p)
        {
            if (SelectedProfile == null) return;
            var dlg = new UserInputDialogWindow("Переименование профиля", "Введите новое название профиля:", SelectedProfile.Name, focusAndSelect: true) { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.UserInput)) return;
            if (Profiles.Any(pr => pr != SelectedProfile && pr.Name.Equals(dlg.UserInput, System.StringComparison.OrdinalIgnoreCase)))
            {
                MessageBoxWindow.Show("Профиль с таким названием уже существует", "Профили столбцов", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var index = Profiles.IndexOf(SelectedProfile);
            SelectedProfile.Name = dlg.UserInput.Trim();
            Profiles.RemoveAt(index);
            Profiles.Insert(index, SelectedProfile);
            SelectedProfile = Profiles[index];
        }
        private bool CanRenameProfileCommandExecute(object p) => SelectedProfile != null;
        #endregion

        #region DeleteProfile
        public ICommand DeleteProfileCommand { get; }
        private void OnDeleteProfileCommandExecuted(object p)
        {
            if (SelectedProfile == null) return;
            if (MessageBoxWindow.Show($"Удалить профиль '{SelectedProfile.Name}'?", "Профили столбцов", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxDefaultButton.No) != MessageBoxResult.Yes)
                return;
            var index = Profiles.IndexOf(SelectedProfile);
            Profiles.RemoveAt(index);
            SelectedProfile = Profiles.Count > 0 ? Profiles[System.Math.Min(index, Profiles.Count - 1)] : null;
        }
        private bool CanDeleteProfileCommandExecute(object p) => SelectedProfile != null;
        #endregion
    }
}
