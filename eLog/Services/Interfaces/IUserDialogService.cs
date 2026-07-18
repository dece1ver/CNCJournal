using libeLog.Views;

namespace eLog.Services.Interfaces
{
    internal interface IUserDialogService
    {
        bool Edit(object item);

        void ShowInfo(string message, string caption);

        void ShowWarning(string message, string caption);

        void ShowError(string message, string caption);

        bool Confirm(string message, string caption, bool Exclamation = false, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.No);
    }
}