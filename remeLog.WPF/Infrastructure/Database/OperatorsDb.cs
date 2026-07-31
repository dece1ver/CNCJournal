using libeLog.Infrastructure;
using libeLog.Models;
using System.Threading.Tasks;

namespace remeLog.Infrastructure
{
    public static class OperatorsDatabase
    {
        public static async Task<int> RemoveNormativeAsync(NormativeEntry normative)
        {
            return await libeLog.Infrastructure.Database.RemoveByIdAsync(
                AppSettings.Instance.ConnectionString!, normative.Id.ToString(), "cnc_normatives");
        }
    }
}
