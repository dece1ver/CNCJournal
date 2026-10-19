using libeLog.Base;
using System.Windows;

namespace remeLog.Infrastructure.Commands
{
    internal class CloseWindowCommand : Command
    {
        public override bool CanExecute(object? parameter) => parameter is Window;

        public override void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            var window = (Window)parameter!;
            window.Close();
        }
    }
}