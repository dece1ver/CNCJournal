using eLog.Infrastructure;
using eLog.Infrastructure.Extensions;
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

namespace eLog.Infrastructure
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
