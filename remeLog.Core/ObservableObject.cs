using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace remeLog.Core
{
    /// <summary>
    /// Базовый класс для доменных моделей remeLog.Core. Замена <c>libeLog.Base.ViewModel</c>,
    /// не зависящая от WPF: WPF-биндинг сам маршалит уведомления на UI-поток для стандартных
    /// сценариев, поэтому явный dispatcher-маршалинг здесь не нужен.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null!)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
