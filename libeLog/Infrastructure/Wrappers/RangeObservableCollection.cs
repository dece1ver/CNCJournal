using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace libeLog.Infrastructure.Wrappers
{
    public sealed class RangeObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotification;

        protected override void OnCollectionChanged(
            NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotification)
                base.OnCollectionChanged(e);
        }

        public void ReplaceAll(IEnumerable<T> items)
        {
            _suppressNotification = true;

            Items.Clear();

            foreach (var item in items)
                Items.Add(item);

            _suppressNotification = false;

            OnCollectionChanged(
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Reset));
        }
    }
}
