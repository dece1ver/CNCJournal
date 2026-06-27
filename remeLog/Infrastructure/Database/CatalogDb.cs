using Dapper;
using libeLog.Infrastructure;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using remeLog.Models.Reports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static libeLog.Infrastructure.Db.DbHelper;
using Machine = remeLog.Models.Machine;

namespace remeLog.Infrastructure
{
    public static partial class Database
    {
        public async static Task<IEnumerable<Qualification>> GetQualificationsAsync(IProgress<string>? progress = null)
        {
            progress?.Report("Подключение к БД...");
            var sql = @"SELECT [Qualification],
                [EfficiencyValueHH],[EfficiencyCoefficientHH],[EfficiencyValueH],[EfficiencyCoefficientH],
                [EfficiencyValueN],[EfficiencyCoefficientN],[EfficiencyValueL],[EfficiencyCoefficientL],
                [EfficiencyValueLL],[EfficiencyCoefficientLL],[EfficiencyValueLLL],[EfficiencyCoefficientLLL],
                [DownTimesValueHH],[DownTimesCoefficientHH],[DownTimesValueH],[DownTimesCoefficientH],
                [DownTimesValueN],[DownTimesCoefficientN],[DownTimesValueL],[DownTimesCoefficientL],
                [DownTimesValueLL],[DownTimesCoefficientLL],[DownTimesValueLLL],[DownTimesCoefficientLLL],
                [NonSerialEfficiencyValueHH],[NonSerialEfficiencyCoefficientHH],
                [NonSerialEfficiencyValueH],[NonSerialEfficiencyCoefficientH],
                [NonSerialEfficiencyValueN],[NonSerialEfficiencyCoefficientN],
                [NonSerialEfficiencyValueL],[NonSerialEfficiencyCoefficientL],
                [NonSerialEfficiencyValueLL],[NonSerialEfficiencyCoefficientLL],
                [NonSerialEfficiencyValueLLL],[NonSerialEfficiencyCoefficientLLL] FROM cnc_qualifications;";

            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                progress?.Report("Чтение данных из БД...");
                var rows = (await conn.QueryAsync<(int, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double, double)>(sql)).AsList();
                var result = rows.Select(r => new Qualification(
                    r.Item1, r.Item2, r.Item3, r.Item4, r.Item5, r.Item6, r.Item7, r.Item8, r.Item9, r.Item10,
                    r.Item11, r.Item12, r.Item13, r.Item14, r.Item15, r.Item16, r.Item17, r.Item18, r.Item19, r.Item20,
                    r.Item21, r.Item22, r.Item23, r.Item24, r.Item25, r.Item26, r.Item27, r.Item28, r.Item29, r.Item30,
                    r.Item31, r.Item32, r.Item33, r.Item34, r.Item35, r.Item36, r.Item37)).ToList();
                progress?.Report("Чтение завершено");
                return result;
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return Enumerable.Empty<Qualification>();
            }
        }

        public async static Task<List<Machine>> GetMachinesAsync(IProgress<string> progress)
        {
            progress.Report("Подключение к БД...");
            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                progress.Report("Чтение данных из БД...");
                var machines = (await conn.QueryAsync<Machine>(@"
                    SELECT * FROM cnc_machines WHERE IsActive = 1")).AsList();
                progress.Report("Чтение завершено");
                return machines;
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return new List<Machine>();
            }
        }

        public async static Task<bool> GetMachineSerialStatus(string machine, IProgress<string>? progress = null)
        {
            progress?.Report("Подключение к БД...");
            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                progress?.Report("Чтение данных из БД...");
                return await conn.QueryFirstOrDefaultAsync<bool>(
                    "SELECT IsSerial FROM cnc_machines WHERE Name = @Machine", new { Machine = machine });
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return false;
            }
        }

