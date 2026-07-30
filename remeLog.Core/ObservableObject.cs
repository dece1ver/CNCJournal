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
        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Поднимает <see cref="PropertyChanged"/> для указанного (или вызывающего) свойства.</summary>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>Присваивает поле и поднимает <see cref="PropertyChanged"/>, если значение изменилось.</summary>
        protected virtual bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null!)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
