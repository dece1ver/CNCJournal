using libeLog.Infrastructure;
using libeLog.Models;

namespace remeLog.Infrastructure
{
    public static class CatalogDatabase
    {
        public static DbResult<int?> GetMachineSetupLimit(this string machine)
        {
            if (AppSettings.Instance.ConnectionString == null)
                return DbResult<int?>.Fail(DbResult.Error, "Невозможно получить лимит наладки т.к. отсутствует строка подключения");
            return machine.GetMachineSetupLimit(AppSettings.Instance.ConnectionString);
        }

        public static DbResult<double?> GetMachineSetupCoefficient(this string machine)
        {
            if (AppSettings.Instance.ConnectionString == null)
                return DbResult<double?>.Fail(DbResult.Error, "Невозможно получить коэффициент лимита наладки т.к. отсутствует строка подключения");
            return machine.GetMachineSetupCoefficient(AppSettings.Instance.ConnectionString);
        }
    }
}
