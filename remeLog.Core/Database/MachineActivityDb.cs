using Dapper;
using remeLog.Core;
using remeLog.Core.Db;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace remeLog.Infrastructure
{
    public static partial class Database
    {
        /// <summary>
        /// Читает текущий heartbeat-статус станков из cnc_machine_activity (пишет eLog,
        /// пока строка ещё не закрыта). Станки без единой записи в выдачу не попадают —
        /// объединение с полным списком станков (для показа "нет данных") на совести вызывающей стороны.
        /// </summary>
        public static async Task<List<MachineActivity>> ReadMachineActivityAsync()
        {
            try
            {
                await using var conn = await DbHelper.OpenConnectionAsync(DomainSettings.ConnectionString);
                var rows = (await conn.QueryAsync<(string Machine, string Status, string PartName, string Order,
                    string Operator, byte Setup, string Shift, DateTime? PhaseStartLocal, DateTime UpdatedUtc)>(@"
                    SELECT Machine, Status, PartName, [Order], Operator, Setup, Shift, PhaseStartLocal, UpdatedUtc
                    FROM cnc_machine_activity")).AsList();

                return rows.Select(r => new MachineActivity
                {
                    Machine = r.Machine,
                    Status = r.Status,
                    PartName = r.PartName ?? string.Empty,
                    Order = r.Order ?? string.Empty,
                    Operator = r.Operator ?? string.Empty,
                    Setup = r.Setup,
                    Shift = r.Shift?.Trim() ?? string.Empty,
                    // Бизнес-время (как parts.StartSetupTime) — уже локальное, в отличие от UpdatedUtc.
                    PhaseStartLocal = r.PhaseStartLocal,
                    UpdatedLocal = DateTime.SpecifyKind(r.UpdatedUtc, DateTimeKind.Utc).ToLocalTime()
                }).ToList();
            }
            catch (Exception ex)
            {
                Log.WriteError(ex, null);
                return new List<MachineActivity>();
            }
        }
    }
}
