using DocumentFormat.OpenXml.Presentation;
using eLog.Models;
using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace eLog.Infrastructure.Extensions
{
    public static class Parts
    {
        public async static Task<string> GetPositionInTasksList(this Part part, IProgress<(int, string)> progress)
        {
            var gs = new GoogleSheet(AppSettings.Instance.GoogleCredentialsPath, AppSettings.Instance.GsId);
            var partPosition = await gs.FindRowByValue(part.Order, AppSettings.Instance.Machine?.Name ?? "", AppSettings.Instance.Machines.Select(m => m.Name), progress);
            if (string.IsNullOrEmpty(partPosition) && part.Order.ToLowerInvariant() == "без м/л") partPosition = 
                    await gs.FindRowByValue(part.FullName, AppSettings.Instance.Machine?.Name ?? "", AppSettings.Instance.Machines.Select(m => m.Name), progress, 1);
            return partPosition;
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

    }
}
