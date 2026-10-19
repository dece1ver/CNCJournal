using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Infrastructure.Extensions
{
    public enum GetDoubleOption
    {

    }

    public static class Object
    {
        /// <summary>
        /// Преобразует объект в число типа <see cref="double"/>. 
        /// Если преобразование невозможно, возвращает значение по умолчанию.
        /// </summary>
        /// <param name="obj">Объект, который требуется преобразовать.</param>
        /// <param name="defaultValue">Значение по умолчанию, возвращаемое при неудачном преобразовании (по умолчанию 0).</param>
        /// <returns>Число типа <see cref="double"/>, если преобразование прошло успешно, иначе <paramref name="defaultValue"/>.</returns>
        public static double GetDouble(this object obj, double defaultValue = 0)
        {
            if (obj is double d) return d;
            return defaultValue;
        }

    }
}
