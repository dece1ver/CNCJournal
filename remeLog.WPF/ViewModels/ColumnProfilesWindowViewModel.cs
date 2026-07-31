using libeLog;
using libeLog.Base;
using libeLog.Views;
using remeLog.Infrastructure;
using remeLog.Views;
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

        private bool _IsChecked;
        public bool IsChecked
        {
            get => _IsChecked;
            set => Set(ref _IsChecked, value);
        }

        public ColumnProfileEditorItem(string id, string displayName, bool isChecked)
        {
            Id = id;
            DisplayName = displayName;
            _IsChecked = isChecked;
        }
    }

    internal class ColumnProfilesWindowViewModel : ViewModel
    {
        public ColumnProfilesWindowViewModel()
        {
            Profiles = new ObservableCollection<ColumnProfile>(
                AppSettings.Instance.ColumnProfiles.Select(p => new ColumnProfile { Name = p.Name, ColumnIds = new List<string>(p.ColumnIds) }));

            AddProfileCommand = new LambdaCommand(OnAddProfileCommandExecuted, CanAddProfileCommandExecute);
            RenameProfileCommand = new LambdaCommand(OnRenameProfileCommandExecuted, CanRenameProfileCommandExecute);
            DeleteProfileCommand = new LambdaCommand(OnDeleteProfileCommandExecuted, CanDeleteProfileCommandExecute);

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
                var item = new ColumnProfileEditorItem(id, meta.DisplayName, SelectedProfile.ColumnIds.Contains(id));
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName != nameof(ColumnProfileEditorItem.IsChecked) || SelectedProfile == null) return;
                    if (item.IsChecked)
                    {
                        if (!SelectedProfile.ColumnIds.Contains(item.Id))
                            SelectedProfile.ColumnIds.Add(item.Id);
                    }
                    else
                    {
                        SelectedProfile.ColumnIds.Remove(item.Id);
                    }
                };
                Columns.Add(item);
            }
        }

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
            var profile = new ColumnProfile { Name = dlg.UserInput.Trim(), ColumnIds = new List<string>() };
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
