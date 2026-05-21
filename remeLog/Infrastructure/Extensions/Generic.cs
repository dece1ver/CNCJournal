using System;

namespace remeLog.Infrastructure.Extensions
{
    public static class Generic
    {
        public static T Tap<T>(this T obj, Action<T> action)
        {
            action(obj);
            return obj;
        }
    }
}
