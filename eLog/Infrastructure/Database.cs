using Dapper;
using eLog.Infrastructure.Extensions;
using eLog.Models;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Infrastructure.Db;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Machine = eLog.Models.Machine;
using static libeLog.Infrastructure.Db.DbHelper;

namespace eLog.Infrastructure
{
    public static class Database
    {
        public static string TryGetUpdatePath()
        {
            try
            {
                using var conn = OpenConnection(AppSettings.Instance.ConnectionString);
                return conn.QueryFirstOrDefault<string>("SELECT UpdatePath FROM cnc_elog_config") ?? "";
            }
            catch
            {
                return "";
            }
        }

        public static bool TryGetOrdersPath(out string ordersPath)
        {
            ordersPath = null!;
            try
            {
                using var conn = OpenConnection(AppSettings.Instance.ConnectionString);
                ordersPath = conn.QueryFirstOrDefault<string>("SELECT OrdersXlPath FROM cnc_elog_config") ?? "";
                return ordersPath != "";
            }
            catch
            {
                return false;
            }
        }

        public async static Task<ObservableCollection<Operator>> GetOperatorsAsync(IProgress<string>? progress = null)
        {
            var operators = new ObservableCollection<Operator>();
            progress?.Report("Подключение к БД...");
            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                progress?.Report("Чтение данных об операторах из БД...");
                var rows = await conn.QueryAsync<(string FirstName, string LastName, string Patronymic)>(
                    "SELECT FirstName, LastName, Patronymic FROM cnc_operators WHERE IsActive = 1 ORDER BY LastName ASC");
                foreach (var (first, last, patr) in rows)
                    operators.Add(new Operator { FirstName = first, LastName = last, Patronymic = patr });
                progress?.Report("Чтение завершено");
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
            }
            return operators;
        }

        public async static Task<ObservableCollection<Machine>> GetMachinesAsync(string connectionString = null!, IProgress<string>? progress = null)
        {
            var machines = new ObservableCollection<Machine>();
            connectionString ??= AppSettings.Instance.ConnectionString;
            progress?.Report("Подключение к БД...");
            try
            {
                await using var conn = await OpenConnectionAsync(connectionString);
                progress?.Report("Чтение данных о станках из БД...");
                var names = await conn.QueryAsync<string>(
                    "SELECT Name FROM cnc_machines WHERE IsActive = 1 ORDER BY Name ASC");
                foreach (var name in names)
                    machines.Add(new Machine(name));
                progress?.Report("Чтение завершено");
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
            }
            return machines;
        }

