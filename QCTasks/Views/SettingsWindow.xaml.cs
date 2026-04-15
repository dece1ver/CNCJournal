using QCTasks.ViewModels;

namespace QCTasks.Views;

public partial class SettingsWindow : DialogBase
{
    public SettingsWindow()
    {
        InitializeComponent();

        var vm = new SettingsViewModel();
        vm.CloseAction = Close;
        DataContext = vm;
    }
}