using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace remeLog.Infrastructure.Extensions
{
    public static class ObservableCollectionExtension
    {
        /// <summary>
        /// Полностью заменяет содержимое коллекции.
        /// </summary>
        public static void ReplaceAll<T>(
            this ObservableCollection<T> collection,
            IEnumerable<T> items)
        {
            collection.Clear();

            foreach (var item in items)
                collection.Add(item);
        }
    }
}
