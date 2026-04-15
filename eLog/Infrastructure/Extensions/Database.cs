using eLog.Models;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Machine = eLog.Models.Machine;

namespace eLog.Infrastructure.Extensions
{
    public static class Database
    {

        public static string TryGetUpdatePath()
        {
            try
            {
                using SqlConnection connection = new(AppSettings.Instance.ConnectionString);
                connection.Open();
                var query = "SELECT UpdatePath FROM cnc_elog_config";
                using SqlCommand command = new(query, connection);
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    return reader.GetString(0);
                }
                return "";
            }
            catch
            {
                return "";
            }
        }

        public static bool TryGetOrdersPath(out string ordersPath)
        {
            ordersPath = null!;
            bool result = false;
            try
            {
                using SqlConnection connection = new(AppSettings.Instance.ConnectionString);
                connection.Open();
                var query = "SELECT OrdersXlPath FROM cnc_elog_config";
                using SqlCommand command = new(query, connection);
                var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    ordersPath = reader.GetString(0);
                    result = true;
                    if (result) break;
                }
                return result;
            }
            catch
            {
                return result;
            }

        }

        public async static Task<ObservableCollection<Operator>> GetOperatorsAsync(IProgress<string>? progress = null)
        {
            ObservableCollection<Operator> operators = new();

            await Task.Run(async () =>
            {
                progress?.Report("Подключение к БД...");
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync();
                    string query = $"SELECT * FROM cnc_operators WHERE IsActive = 1 ORDER BY LastName ASC;";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            progress?.Report("Чтение данных об операторах из БД...");
                            while (await reader.ReadAsync())
                            {
                                operators.Add(new Operator() { 
                                    FirstName = reader.GetStringOrEmpty(1), 
                                    LastName = reader.GetStringOrEmpty(2), 
                                    Patronymic = reader.GetStringOrEmpty(3) });
                            }
                        }
                    }
                }
                progress?.Report("Чтение завершено");
            });
            return operators;
        }

        public async static Task<ObservableCollection<Machine>> GetMachinesAsync(string connectionString = null!, IProgress<string>? progress = null)
        {
            ObservableCollection<Machine> machines = new();
            connectionString ??= AppSettings.Instance.ConnectionString;
            await Task.Run(async () =>
            {
                progress?.Report("Подключение к БД...");
                using (SqlConnection connection = new(connectionString))
                {
                    await connection.OpenAsync();
                    string query = $"SELECT * FROM cnc_machines WHERE IsActive = 1 ORDER BY Name ASC;";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            progress?.Report("Чтение данных о станках из БД...");
                            while (await reader.ReadAsync())
                            {
                                machines.Add(new Machine(reader.GetStringOrEmpty(1)));
                            }
                        }
                    }
                }
                progress?.Report("Чтение завершено");
            });
            return machines;
        }

        public async static Task<string[]> GetOrderQualifiersAsync(IProgress<string>? progress = null)
        {
            var orderQualifiers = new HashSet<string>();
            await Task.Run(async () =>
            {
                progress?.Report("Подключение к БД...");
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync();
                    string query = $"SELECT OrderPrefixes FROM cnc_elog_config;";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            progress?.Report("Чтение данных об операторах из БД...");
                            while (await reader.ReadAsync())
                            {
                                if (!(await reader.IsDBNullAsync(0))) orderQualifiers.Add(await reader.GetFieldValueAsync<string>(0));
                            }
                        }
                    }
                }
                progress?.Report("Чтение завершено");
            });
            return orderQualifiers.OrderBy(o => o).ToArray();
        }

        public static async Task<string> GetAssignedPartsGsIdAsync(IProgress<string>? progress = null)
        {
            progress?.Report("Подключение к БД...");
            await using SqlConnection connection = new(AppSettings.Instance.ConnectionString);
            await connection.OpenAsync();

            const string query = "SELECT AssignedPartsGsId FROM cnc_elog_config;";
            await using SqlCommand command = new(query, connection);
            await using SqlDataReader reader = await command.ExecuteReaderAsync();

            progress?.Report("Чтение данных из БД...");
            while (await reader.ReadAsync())
            {
                if (!await reader.IsDBNullAsync(0))
                    return await reader.GetFieldValueAsync<string>(0);
            }

            return "";
        }

        /// <summary>
        /// Запись информации о детали в БД
        /// </summary>
        /// <param name="part">Деталь</param>
        /// <param name="passive">Нужно ли присваивать Id, false используется для одновременной работы с двумя источниками, тогда Id назначается при записи в XL</param>
        /// <returns></returns>
        public async static Task<DbResult> WritePartAsync(this Part part, bool passive = false)
        {
            if (AppSettings.Instance.DebugMode) Util.WriteLog(part, "Добавление информации об изготовлении в БД.");
            try
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync();
                    if (AppSettings.Instance.DebugMode) Util.WriteLog("Соединение к БД открыто.");
                    var partIndex = AppSettings.Instance.Parts.IndexOf(part);
                    var prevPart = partIndex != -1 && AppSettings.Instance.Parts.Count > partIndex + 1 ? AppSettings.Instance.Parts[partIndex + 1] : null;
                    foreach (var downtime in part.DownTimes.ToList())
                    {
                        if (downtime.Type == DownTime.Types.CreateNcProgram && downtime.Relation == DownTime.Relations.Machining)
                            part.DownTimes.Remove(downtime);
                    }
                    var partial = Util.SetPartialState(ref part, false);
                    string insertQuery = "INSERT INTO Parts (" +
                        "Guid, " +
                        "Machine, " +
                        "Shift, " +
                        "ShiftDate, " +
                        "Operator, " +
                        "PartName, " +
                        "[Order], " +
                        "Setup, " +
                        "FinishedCount, " +
                        "TotalCount, " +
                        "StartSetupTime, " +
                        "StartMachiningTime, " +
                        "SetupTimeFact, " +
                        "EndMachiningTime, " +
                        "SetupTimePlan, " +
                        "SetupTimePlanForReport, " +
                        "SingleProductionTimePlan, " +
                        "ProductionTimeFact, " +
                        "MachiningTime, " +
                        "SetupDowntimes, " +
                        "MachiningDowntimes, " +
                        "PartialSetupTime, " +
                        "CreateNcProgramTime, " +
                        "MaintenanceTime, " +
                        "ToolSearchingTime, " +
                        "ToolChangingTime, " +
                        "MentoringTime, " +
                        "ContactingDepartmentsTime, " +
                        "FixtureMakingTime, " +
                        "HardwareFailureTime, " +
                        "OperatorComment, " +
                        "DefectiveCount" +
                        ") " +
                        "VALUES (" +
                        "@Guid, " +
                        "@Machine, " +
                        "@Shift, " +
                        "@ShiftDate, " +
                        "@Operator, " +
                        "@PartName, " +
                        "@Order, " +
                        "@Setup, " +
                        "@FinishedCount, " +
                        "@TotalCount, " +
                        "@StartSetupTime, " +
                        "@StartMachiningTime, " +
                        "@SetupTimeFact, " +
                        "@EndMachiningTime, " +
                        "@SetupTimePlan, " +
                        "@SetupTimePlanForReport, " +
                        "@SingleProductionTimePlan, " +
                        "@ProductionTimeFact, " +
                        "@MachiningTime, " +
                        "@SetupDowntimes, " +
                        "@MachiningDowntimes, " +
                        "@PartialSetupTime, " +
                        "@CreateNcProgramTime, " +
                        "@MaintenanceTime, " +
                        "@ToolSearchingTime, " +
                        "@ToolChangingTime, " +
                        "@MentoringTime, " +
                        "@ContactingDepartmentsTime, " +
                        "@FixtureMakingTime, " +
                        "@HardwareFailureTime, " +
                        "@OperatorComment, " +
                        "@DefectiveCount" +
                        "); SELECT SCOPE_IDENTITY();";
                    using (SqlCommand cmd = new(insertQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Guid", part.Guid);
                        cmd.Parameters.AddWithValue("@Machine", AppSettings.Instance.Machine?.Name ?? "");
                        cmd.Parameters.AddWithValue("@Shift", part.Shift);
                        var needDiscrease = part.Shift == Text.NightShift && part.EndMachiningTime < new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day).AddHours(8);
                        var shiftDate = needDiscrease
                            ? new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day).AddDays(-1)
                            : new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day);
                        cmd.Parameters.AddWithValue("@ShiftDate", shiftDate);
                        cmd.Parameters.AddWithValue("@Operator", part.Operator.FullName);
                        cmd.Parameters.AddWithValue("@PartName", part.FullName);
                        cmd.Parameters.AddWithValue("@Order", part.Order);
                        cmd.Parameters.AddWithValue("@Setup", part.Setup);
                        cmd.Parameters.AddWithValue("@FinishedCount", part.FinishedCount + part.DefectiveCount);
                        cmd.Parameters.AddWithValue("@TotalCount", part.TotalCount);
                        cmd.Parameters.AddWithValue("@StartSetupTime", part.StartSetupTime);
                        cmd.Parameters.AddWithValue("@StartMachiningTime", part.StartMachiningTime);
                        cmd.Parameters.AddWithValue("@SetupTimeFact", partial ? 0 : part.SetupTimeFact.TotalMinutes);
                        cmd.Parameters.AddWithValue("@EndMachiningTime", part.EndMachiningTime);
                        cmd.Parameters.AddWithValue("@SetupTimePlan", part.SetupTimePlan);
                        var partSetupTimePlanReport = prevPart != null && prevPart.Order == part.Order && prevPart.Setup == part.Setup ? 0 : part.SetupTimePlan;
                        if (partSetupTimePlanReport == 0 && part.SetupTimeFact.TotalMinutes > 0) partSetupTimePlanReport = part.SetupTimeFact.TotalMinutes;
                        if (partSetupTimePlanReport == 0 && part.SetupTimePlan == 0)
                        {
                            var partialTime = part.DownTimes.Where(x => x.Type == DownTime.Types.PartialSetup).TotalMinutes();
                            if (partialTime > 0) partSetupTimePlanReport = partialTime;
                        }
                        cmd.Parameters.AddWithValue("@SetupTimePlanForReport", partSetupTimePlanReport);
                        cmd.Parameters.AddWithValue("@SingleProductionTimePlan", part.SingleProductionTimePlan);
                        cmd.Parameters.AddWithValue("@ProductionTimeFact", part.ProductionTimeFact.TotalMinutes);
                        cmd.Parameters.AddWithValue("@MachiningTime", part.MachineTime.Ticks);
                        cmd.Parameters.AddWithValue("@SetupDowntimes", Math.Round(part.DownTimes.Where(x => x is { Relation: DownTime.Relations.Setup, Type: not DownTime.Types.PartialSetup }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@MachiningDowntimes", Math.Round(part.DownTimes.Where(x => x is { Relation: DownTime.Relations.Machining }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@PartialSetupTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.PartialSetup }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@CreateNcProgramTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.CreateNcProgram }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@MaintenanceTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.Maintenance }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@ToolSearchingTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ToolSearching }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@ToolChangingTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ToolChanging }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@MentoringTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.Mentoring }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@ContactingDepartmentsTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ContactingDepartments }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@FixtureMakingTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.FixtureMaking }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@HardwareFailureTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.HardwareFailure }).TotalMinutes(), 0));
                        var combinedDownTimes = part.DownTimes.Combine();
                        cmd.Parameters.AddWithValue("@OperatorComment", $"{part.OperatorComments}\n{combinedDownTimes.Report()}".Trim());
                        cmd.Parameters.AddWithValue("@DefectiveCount", part.DefectiveCount);
                        if (AppSettings.Instance.DebugMode) Util.WriteLog("Запись...");
                        var execureResult = await cmd.ExecuteNonQueryAsync();
                        if (!passive)
                        {
                            using (SqlCommand countCmd = new("SELECT COUNT(*) FROM Parts", connection))
                            {
                                part.Id = (int)countCmd.ExecuteScalar();
                            }
                        }

                        var insertToolSearchQuery = "INSERT INTO cnc_tool_search_cases (PartGuid, ToolType, Value, StartTime, EndTime, IsSuccess) " +
                            "VALUES (@PartGuid, @ToolType, @Value, @StartTime, @EndTime, @IsSuccess);";
                        using (SqlCommand insertToolSearchCmd = new(insertToolSearchQuery, connection))
                        {
                            foreach (var d in part.DownTimes.Where(d => d.Type == DownTime.Types.ToolSearching))
                            {
                                insertToolSearchCmd.Parameters.Clear();
                                insertToolSearchCmd.Parameters.AddWithValue("@PartGuid", part.Guid);
                                insertToolSearchCmd.Parameters.AddWithValue("@ToolType", d.ToolType);
                                insertToolSearchCmd.Parameters.AddWithValue("@Value", d.Comment);
                                insertToolSearchCmd.Parameters.AddWithValue("@StartTime", d.StartTime);
                                insertToolSearchCmd.Parameters.AddWithValue("@EndTime", d.EndTime);
                                insertToolSearchCmd.Parameters.AddNullableParameter("@IsSuccess", d.IsSuccess);
                                    
                                await insertToolSearchCmd.ExecuteNonQueryAsync();
                            }
                        }
                        if (AppSettings.Instance.DebugMode) Util.WriteLog($"Записно строк: {execureResult}\n{(passive ? "Оставлен" : "Присвоен")} Id: {part.Id}");
                    }
                    connection.Close();
                    return DbResult.Ok;
                }
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case -1:
                        Util.WriteLog("База данных недоступна.");
                        return DbResult.NoConnection;
                    case 2601 or 2627:
                        Util.WriteLog($"Ошибка №{sqlEx.Number}:\nЗапись в БД уже существует.");
                        return await UpdatePartAsync(part);
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

        /// <summary>
        /// Обновление информации о детали в БД
        /// </summary>
        /// <param name="part">Деталь</param>
        /// <param name="passive">Нужно ли присваивать Id, false используется для одновременной работы с двумя источниками, тогда Id назначается при записи в XL. 
        /// В этом методе используется только для передачи в метод WritePart.</param>
        /// <returns></returns>
        public async static Task<DbResult> UpdatePartAsync(this Part part, bool passive = false)
        {
            if (AppSettings.Instance.DebugMode) Util.WriteLog(part, "Обновление информации об изготовлении в БД.");
            var partIndex = AppSettings.Instance.Parts.IndexOf(part);
            var prevPart = partIndex != -1 && AppSettings.Instance.Parts.Count > partIndex + 1 ? AppSettings.Instance.Parts[partIndex + 1] : null;
            var aaa = part.DownTimes.ToList().RemoveAll(dt => dt.Relation == DownTime.Relations.Machining && dt.Type == DownTime.Types.CreateNcProgram);
            foreach (var downtime in part.DownTimes.ToList())
            {
                if (downtime.Type == DownTime.Types.CreateNcProgram && downtime.Relation == DownTime.Relations.Machining)
                    part.DownTimes.Remove(downtime);
            }
            var partial = Util.SetPartialState(ref part, false);
            try
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync();
                    if (AppSettings.Instance.DebugMode) Util.WriteLog("Соединение к БД открыто.");
                    string updateQuery = "UPDATE Parts SET " +
                        "Machine = @Machine, " +
                        "Shift = @Shift, " +
                        "ShiftDate = @ShiftDate, " +
                        "Operator = @Operator, " +
                        "PartName = @PartName, " +
                        "[Order] = @Order, " +
                        "Setup = @Setup, " +
                        "FinishedCount = @FinishedCount, " +
                        "TotalCount = @TotalCount, " +
                        "StartSetupTime = @StartSetupTime, " +
                        "StartMachiningTime = @StartMachiningTime, " +
                        "EndMachiningTime = @EndMachiningTime, " +
                        "SetupTimeFact = @SetupTimeFact, " +
                        "SetupTimePlan = @SetupTimePlan, " +
                        "SetupTimePlanForReport = @SetupTimePlanForReport, " +
                        "SingleProductionTimePlan = @SingleProductionTimePlan, " +
                        "ProductionTimeFact = @ProductionTimeFact, " +
                        "MachiningTime = @MachiningTime, " +
                        "SetupDowntimes = @SetupDowntimes, " +
                        "MachiningDowntimes = @MachiningDowntimes, " +
                        "PartialSetupTime = @PartialSetupTime, " +
                        "CreateNcProgramTime = @CreateNcProgramTime, " +
                        "MaintenanceTime = @MaintenanceTime, " +
                        "ToolSearchingTime = @ToolSearchingTime, " +
                        "ToolChangingTime = @ToolChangingTime, " +
                        "MentoringTime = @MentoringTime, " +
                        "ContactingDepartmentsTime = @ContactingDepartmentsTime, " +
                        "FixtureMakingTime = @FixtureMakingTime, " +
                        "HardwareFailureTime = @HardwareFailureTime, " +
                        "OperatorComment = @OperatorComment, " +
                        "DefectiveCount = @DefectiveCount " +
                        "WHERE Guid = @Guid";
                    using (SqlCommand cmd = new(updateQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Guid", part.Guid);
                        cmd.Parameters.AddWithValue("@Machine", AppSettings.Instance.Machine?.Name ?? "");
                        cmd.Parameters.AddWithValue("@Shift", part.Shift);
                        var needDiscrease = part.Shift == Text.NightShift && part.EndMachiningTime < new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day).AddHours(9);
                        var shiftDate = needDiscrease
                            ? new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day).AddDays(-1)
                            : new DateTime(part.EndMachiningTime.Year, part.EndMachiningTime.Month, part.EndMachiningTime.Day);
                        cmd.Parameters.AddWithValue("@ShiftDate", shiftDate);
                        cmd.Parameters.AddWithValue("@Operator", part.Operator.FullName);
                        cmd.Parameters.AddWithValue("@PartName", part.FullName);
                        cmd.Parameters.AddWithValue("@Order", part.Order);
                        cmd.Parameters.AddWithValue("@Setup", part.Setup);
                        cmd.Parameters.AddWithValue("@FinishedCount", part.FinishedCount + part.DefectiveCount);
                        cmd.Parameters.AddWithValue("@TotalCount", part.TotalCount);
                        cmd.Parameters.AddWithValue("@StartSetupTime", part.StartSetupTime);
                        cmd.Parameters.AddWithValue("@StartMachiningTime", part.StartMachiningTime);
                        cmd.Parameters.AddWithValue("@SetupTimeFact", partial ? 0 : part.SetupTimeFact.TotalMinutes);
                        cmd.Parameters.AddWithValue("@EndMachiningTime", part.EndMachiningTime);
                        cmd.Parameters.AddWithValue("@SetupTimePlan", part.SetupTimePlan);
                        var partSetupTimePlanReport = prevPart != null && prevPart.Order == part.Order && prevPart.Setup == part.Setup ? 0 : part.SetupTimePlan;
                        if (partSetupTimePlanReport == 0 && part.SetupTimeFact.TotalMinutes > 0) partSetupTimePlanReport = part.SetupTimeFact.TotalMinutes;
                        if (partSetupTimePlanReport == 0 && part.SetupTimePlan == 0)
                        {
                            var partialTime = part.DownTimes.Where(x => x.Type == DownTime.Types.PartialSetup).TotalMinutes();
                            if (partialTime > 0) partSetupTimePlanReport = partialTime;
                        }
                        cmd.Parameters.AddWithValue("@SetupTimePlanForReport", partSetupTimePlanReport);
                        cmd.Parameters.AddWithValue("@SingleProductionTimePlan", part.SingleProductionTimePlan);
                        cmd.Parameters.AddWithValue("@ProductionTimeFact", part.ProductionTimeFact.TotalMinutes);
                        cmd.Parameters.AddWithValue("@MachiningTime", part.MachineTime.Ticks);
                        cmd.Parameters.AddWithValue("@SetupDowntimes", Math.Round(part.DownTimes.Where(x => x is { Relation: DownTime.Relations.Setup, Type: not DownTime.Types.PartialSetup }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@MachiningDowntimes", Math.Round(part.DownTimes.Where(x => x is { Relation: DownTime.Relations.Machining }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@PartialSetupTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.PartialSetup }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@CreateNcProgramTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.CreateNcProgram }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@MaintenanceTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.Maintenance }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@ToolSearchingTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ToolSearching }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@ToolChangingTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ToolChanging }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@MentoringTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.Mentoring }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@ContactingDepartmentsTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.ContactingDepartments }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@FixtureMakingTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.FixtureMaking }).TotalMinutes(), 0));
                        cmd.Parameters.AddWithValue("@HardwareFailureTime", Math.Round(part.DownTimes.Where(x => x is { Type: DownTime.Types.HardwareFailure }).TotalMinutes(), 0));
                        var combinedDownTimes = part.DownTimes.Combine();
                        cmd.Parameters.AddWithValue("@OperatorComment", $"{part.OperatorComments}\n{combinedDownTimes.Report()}".Trim());
                        cmd.Parameters.AddWithValue("@DefectiveCount", part.DefectiveCount);

                        if (AppSettings.Instance.DebugMode) Util.WriteLog("Запись...");
                        var execureResult = await cmd.ExecuteNonQueryAsync();
                        if (AppSettings.Instance.DebugMode) Util.WriteLog($"Изменено строк: {execureResult}");
                        if (execureResult == 0)
                        {
                            Util.WriteLog("Деталь не найдена, добавение новой.");
                            return await WritePartAsync(part, passive);
                        }
                        
                        var deleteToolSearchQuery = "DELETE FROM cnc_tool_search_cases WHERE PartGuid = @PartGuid";
                        using (SqlCommand deleteToolSearchCmd = new(deleteToolSearchQuery, connection))
                        {
                            deleteToolSearchCmd.Parameters.AddWithValue("@PartGuid", part.Guid);
                            await deleteToolSearchCmd.ExecuteNonQueryAsync();
                        }

                        var insertToolSearchQuery = "INSERT INTO cnc_tool_search_cases (PartGuid, ToolType, Value, StartTime, EndTime, IsSuccess) " +
                            "VALUES (@PartGuid, @ToolType, @Value, @StartTime, @EndTime, @IsSuccess);";
                        using (SqlCommand insertToolSearchCmd = new(insertToolSearchQuery, connection))
                        {
                            foreach (var d in part.DownTimes.Where(d => d.Type == DownTime.Types.ToolSearching))
                            {
                                insertToolSearchCmd.Parameters.Clear();
                                insertToolSearchCmd.Parameters.AddWithValue("@PartGuid", part.Guid);
                                insertToolSearchCmd.Parameters.AddWithValue("@ToolType", d.ToolType);
                                insertToolSearchCmd.Parameters.AddWithValue("@Value", d.Comment);
                                insertToolSearchCmd.Parameters.AddWithValue("@StartTime", d.StartTime);
                                insertToolSearchCmd.Parameters.AddWithValue("@EndTime", d.EndTime);
                                insertToolSearchCmd.Parameters.AddNullableParameter("@IsSuccess", d.IsSuccess);
                                await insertToolSearchCmd.ExecuteNonQueryAsync();
                            }
                        }
                    }
                    return DbResult.Ok;
                }
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

        public static async Task<DbResult> SendHardwareFailureMessage(string message)
        {
            if (AppSettings.Instance.DebugMode) Util.WriteLog("Добавление информации об изготовлении в БД.");
            try
            {
                await Task.Run(() =>
                {
                    using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                    {
                        connection.Open();
                        var query = "INSERT INTO maintenance_log (machine, creation_date, rq_status, comments, plandate) VALUES (@Machine, @Date, @Status, @Comment, @PlanDate);";
                        using (SqlCommand cmd = new(query, connection))
                        {
                            cmd.Parameters.AddWithValue("Machine", AppSettings.Instance.Machine?.Name ?? "");
                            cmd.Parameters.AddWithValue("Date", DateTime.Now);
                            cmd.Parameters.AddWithValue("Status", "Открыто");
                            cmd.Parameters.AddWithValue("Comment", message);
                            cmd.Parameters.AddWithValue("PlanDate", DateTime.Today.AddDays(7));
                            var execureResult = cmd.ExecuteNonQuery();
                        }

                    }
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

        /// <summary>
        /// Записывает данные о передаче смены в базу данных. 
        /// Если запись для указанной даты и типа смены существует, она обновляется.
        /// Иначе создается новая запись. Запись ведется либо для передающего (giver), либо для принимающего (receiver).
        /// </summary>
        /// <param name="shiftDate">Дата смены.</param>
        /// <param name="shiftType">Тип смены ("День" или "Ночь").</param>
        /// <param name="giver">True, если передающая сторона (giver), иначе - принимающая (receiver).</param>
        /// <param name="workplaceCleaned">Флаг, указывающий, было ли убрано рабочее место.</param>
        /// <param name="failures">Флаг наличия неисправностей.</param>
        /// <param name="extraneousNoises">Флаг наличия посторонних шумов.</param>
        /// <param name="liquidLeaks">Флаг наличия утечек жидкостей.</param>
        /// <param name="toolBreakage">Флаг поломки инструмента.</param>
        /// <param name="coolantConcentration">Концентрация охлаждающей жидкости.</param>
        /// <returns>Возвращает результат операции записи в базу данных.</returns>
        public static async Task<DbResult> WriteShiftHandover(ShiftHandOverInfo shiftInfo)
        {
            try
            {
                var who = shiftInfo.Giver ? "Giver" : "Reciever";
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync();
                    var query = $@"
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
                    using (SqlCommand cmd = new(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@ShiftDate", shiftInfo.Date);
                        cmd.Parameters.AddWithValue("@ShiftType", shiftInfo.Type);
                        cmd.Parameters.AddWithValue("@Machine", shiftInfo.Machine);
                        cmd.Parameters.AddWithValue("@Master", "");  // Мастер специально пустой, как признак отстутствия отчета
                        cmd.Parameters.AddWithValue("@UnspecifiedDowntimes", 0); // Заполняет мастер в отчете
                        cmd.Parameters.AddWithValue("@DowntimesComment", ""); // Заполняет мастер в отчете
                        cmd.Parameters.AddWithValue("@CommonComment", ""); // Заполняет мастер в отчете
                        cmd.Parameters.AddWithValue("@IsChecked", false); // Заполняет техотдел
                        cmd.Parameters.AddWithValue("@WorkplaceCleaned", shiftInfo.WorkplaceCleaned);
                        cmd.Parameters.AddWithValue("@Failures", shiftInfo.Failures);
                        cmd.Parameters.AddWithValue("@ExtraneousNoises", shiftInfo.ExtraneousNoises);
                        cmd.Parameters.AddWithValue("@LiquidLeaks", shiftInfo.LiquidLeaks);
                        cmd.Parameters.AddWithValue("@ToolBreakage", shiftInfo.ToolBreakage);
                        cmd.Parameters.AddWithValue("@CoolantConcentration", shiftInfo.CoolantConcentration);

                        var execureResult = await cmd.ExecuteNonQueryAsync();
                    }
                }
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
            if (string.IsNullOrWhiteSpace(AppSettings.Instance.ConnectionString)) return (DbResult.Error, toolTypes, "NO CONNECTION STRING");
            try
            {
                
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync();
                    string query = "SELECT SearchToolTypes FROM cnc_elog_config WHERE SearchToolTypes IS NOT NULL;";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (await reader.ReadAsync())
                            {
                                toolTypes.Add(reader.GetString(0));
                            }
                            if (toolTypes.Any())
                            {
                                return (DbResult.Ok, toolTypes, null);
                            }
                            return (DbResult.NotFound, toolTypes, "EMPTY");
                        }
                    }
                }
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

        /// <summary>
        /// Получает лимит наладки для заданного станка, используя строку подключения из настроек приложения.
        /// </summary>
        /// <param name="machine">Имя станка для получения лимита наладки.</param>
        /// <returns>
        /// Кортеж, состоящий из:
        /// - <see cref="DbResult"/>: результат выполнения запроса.
        /// - SetupLimit: лимит наладки для станка (nullable int), может быть null, если данных нет.
        /// - Error: строка с описанием ошибки, если она произошла.
        /// </returns>
        public static (DbResult Result, int? SetupLimit, string Error) GetMachineSetupLimit(this string machine)
            => machine.GetMachineSetupLimit(AppSettings.Instance.ConnectionString);

        /// <summary>
        /// Получает коэффициент наладки для заданного станка, используя строку подключения из настроек приложения.
        /// </summary>
        /// <param name="machine">Имя станка для получения коэффициента наладки.</param>
        /// <returns>
        /// Кортеж, состоящий из:
        /// - <see cref="DbResult"/>: результат выполнения запроса.
        /// - SetupCoefficient: коэффициент наладки для станка (nullable double), может быть null, если данных нет.
        /// - Error: строка с описанием ошибки, если она произошла.
        /// </returns>
        public static (DbResult Result, double? SetupCoefficient, string Error) GetMachineSetupCoefficient(this string machine) 
            => machine.GetMachineSetupCoefficient(AppSettings.Instance.ConnectionString);
    }
}