        public async static Task<List<DateTime>> GetHolidaysAsync(IProgress<string>? progress)
        {
            progress?.Report("Подключение к БД...");
            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                progress?.Report("Чтение данных из БД...");
                var holidays = (await conn.QueryAsync<DateTime>(
                    "SELECT Holidays FROM cnc_remelog_config")).AsList();
                progress?.Report("Чтение завершено");
                return holidays;
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return new List<DateTime>();
            }
        }

        public async static Task<List<ToolSearchCase>> GetToolSearchCasesAsync(List<Guid> guids, IProgress<string> progress)
        {
            var cases = new List<ToolSearchCase>();
            if (guids == null || guids.Count == 0) return cases;

            progress.Report("Формирую запрос...");
            const int chunkSize = 2000;
            var chunks = guids.Select((g, i) => new { g, i })
                .GroupBy(x => x.i / chunkSize)
                .Select(g => g.Select(x => x.g).ToList()).ToList();

            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                foreach (var chunk in chunks)
                {
                    progress.Report($"Запрашиваю данные...");
                    var chunkCases = await conn.QueryAsync<ToolSearchCase>(
                        "SELECT * FROM cnc_tool_search_cases WHERE PartGuid IN @guids",
                        new { guids = chunk });
                    cases.AddRange(chunkCases);
                }
                progress.Report("Чтение завершено.");
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
            }
            return cases;
        }

        public static DbResult<List<string>> ReadMachines()
        {
            try
            {
                using var conn = OpenConnection(AppSettings.Instance.ConnectionString);
                var machines = conn.Query<string>("SELECT Name FROM cnc_machines ORDER BY Name").AsList();
                return DbResult<List<string>>.Ok(machines);
            }
            catch (SqlException sqlEx)
            {
                Util.WriteLog(sqlEx, sqlEx.Number.ToString());
                return sqlEx.Number == 18456
                    ? DbResult<List<string>>.Fail(DbResult.AuthError, "Ошибка авторизации.")
                    : DbResult<List<string>>.Fail(DbResult.Error, sqlEx.Number.ToString());
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return DbResult<List<string>>.FailWithError(ex.Message);
            }
        }

        public static async Task<DbResult<List<MachineFilter>>> ReadMachinesAsync()
        {
            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                const string sql = "SELECT Name AS Machine, Type, CAST(0 AS bit) AS Filter FROM cnc_machines WHERE IsActive = 1 ORDER BY Name";
                var machines = (await conn.QueryAsync<MachineFilter>(sql)).AsList();
                return DbResult<List<MachineFilter>>.Ok(machines);
            }
            catch (SqlException sqlEx)
            {
                Util.WriteLog(sqlEx, sqlEx.Number.ToString());
                return sqlEx.Number == 18456
                    ? DbResult<List<MachineFilter>>.Fail(DbResult.AuthError, "Ошибка авторизации.")
                    : DbResult<List<MachineFilter>>.Fail(DbResult.Error, sqlEx.Number.ToString());
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return DbResult<List<MachineFilter>>.FailWithError(ex.Message);
            }
        }

        public static DbResult<List<(string Reason, bool RequireComment)>> ReadDeviationReasons(DeviationReasonType type)
        {
            try
            {
                const string sql = "SELECT Reason, RequireComment FROM cnc_deviation_reasons WHERE Type IS NULL OR Type = @Type ORDER BY Reason ASC";
                using var conn = OpenConnection(AppSettings.Instance.ConnectionString);
                var reasons = conn.Query<(string Reason, bool RequireComment)>(sql, new { Type = type.ToString() }).AsList();
                return DbResult<List<(string Reason, bool RequireComment)>>.Ok(reasons);
            }
            catch (SqlException sqlEx)
            {
                Util.WriteLog(sqlEx, sqlEx.Number.ToString());
                return DbResult<List<(string Reason, bool RequireComment)>>.Fail(DbResult.Error, sqlEx.Number.ToString());
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return DbResult<List<(string Reason, bool RequireComment)>>.FailWithError(ex.Message);
            }
        }

        public static DbResult<List<string>> ReadDowntimeReasons()
        {
            try
            {
                using var conn = OpenConnection(AppSettings.Instance.ConnectionString);
                var reasons = conn.Query<string>("SELECT Reason FROM cnc_downtime_reasons ORDER BY Reason ASC").AsList();
                return DbResult<List<string>>.Ok(reasons);
            }
            catch (SqlException sqlEx)
            {
                Util.WriteLog(sqlEx, sqlEx.Number.ToString());
                return sqlEx.Number == 18456
                    ? DbResult<List<string>>.Fail(DbResult.AuthError, "Ошибка авторизации.")
                    : DbResult<List<string>>.Fail(DbResult.Error, sqlEx.Number.ToString());
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return DbResult<List<string>>.FailWithError(ex.Message);
            }
        }

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

        public static DbResult<WncConfig> GetWncConfig()
        {
            try
            {
                using var conn = OpenConnection(AppSettings.Instance.ConnectionString);
                var row = conn.QueryFirstOrDefault<(string Server, string User, string Password, string LocalType)>(
                    "SELECT Server, CncUser, CncPassword, LocalType FROM cnc_wnc_cfg");
                if (row == default)
                    return DbResult<WncConfig>.NotFound();
                return DbResult<WncConfig>.Ok(new WncConfig(row.Server, row.User, row.Password, row.LocalType));
            }
            catch (SqlException sqlEx)
            {
                Util.WriteLog(sqlEx, sqlEx.Number.ToString());
                return sqlEx.Number == 18456
                    ? DbResult<WncConfig>.Fail(DbResult.AuthError, "Ошибка авторизации.")
                    : DbResult<WncConfig>.Fail(DbResult.Error, sqlEx.Number.ToString());
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return DbResult<WncConfig>.FailWithError(ex.Message);
            }
        }

        internal static async Task UpdateAppSettings()
        {
            await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);

            var administrators = new List<string>();
            var operations = new List<string>();
            var holidays = new List<DateTime>();
            var engineerComments = new List<string>();

            var rows = await conn.QueryAsync<(double? max_setup_limit, double? long_setup_limit, string? NcArchivePath, string? NcIntermediatePath, string? Administrators, string? CncOperations, DateTime? Holidays, string? PcaReportPath, string? EngineerComments, string? AiIp, string? AiModel)>(
                "SELECT max_setup_limit, long_setup_limit, NcArchivePath, NcIntermediatePath, Administrators, CncOperations, Holidays, PcaReportPath, EngineerComments, AiIp, AiModel FROM cnc_remelog_config");

            foreach (var row in rows)
            {
                if (row.max_setup_limit.HasValue) AppSettings.MaxSetupLimit = row.max_setup_limit.Value;
                if (row.long_setup_limit.HasValue) AppSettings.LongSetupLimit = row.long_setup_limit.Value;
                if (row.NcArchivePath != null) AppSettings.NcArchivePath = row.NcArchivePath;
                if (row.NcIntermediatePath != null) AppSettings.NcIntermediatePath = row.NcIntermediatePath;
                if (row.Administrators != null) administrators.Add(row.Administrators);
                if (row.CncOperations != null) operations.Add(row.CncOperations);
                if (row.Holidays.HasValue) holidays.Add(row.Holidays.Value);
                if (row.PcaReportPath != null) AppSettings.PcaReportPath = row.PcaReportPath;
                if (row.EngineerComments != null) engineerComments.Add(row.EngineerComments);
                if (row.AiIp != null) AppSettings.AiIp = row.AiIp;
                if (!string.IsNullOrWhiteSpace(row.AiModel)) AppSettings.AiModel = row.AiModel;
            }

            AppSettings.Administrators = administrators.ToArray();
            AppSettings.CncOperations = operations.ToArray();
            AppSettings.Holidays = holidays.ToArray();
            AppSettings.EngineerComments = engineerComments.ToArray();

            AppSettings.MaxSetupLimits.Clear();
            var limits = await conn.QueryAsync<(string Name, double SetupCoefficient)>(
                "SELECT Name, SetupCoefficient FROM cnc_machines");
            foreach (var (name, coeff) in limits)
                AppSettings.MaxSetupLimits[name] = coeff;

            AppSettings.Save();
        }
    }
}