        public async static Task<string[]> GetOrderQualifiersAsync(IProgress<string>? progress = null)
        {
            progress?.Report("Подключение к БД...");
            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                progress?.Report("Чтение данных об операторах из БД...");
                var prefixes = await conn.QueryFirstOrDefaultAsync<string>(
                    "SELECT OrderPrefixes FROM cnc_elog_config");
                if (prefixes is null) return Array.Empty<string>();
                var qualifiers = new HashSet<string>(prefixes.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0));
                return qualifiers.OrderBy(o => o).ToArray();
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return Array.Empty<string>();
            }
        }

        public static async Task<string> GetAssignedPartsGsIdAsync(IProgress<string>? progress = null)
        {
            progress?.Report("Подключение к БД...");
            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                progress?.Report("Чтение данных из БД...");
                return await conn.QueryFirstOrDefaultAsync<string>(
                    "SELECT AssignedPartsGsId FROM cnc_elog_config") ?? "";
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return "";
            }
        }

        public static async Task<DbResult> SendHardwareFailureMessage(string message)
        {
            if (AppSettings.Instance.DebugMode) Util.WriteLog("Добавление информации об изготовлении в БД.");
            const string sql = @"INSERT INTO maintenance_log (machine, creation_date, rq_status, comments, plandate)
                VALUES (@Machine, @Date, @Status, @Comment, @PlanDate);";
            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                await conn.ExecuteAsync(sql, new
                {
                    Machine = AppSettings.Instance.Machine?.Name ?? "",
                    Date = DateTime.Now,
                    Status = "Открыто",
                    Comment = message,
                    PlanDate = DateTime.Today.AddDays(7)
                });
                return DbResult.Ok;
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case -1:
                        Util.WriteLog("База данных недоступна.");
                        return DbResult.NoConnection;
                    case 18456:
                        Util.WriteLog($"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
                        return DbResult.AuthError;
                    default:
                        Util.WriteLog(sqlEx, $"Ошибка №{sqlEx.Number}:");
                        return DbResult.Error;
                }
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return DbResult.Error;
            }
        }

        public static async Task<DbResult> WriteShiftHandover(ShiftHandOverInfo shiftInfo)
        {
            var who = shiftInfo.Giver ? "Giver" : "Reciever";
            var sql = $@"
                MERGE INTO cnc_shifts AS target
                USING (VALUES (@ShiftDate, @ShiftType, @Machine, @Master, @UnspecifiedDowntimes, 
                               @DowntimesComment, @CommonComment, @IsChecked, @WorkplaceCleaned, 
                               @Failures, @ExtraneousNoises, @LiquidLeaks, @ToolBreakage, @CoolantConcentration))
                AS source (ShiftDate, ShiftType, Machine, Master, UnspecifiedDowntimes, 
                           DowntimesComment, CommonComment, IsChecked, WorkplaceCleaned, 
                           Failures, ExtraneousNoises, LiquidLeaks, ToolBreakage, CoolantConcentration)
                ON target.ShiftDate = source.ShiftDate AND target.Shift = source.ShiftType AND target.Machine = source.Machine
                WHEN MATCHED THEN
                    UPDATE SET
                        target.{who}WorkplaceCleaned = source.WorkplaceCleaned,
                        target.{who}Failures = source.Failures,
                        target.{who}ExtraneousNoises = source.ExtraneousNoises,
                        target.{who}LiquidLeaks = source.LiquidLeaks,
                        target.{who}ToolBreakage = source.ToolBreakage,
                        target.{who}CoolantConcentration = source.CoolantConcentration
                WHEN NOT MATCHED THEN
                    INSERT (ShiftDate, Shift, Machine, Master, UnspecifiedDowntimes, DowntimesComment, CommonComment, IsChecked, 
                            {who}WorkplaceCleaned, {who}Failures, {who}ExtraneousNoises, {who}LiquidLeaks, {who}ToolBreakage, {who}CoolantConcentration)
                    VALUES (source.ShiftDate, source.ShiftType, source.Machine, source.Master, source.UnspecifiedDowntimes, source.DowntimesComment, source.CommonComment, source.IsChecked, 
                            source.WorkplaceCleaned, source.Failures, source.ExtraneousNoises, source.LiquidLeaks, source.ToolBreakage, source.CoolantConcentration);";
            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                await conn.ExecuteAsync(sql, new
                {
                    ShiftDate = shiftInfo.Date,
                    ShiftType = shiftInfo.Type,
                    Machine = shiftInfo.Machine,
                    Master = "",
                    UnspecifiedDowntimes = 0,
                    DowntimesComment = "",
                    CommonComment = "",
                    IsChecked = false,
                    WorkplaceCleaned = shiftInfo.WorkplaceCleaned,
                    Failures = shiftInfo.Failures,
                    ExtraneousNoises = shiftInfo.ExtraneousNoises,
                    LiquidLeaks = shiftInfo.LiquidLeaks,
                    ToolBreakage = shiftInfo.ToolBreakage,
                    CoolantConcentration = shiftInfo.CoolantConcentration
                });
                return DbResult.Ok;
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case -1:
                        Util.WriteLog("База данных недоступна.");
                        return DbResult.NoConnection;
                    case 18456:
                        Util.WriteLog($"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
                        return DbResult.AuthError;
                    default:
                        Util.WriteLog(sqlEx, $"Ошибка №{sqlEx.Number}:");
                        return DbResult.Error;
                }
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return DbResult.Error;
            }
        }

        public static async Task<(DbResult Result, List<string> ToolTypes, string? Error)> GetSearchToolTypes()
        {
            var toolTypes = new List<string>();
            if (string.IsNullOrWhiteSpace(AppSettings.Instance.ConnectionString))
                return (DbResult.Error, toolTypes, "NO CONNECTION STRING");
            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                var rows = await conn.QueryAsync<string>(
                    "SELECT SearchToolTypes FROM cnc_elog_config WHERE SearchToolTypes IS NOT NULL");
                toolTypes.AddRange(rows.Where(r => r != null));
                return toolTypes.Any()
                    ? (DbResult.Ok, toolTypes, null)
                    : (DbResult.NotFound, toolTypes, "EMPTY");
            }
            catch (SqlException sqlEx)
            {
                return sqlEx.Number switch
                {
                    18456 => (DbResult.AuthError, toolTypes, sqlEx.Number.ToString()),
                    _ => (DbResult.Error, toolTypes, sqlEx.Number.ToString()),
                };
            }
            catch (Exception ex)
            {
                return (DbResult.Error, toolTypes, ex.Message);
            }
        }

        public static DbResult<int?> GetMachineSetupLimit(this string machine)
            => machine.GetMachineSetupLimit(AppSettings.Instance.ConnectionString);

        public static DbResult<double?> GetMachineSetupCoefficient(this string machine)
            => machine.GetMachineSetupCoefficient(AppSettings.Instance.ConnectionString);
    }
}
