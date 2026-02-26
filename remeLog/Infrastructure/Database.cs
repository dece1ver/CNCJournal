using libeLog.Extensions;
using libeLog.Infrastructure;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using remeLog.Infrastructure.Extensions;
using remeLog.Models;
using remeLog.Models.Reports;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Part = remeLog.Models.Part;

namespace remeLog.Infrastructure
{
    public static class Database
    {
        public static string GetLicenseKey(string licenseName)
        {
            try
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    connection.Open();
                    string query = $"SELECT license_key FROM licensing WHERE license_name = '{licenseName}';";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                return reader.GetString(0);

                            }
                        }
                    }
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return string.Empty;
            }
        }

        public static async Task<int> RemoveNormativeAsync(NormativeEntry normative)
        {
            return await libeLog.Infrastructure.Database.RemoveByIdAsync(AppSettings.Instance.ConnectionString!, normative.Id.ToString(), "cnc_normatives");
        }

        public static List<OperatorInfo> GetOperators()
        {
            List<OperatorInfo> operators = new();
            using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
            {
                connection.Open();
                string query = $"SELECT * FROM cnc_operators ORDER BY LastName ASC;";
                using (SqlCommand command = new(query, connection))
                {
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            operators.Add(new OperatorInfo(
                                reader.GetInt32(0),
                                reader.GetString(1).Trim(),
                                reader.GetString(2).Trim(),
                                reader.GetString(3).Trim(),
                                reader.GetInt32(4),
                                reader.GetBoolean(5)));
                        }
                    }
                }
            }
            return operators;
        }

        public async static Task<List<OperatorInfo>> GetOperatorsAsync(IProgress<string>? progress = null)
        {
            List<OperatorInfo> operators = new();

            await Task.Run(async () =>
            {
                progress?.Report("Подключение к БД...");
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync();
                    string query = $"SELECT * FROM cnc_operators ORDER BY LastName ASC;";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            progress?.Report("Чтение данных из БД...");
                            while (await reader.ReadAsync())
                            {
                                operators.Add(new OperatorInfo(
                                    reader.GetInt32(0),
                                    reader.GetString(1).Trim(),
                                    reader.GetString(2).Trim(),
                                    reader.GetString(3).Trim(),
                                    reader.GetInt32(4),
                                    reader.GetBoolean(5)));
                            }
                        }
                    }
                }
                progress?.Report("Чтение завершено");
            });
            return operators;
        }

        /// <summary>
        /// Сохраняет информацию об операторе в базе данных.
        /// Если оператор с заданным Id существует, выполняется обновление его данных,
        /// если нет - создается новый оператор.
        /// </summary>
        /// <param name="operatorInfo">Объект оператора, содержащий данные для сохранения.</param>
        /// <param name="progress">Прогресс для отслеживания состояния выполнения.</param>
        /// <returns>Асинхронная задача, представляющая операцию сохранения.</returns>
        public static async Task SaveOperatorAsync(OperatorInfo operatorInfo, IProgress<string> progress)
        {

            string query = "IF EXISTS (SELECT 1 FROM cnc_operators WHERE Id = @Id) " +
                           "BEGIN " +
                           "    UPDATE cnc_operators SET FirstName = @FirstName, LastName = @LastName, Patronymic = @Patronymic, Qualification = @Qualification, IsActive = @IsActive WHERE Id = @Id; " +
                           "END " +
                           "ELSE " +
                           "BEGIN " +
                           "    INSERT INTO cnc_operators (FirstName, LastName, Patronymic, Qualification, IsActive) VALUES (@FirstName, @LastName, @Patronymic, @Qualification, @IsActive); " +
                           "END;";
            progress.Report("Подключение к БД...");
            using (var connection = new SqlConnection(AppSettings.Instance.ConnectionString))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", operatorInfo.Id);
                    command.Parameters.AddWithValue("@FirstName", operatorInfo.FirstName);
                    command.Parameters.AddWithValue("@LastName", operatorInfo.LastName);
                    command.Parameters.AddWithValue("@Patronymic", operatorInfo.Patronymic);
                    command.Parameters.AddWithValue("@Qualification", operatorInfo.Qualification);
                    command.Parameters.AddWithValue("@IsActive", operatorInfo.IsActive);

                    progress.Report($"Сохранение оператора '{operatorInfo.DisplayName}' в БД...");
                    await command.ExecuteNonQueryAsync();
                    progress.Report($"Оператор '{operatorInfo.DisplayName}' успешно сохранен.");
                }
            }
        }

        /// <summary>
        /// Сохраняет список операторов в базе данных.
        /// Для каждого оператора в списке вызывается метод SaveOperatorAsync,
        /// который проверяет существование оператора и выполняет соответствующее действие.
        /// </summary>
        /// <param name="operators">Список операторов для сохранения.</param>
        /// <param name="progress">Прогресс для отслеживания состояния выполнения.</param>
        /// <returns>Асинхронная задача, представляющая операцию сохранения.</returns>
        public static async Task SaveOperatorsAsync(IEnumerable<OperatorInfo> operators, IProgress<string> progress)
        {
            progress.Report("Сохранение операторов в БД");
            foreach (var operatorInfo in operators)
            {
                await SaveOperatorAsync(operatorInfo, progress);
            }
            progress.Report("Сохранение операторов в БД выполнено");
        }

        /// <summary>
        /// Удаляет оператора из базы данных по указанному идентификатору.
        /// Если оператор с данным Id существует, он будет удален.
        /// </summary>
        /// <param name="operatorId">Уникальный идентификатор оператора, которого необходимо удалить.</param>
        /// <param name="progress">Прогресс для отслеживания состояния выполнения.</param>
        /// <returns>Асинхронная задача, представляющая операцию удаления.</returns>
        public static async Task DeleteOperatorAsync(int operatorId, IProgress<string> progress)
        {
            string query = "DELETE FROM cnc_operators WHERE Id = @Id;";

            using (var connection = new SqlConnection(AppSettings.Instance.ConnectionString))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", operatorId);

                    progress.Report("Удаление оператора из БД...");
                    int rowsAffected = await command.ExecuteNonQueryAsync();

                    if (rowsAffected > 0)
                    {
                        progress.Report("Оператор успешно удален.");
                    }
                    else
                    {
                        progress.Report("Оператор не найден.");
                    }
                }
            }
        }

        public async static Task<IEnumerable<Qualification>> GetQualificationsAsync(IProgress<string>? progress = null)
        {
            List<Qualification> qualifications = new();

            await Task.Run(async () =>
            {
                progress?.Report("Подключение к БД...");
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync();
                    string query = $"SELECT [Qualification]," +
                    $"[EfficiencyValueHH],[EfficiencyCoefficientHH]," +
                    $"[EfficiencyValueH]," +
                    $"[EfficiencyCoefficientH]," +
                    $"[EfficiencyValueN]," +
                    $"[EfficiencyCoefficientN]," +
                    $"[EfficiencyValueL]," +
                    $"[EfficiencyCoefficientL]," +
                    $"[EfficiencyValueLL]," +
                    $"[EfficiencyCoefficientLL]," +
                    $"[EfficiencyValueLLL]," +
                    $"[EfficiencyCoefficientLLL]," +
                    $"[DownTimesValueHH]," +
                    $"[DownTimesCoefficientHH]," +
                    $"[DownTimesValueH]," +
                    $"[DownTimesCoefficientH]," +
                    $"[DownTimesValueN]," +
                    $"[DownTimesCoefficientN]," +
                    $"[DownTimesValueL]," +
                    $"[DownTimesCoefficientL]," +
                    $"[DownTimesValueLL]," +
                    $"[DownTimesCoefficientLL]," +
                    $"[DownTimesValueLLL]," +
                    $"[DownTimesCoefficientLLL]," +
                    $"[NonSerialEfficiencyValueHH]," +
                    $"[NonSerialEfficiencyCoefficientHH]," +
                    $"[NonSerialEfficiencyValueH]," +
                    $"[NonSerialEfficiencyCoefficientH]," +
                    $"[NonSerialEfficiencyValueN]," +
                    $"[NonSerialEfficiencyCoefficientN]," +
                    $"[NonSerialEfficiencyValueL] ," +
                    $"[NonSerialEfficiencyCoefficientL]," +
                    $"[NonSerialEfficiencyValueLL]," +
                    $"[NonSerialEfficiencyCoefficientLL]," +
                    $"[NonSerialEfficiencyValueLLL]," +
                    $"[NonSerialEfficiencyCoefficientLLL] FROM cnc_qualifications;";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            progress?.Report("Чтение данных из БД...");
                            while (await reader.ReadAsync())
                            {
                                qualifications.Add(new Qualification(
                                    reader.GetInt32(0),
                                    reader.GetDouble(1),
                                    reader.GetDouble(2),
                                    reader.GetDouble(3),
                                    reader.GetDouble(4),
                                    reader.GetDouble(5),
                                    reader.GetDouble(6),
                                    reader.GetDouble(7),
                                    reader.GetDouble(8),
                                    reader.GetDouble(9),
                                    reader.GetDouble(10),
                                    reader.GetDouble(11),
                                    reader.GetDouble(12),
                                    reader.GetDouble(13),
                                    reader.GetDouble(14),
                                    reader.GetDouble(15),
                                    reader.GetDouble(16),
                                    reader.GetDouble(17),
                                    reader.GetDouble(18),
                                    reader.GetDouble(19),
                                    reader.GetDouble(20),
                                    reader.GetDouble(21),
                                    reader.GetDouble(22),
                                    reader.GetDouble(23),
                                    reader.GetDouble(24),
                                    reader.GetDouble(25),
                                    reader.GetDouble(26),
                                    reader.GetDouble(27),
                                    reader.GetDouble(28),
                                    reader.GetDouble(29),
                                    reader.GetDouble(30),
                                    reader.GetDouble(31),
                                    reader.GetDouble(32),
                                    reader.GetDouble(33),
                                    reader.GetDouble(34),
                                    reader.GetDouble(35),
                                    reader.GetDouble(36)));
                            }
                        }
                    }
                }
                progress?.Report("Чтение завершено");
            });
            return qualifications;
        }

        public async static Task<List<Machine>> GetMachinesAsync(IProgress<string> progress)
        {
            List<Machine> machines = new();
            await Task.Run(async () =>
            {
                progress.Report("Подключение к БД...");
                using SqlConnection connection = new(AppSettings.Instance.ConnectionString);
                await connection.OpenAsync();

                const string query = @"
            SELECT
                Id, Name, IsActive, IsSerial, Type, SetupLimit, SetupCoefficient,
                WnId, WnUuid,
                WnCounterSignal, WnNcProgramNameSignal, WnNcPartNameSignal,
                WnCurrentCSSignal, WnCurrentPlaneSignal,
                WnCurrentBlockNumberSignal, WnCurrentBlockTextSignal, WnNcModeSignal,
                WnFeedHoldSignal, WnSBKSignal, WnDryRunSignal, WnMSTLKSignal, WnMLKSignal,
                WnProgramRunningSignal, WnStopSignal, WnOpStopSignal,
                WnMcodeSignal, WnGMoveSignal, WnGCycleSignal, WnGcodeSignal,
                WnRapidMultiplierSignal, WnFeedMultiplierSignal,
                WnSpindleSpeed1MultiplierSignal, WnSpindleSpeed2MultiplierSignal,
                WnActSpindle1SpeedSignal, WnActSpindle2SpeedSignal, WnActCutSpeedSignal,
                WnActFeedPerMinSignal, WnActFeedPerRevSignal,
                WnAbsXSignal, WnAbsYSignal, WnAbsZSignal, WnAbsZASignal, WnAbsBSignal, WnAbsCSignal, WnAbsWSignal,
                WnRelXSignal, WnRelYSignal, WnRelZSignal, WnRelZASignal, WnRelBSignal, WnRelCSignal, WnRelWSignal,
                WnMachXSignal, WnMachYSignal, WnMachZSignal, WnMachZASignal, WnMachBSignal, WnMachCSignal, WnMachWSignal,
                WnToolSignal, WnToolHSignal, WnToolDSignal, WnToolVectorSignal,
                WnToolGeomRSignal, WnToolGeomXSignal, WnToolGeomYSignal, WnToolGeomZSignal,
                WnToolWearRSignal, WnToolWearXSignal, WnToolWearYSignal, WnToolWearZSignal,
                WnAlarmMessageSignal,
                WnLoadXSignal, WnLoadYSignal, WnLoadZSignal, WnLoadCSignal, WnLoadBSignal, WnLoadWSignal, WnLoadSpindle1Signal, WnLoadSpindle2Signal
            FROM cnc_machines
            WHERE IsActive = 1;";

                using SqlCommand command = new(query, connection);
                using SqlDataReader reader = await command.ExecuteReaderAsync();

                progress.Report("Чтение данных из БД...");
                var ct = CancellationToken.None;

                while (await reader.ReadAsync())
                {
                    int i = 0;
                    machines.Add(new Machine
                    {
                        // Основные
                        Id = await reader.GetValueOrDefaultAsync(i++, 0, ct),
                        Name = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        IsActive = await reader.GetValueOrDefaultAsync(i++, false, ct),
                        IsSerial = await reader.GetValueOrDefaultAsync(i++, false, ct),
                        Type = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        SetupLimit = await reader.GetValueOrDefaultAsync(i++, 0, ct),
                        SetupCoefficient = await reader.GetValueOrDefaultAsync(i++, 0.0, ct),
                        // Winnum
                        WnId = await reader.GetValueOrDefaultAsync(i++, 0, ct),
                        WnUuid = await reader.GetValueOrDefaultAsync(i++, Guid.Empty, ct),
                        // Общие
                        WnCounterSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnNcProgramNameSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnNcPartNameSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnCurrentCSSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnCurrentPlaneSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnCurrentBlockNumberSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnCurrentBlockTextSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnNcModeSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnFeedHoldSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnSBKSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnDryRunSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnMSTLKSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnMLKSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnProgramRunningSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnStopSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnOpStopSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnMcodeSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnGMoveSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnGCycleSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnGcodeSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        // Коррекции
                        WnRapidMultiplierSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnFeedMultiplierSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnSpindleSpeed1MultiplierSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnSpindleSpeed2MultiplierSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        // Актуальные значения
                        WnActSpindle1SpeedSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnActSpindle2SpeedSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnActCutSpeedSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnActFeedPerMinSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnActFeedPerRevSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        // Абсолютные координаты
                        WnAbsXSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnAbsYSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnAbsZSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnAbsZASignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnAbsBSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnAbsCSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnAbsWSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        // Относительные координаты
                        WnRelXSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnRelYSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnRelZSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnRelZASignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnRelBSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnRelCSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnRelWSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        // Машинные координаты
                        WnMachXSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnMachYSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnMachZSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnMachZASignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnMachBSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnMachCSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnMachWSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        // Инструмент
                        WnToolSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnToolHSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnToolDSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnToolVectorSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnToolGeomRSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnToolGeomXSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnToolGeomYSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnToolGeomZSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnToolWearRSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnToolWearXSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnToolWearYSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnToolWearZSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        // Аварии и нагрузки
                        WnAlarmMessageSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnLoadXSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnLoadYSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnLoadZSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnLoadCSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnLoadBSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnLoadWSignal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnLoadSpindle1Signal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                        WnLoadSpindle2Signal = await reader.GetValueOrDefaultAsync(i++, string.Empty, ct),
                    });
                }
                progress.Report("Чтение завершено");
            });
            return machines;
        }

        public async static Task<bool> GetMachineSerialStatus(string machine, IProgress<string>? progress = null)
        {
            progress?.Report("Подключение к БД...");
            using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
            {
                await connection.OpenAsync();
                string query = $"SELECT IsSerial FROM cnc_machines WHERE Name = @Machine;";
                using (SqlCommand command = new(query, connection))
                {
                    command.Parameters.AddWithValue("@Machine", machine);
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        progress?.Report("Чтение данных из БД...");
                        while (await reader.ReadAsync())
                        {
                            return reader.GetBoolean(0);
                        }
                    }
                }
            }
            progress?.Report("Чтение завершено");
            return false;
        }

        public async static Task<List<DateTime>> GetHolidaysAsync(IProgress<string>? progress)
        {
            List<DateTime> holidays = new();

            await Task.Run(async () =>
            {
                progress?.Report("Подключение к БД...");
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync();
                    string query = $"SELECT Holidays FROM cnc_remelog_config;";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            progress?.Report("Чтение данных из БД...");
                            while (await reader.ReadAsync())
                            {
                                holidays.Add(reader.GetDateTime(0));
                            }
                        }
                    }
                }
                progress?.Report("Чтение завершено");
            });
            return holidays;
        }

        /// <summary>
        /// Сохраняет информацию о серийной детали в базе данных.
        /// Если деталь с заданным Id существует, выполняется обновление её имени,
        /// если Id равен 0 — создается новая запись.
        /// </summary>
        /// <param name="part">Объект серийной детали для сохранения.</param>
        /// <param name="progress">Прогресс для отслеживания состояния выполнения.</param>
        /// <returns>Асинхронная задача, представляющая операцию сохранения.</returns>
        public static async Task SaveSerialPartAsync(SerialPart part, IProgress<string>? progress = null)
        {
            string query = part.Id == 0
                ? "IF NOT EXISTS (SELECT 1 FROM cnc_serial_parts WHERE PartName = @PartName) " +
                  "BEGIN INSERT INTO cnc_serial_parts (PartName, YearCount) VALUES (@PartName, @YearCount); END"
                : "UPDATE cnc_serial_parts SET PartName = @PartName, YearCount = @YearCount WHERE Id = @Id;";

            using var connection = new SqlConnection(AppSettings.Instance.ConnectionString);
            await connection.OpenAsync();

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@PartName", part.PartName);
            command.Parameters.AddWithValue("@YearCount", part.YearCount);
            if (part.Id != 0)
                command.Parameters.AddWithValue("@Id", part.Id);

            progress?.Report($"Сохранение детали '{part.PartName}'...");
            await command.ExecuteNonQueryAsync();
            progress?.Report($"Деталь '{part.PartName}' успешно сохранена.");
        }


        /// <summary>
        /// Сохраняет список серийных деталей в базе данных.
        /// Для каждой детали вызывается метод SaveSerialPartAsync,
        /// который проверяет наличие Id и выполняет добавление или обновление.
        /// </summary>
        /// <param name="partNames">Список серийных деталей для сохранения.</param>
        /// <param name="progress">Прогресс для отслеживания состояния выполнения.</param>
        /// <returns>Асинхронная задача, представляющая операцию сохранения.</returns>
        public static async Task SaveSerialPartsAsync(IEnumerable<SerialPart> partNames, IProgress<string>? progress = null)
        {
            progress?.Report("Сохранение серийных деталей в БД...");
            foreach (var part in partNames)
            {
                await SaveSerialPartAsync(part, progress);
            }
            progress?.Report("Сохранение серийных деталей завершено.");
        }


        /// <summary>
        /// Удаляет серийную деталь из базы данных по указанному идентификатору.
        /// Если деталь с данным Id существует, она будет удалена.
        /// </summary>
        /// <param name="partId">Уникальный идентификатор детали, которую необходимо удалить.</param>
        /// <param name="progress">Прогресс для отслеживания состояния выполнения.</param>
        /// <returns>Асинхронная задача, представляющая операцию удаления.</returns>
        public static async Task DeleteSerialPartAsync(int partId, IProgress<string>? progress = null)
        {
            const string query = "DELETE FROM cnc_serial_parts WHERE Id = @Id;";

            using var connection = new SqlConnection(AppSettings.Instance.ConnectionString);
            await connection.OpenAsync();

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", partId);

            progress?.Report($"Удаление детали с Id = {partId} из БД...");
            int rowsAffected = await command.ExecuteNonQueryAsync();

            if (rowsAffected > 0)
            {
                progress?.Report("Деталь успешно удалена.");
            }
            else
            {
                progress?.Report("Деталь с указанным Id не найдена.");
            }
        }


        /// <summary>
        /// Асинхронно извлекает данные о случаях поиска инструмента, разделяя запросы на подсписки по 2000 элементов.
        /// </summary>
        /// <param name="guids">Список GUID'ов для фильтрации данных по таблице parts.</param>
        /// <param name="progress">Прогресс-объект для обновления статуса извлечения данных.</param>
        /// <returns>Список объектов <see cref="ToolSearchCase"/>, полученных из базы данных.</returns>
        /// <exception cref="SqlException">Возникает, если происходит ошибка при выполнении SQL-запроса.</exception>
        public async static Task<List<ToolSearchCase>> GetToolSearchCasesAsync(List<Guid> guids, IProgress<string> progress)
        {
            var cases = new List<ToolSearchCase>();

            if (guids == null || guids.Count == 0)
                return cases;

            // больше 2100 нельзя
            var chunkSize = 2000;

            var guidsChunks = guids
                .Select((guid, index) => new { guid, index })
                .GroupBy(x => x.index / chunkSize)
                .Select(g => g.Select(x => x.guid).ToList())
                .ToList();

            await using var connection = new SqlConnection(AppSettings.Instance.ConnectionString);
            await connection.OpenAsync();

            for (int currentChunk = 0; currentChunk < guidsChunks.Count; currentChunk++)
            {


                var chunk = guidsChunks[currentChunk];
                var parameters = chunk.Select((guid, index) => $"@p{index}").ToArray();
                var query = $@"
                    SELECT p.Operator, p.PartName, p.Machine, c.ToolType, c.Value, c.StartTime, c.EndTime, c.IsSuccess
                    FROM cnc_tool_search_cases c
                    INNER JOIN parts p ON c.PartGuid = p.Guid
                    WHERE p.Guid IN ({string.Join(", ", parameters)});
                ";

                await using var command = new SqlCommand(query, connection);

                for (int i = 0; i < chunk.Count; i++)
                {
                    command.Parameters.AddWithValue(parameters[i], chunk[i]);
                }

                await using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    string operatorName = reader.GetStringOrEmpty(0);
                    string partName = reader.GetStringOrEmpty(1);
                    string machine = reader.GetStringOrEmpty(2);
                    string toolType = reader.GetStringOrEmpty(3);
                    string value = reader.GetStringOrEmpty(4);
                    DateTime startTime = reader.GetDateTimeOrMinValue(5);
                    DateTime endTime = reader.GetDateTimeOrMinValue(6);
                    bool? isSuccess = reader.GetNullableBoolean(7);

                    cases.Add(new ToolSearchCase(
                        operatorName,
                        partName,
                        machine,
                        toolType,
                        value,
                        startTime,
                        endTime,
                        isSuccess
                    ));
                }

                if (guids.Count > chunkSize)
                {
                    progress.Report($"Получение данных из БД {((currentChunk + 1) * 100) / guidsChunks.Count}%");
                }
            }

            return cases;
        }

        public async static Task<List<Part>> ReadPartsWithConditions(string conditions, CancellationToken cancellationToken)
        {
            List<Part> parts = new();
            await Task.Run(async () =>
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync(cancellationToken);
                    string query = $"SELECT * FROM Parts WHERE {conditions} ORDER BY StartSetupTime ASC;";
                    using (SqlCommand command = new(query, connection))
                    {
                        await FillPartsAsync(parts, command, cancellationToken);
                    }
                }
            }, cancellationToken);
            return parts;
        }

        public async static Task<ObservableCollection<Part>> ReadPartsByShiftDateAndMachine(DateTime fromDate, DateTime toDate, string machine, CancellationToken cancellationToken)
        {
            ObservableCollection<Part> parts = new();
            using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
            {
                connection.Open();

                string query = "SELECT * FROM Parts WHERE ShiftDate BETWEEN @FromDate AND @ToDate AND Machine = @Machine ORDER BY StartSetupTime ASC;";
                using (SqlCommand command = new(query, connection))
                {
                    command.Parameters.AddWithValue("@FromDate", fromDate);
                    command.Parameters.AddWithValue("@ToDate", toDate);
                    command.Parameters.AddWithValue("@Machine", machine);

                    await parts.FillPartsAsync(command, cancellationToken);
                }
            }
            return parts;
        }

        public async static Task<ObservableCollection<Part>> ReadPartsByShiftDate(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
        {
            ObservableCollection<Part> parts = new();
            using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
            {
                connection.Open();

                string query = "SELECT * FROM Parts WHERE ShiftDate BETWEEN @FromDate AND @ToDate ORDER BY StartSetupTime ASC;";
                using (SqlCommand command = new(query, connection))
                {
                    command.Parameters.AddWithValue("@FromDate", fromDate);
                    command.Parameters.AddWithValue("@ToDate", toDate);

                    await parts.FillPartsAsync(command, cancellationToken);
                }
            }
            return parts;
        }
        /// <summary>
        /// Асинхронно извлекает данные о деталях, разделяя запросы на подсписки по 2000 элементов.
        /// </summary>
        /// <param name="guids">Список GUID'ов для фильтрации данных по таблице Parts.</param>
        /// <param name="cancellationToken">Токен отмены для прерывания операции.</param>
        /// <returns>Коллекция объектов <see cref="Part"/>, полученных из базы данных.</returns>
        public async static Task<ObservableCollection<Part>> ReadPartsByGuids(IEnumerable<Guid> guids, CancellationToken cancellationToken)
        {
            ObservableCollection<Part> parts = new();

            if (guids == null || !guids.Any())
                return parts;

            const int chunkSize = 2000;

            var chunks = guids
                .Select((guid, index) => new { guid, index })
                .GroupBy(x => x.index / chunkSize)
                .Select(g => g.Select(x => x.guid).ToList());

            using var connection = new SqlConnection(AppSettings.Instance.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            foreach (var chunk in chunks)
            {
                var parameters = chunk.Select((_, index) => $"@p{index}").ToArray();
                var query = $"SELECT * FROM Parts WHERE Guid IN ({string.Join(", ", parameters)}) ORDER BY StartSetupTime ASC;";

                using var command = new SqlCommand(query, connection);

                for (int i = 0; i < chunk.Count; i++)
                {
                    command.Parameters.AddWithValue(parameters[i], chunk[i]);
                }

                await parts.FillPartsAsync(command, cancellationToken);
            }

            return parts;
        }


        public async static Task<ObservableCollection<Part>> ReadPartsByPartNameAndOrder(string[] partNames, string[] orders, CancellationToken cancellationToken)
        {
            ObservableCollection<Part> parts = new();
            using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
            {
                connection.Open();

                string query = "SELECT * FROM Parts WHERE PartName IN ('" + string.Join("','", partNames) + "') AND [Order] IN ('" + string.Join("','", orders) + "')";
                using (SqlCommand command = new(query, connection))
                {
                    await parts.FillPartsAsync(command, cancellationToken);
                }
            }
            return parts;
        }


        public async static Task<(DbResult dbResult, string message)> UpdatePartAsync(this Part part)
        {
            try
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync();
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
                        "SpecialDowntimeTime = @SpecialDowntimeTime, " +
                        "OperatorComment = @OperatorComment, " +
                        "MasterSetupComment = @MasterSetupComment, " +
                        "MasterMachiningComment = @MasterMachiningComment, " +
                        "SpecifiedDowntimesComment = @SpecifiedDowntimesComment, " +
                        "UnspecifiedDowntimeComment = @UnspecifiedDowntimeComment, " +
                        "MasterComment = @MasterComment, " +
                        "FixedSetupTimePlan = @FixedSetupTimePlan, " +
                        "FixedProductionTimePlan = @FixedProductionTimePlan, " +
                        "EngineerComment = @EngineerComment, " +
                        "ExcludeFromReports = @ExcludeFromReports, " +
                        "LongSetupReasonComment = @LongSetupReasonComment, " +
                        "LongSetupFixComment = @LongSetupFixComment, " +
                        "LongSetupEngeneerComment = @LongSetupEngeneerComment, " +
                        "ExcludedOperationsTime = @ExcludedOperationsTime, " +
                        "IncreaseReason = @IncreaseReason, " +
                        "DefectiveCount = @DefectiveCount " +
                        "WHERE Guid = @Guid";
                    using (SqlCommand cmd = new(updateQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Guid", part.Guid);
                        cmd.Parameters.AddWithValue("@Machine", part.Machine);
                        cmd.Parameters.AddWithValue("@Shift", part.Shift);
                        cmd.Parameters.AddWithValue("@ShiftDate", part.ShiftDate);
                        cmd.Parameters.AddWithValue("@Operator", part.Operator);
                        cmd.Parameters.AddWithValue("@PartName", part.PartName);
                        cmd.Parameters.AddWithValue("@Order", part.Order);
                        cmd.Parameters.AddWithValue("@Setup", part.Setup);
                        cmd.Parameters.AddWithValue("@FinishedCount", part.FinishedCount);
                        cmd.Parameters.AddWithValue("@TotalCount", part.TotalCount);
                        cmd.Parameters.AddWithValue("@StartSetupTime", part.StartSetupTime);
                        cmd.Parameters.AddWithValue("@StartMachiningTime", part.StartMachiningTime);
                        cmd.Parameters.AddWithValue("@SetupTimeFact", part.SetupTimeFact);
                        cmd.Parameters.AddWithValue("@EndMachiningTime", part.EndMachiningTime);
                        cmd.Parameters.AddWithValue("@SetupTimePlan", part.SetupTimePlan);
                        cmd.Parameters.AddWithValue("@SetupTimePlanForReport", part.SetupTimePlanForReport);
                        cmd.Parameters.AddWithValue("@SingleProductionTimePlan", part.SingleProductionTimePlan);
                        cmd.Parameters.AddWithValue("@ProductionTimeFact", part.ProductionTimeFact);
                        cmd.Parameters.AddWithValue("@MachiningTime", part.MachiningTime.Ticks);
                        cmd.Parameters.AddWithValue("@SetupDowntimes", part.SetupDowntimes);
                        cmd.Parameters.AddWithValue("@MachiningDowntimes", part.MachiningDowntimes);
                        cmd.Parameters.AddWithValue("@PartialSetupTime", part.PartialSetupTime);
                        cmd.Parameters.AddWithValue("@CreateNcProgramTime", part.CreateNcProgramTime);
                        cmd.Parameters.AddWithValue("@MaintenanceTime", part.MaintenanceTime);
                        cmd.Parameters.AddWithValue("@ToolSearchingTime", part.ToolSearchingTime);
                        cmd.Parameters.AddWithValue("@ToolChangingTime", part.ToolChangingTime);
                        cmd.Parameters.AddWithValue("@MentoringTime", part.MentoringTime);
                        cmd.Parameters.AddWithValue("@ContactingDepartmentsTime", part.ContactingDepartmentsTime);
                        cmd.Parameters.AddWithValue("@FixtureMakingTime", part.FixtureMakingTime);
                        cmd.Parameters.AddWithValue("@HardwareFailureTime", part.HardwareFailureTime);
                        cmd.Parameters.AddWithValue("@SpecialDowntimeTime", part.SpecialDowntimeTime);
                        cmd.Parameters.AddWithValue("@OperatorComment", part.OperatorComment);
                        cmd.Parameters.AddWithValue("@MasterSetupComment", part.MasterSetupComment);
                        cmd.Parameters.AddWithValue("@MasterMachiningComment", part.MasterMachiningComment);
                        cmd.Parameters.AddWithValue("@SpecifiedDowntimesComment", part.SpecifiedDowntimesComment);
                        cmd.Parameters.AddWithValue("@UnspecifiedDowntimeComment", part.UnspecifiedDowntimesComment);
                        cmd.Parameters.AddWithValue("@MasterComment", part.MasterComment);
                        cmd.Parameters.AddWithValue("@FixedSetupTimePlan", part.FixedSetupTimePlan);
                        cmd.Parameters.AddWithValue("@FixedProductionTimePlan", part.FixedProductionTimePlan);
                        cmd.Parameters.AddWithValue("@EngineerComment", part.EngineerComment);
                        cmd.Parameters.AddWithValue("@ExcludeFromReports", part.ExcludeFromReports);
                        cmd.Parameters.AddWithValue("@LongSetupReasonComment", part.LongSetupReasonComment);
                        cmd.Parameters.AddWithValue("@LongSetupFixComment", part.LongSetupFixComment);
                        cmd.Parameters.AddWithValue("@LongSetupEngeneerComment", part.LongSetupEngeneerComment);
                        cmd.Parameters.AddWithValue("@ExcludedOperationsTime", part.ExcludedOperationsTime);
                        cmd.Parameters.AddWithValue("@IncreaseReason", part.IncreaseReason);
                        cmd.Parameters.AddWithValue("@DefectiveCount", part.DefectiveCount);

                        var execureResult = await cmd.ExecuteNonQueryAsync();
                    }
                    await connection.CloseAsync();
                    return (DbResult.Ok, "ОК");
                }
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        var authMessage = $"Ошибка №{sqlEx.Number}:\nОшибка авторизации.";
                        Util.WriteLog(sqlEx, authMessage);
                        return (DbResult.AuthError, authMessage);
                    default:
                        var sqlExMessage = $"Ошибка №{sqlEx.Number}:";
                        Util.WriteLog(sqlEx, sqlExMessage);
                        return (DbResult.Error, sqlExMessage);
                }
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return (DbResult.Error, ex.Message);
            }
        }

        static async Task FillPartsAsync(this ICollection<Part> parts, SqlCommand command, CancellationToken cancellationToken)
        {
            using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var guid = await reader.GetFieldValueAsync<Guid>(0, cancellationToken);
                    var machine = await reader.GetFieldValueAsync<string>(1, cancellationToken);
                    var shift = await reader.GetFieldValueAsync<string>(2, cancellationToken);
                    var shiftDate = await reader.GetFieldValueAsync<DateTime>(3, cancellationToken);
                    var @operator = await reader.GetFieldValueAsync<string>(4, cancellationToken);
                    var partName = await reader.GetFieldValueAsync<string>(5, cancellationToken);
                    var order = await reader.GetFieldValueAsync<string>(6, cancellationToken);
                    var setup = await reader.GetFieldValueAsync<int>(7, cancellationToken);
                    var finishedCount = await reader.GetFieldValueAsync<double>(8, cancellationToken);
                    var totalCount = await reader.GetFieldValueAsync<int>(9, cancellationToken);
                    var startSetupTime = await reader.GetFieldValueAsync<DateTime>(10, cancellationToken);
                    var startMachiningTime = await reader.GetFieldValueAsync<DateTime>(11, cancellationToken);
                    var setupTimeFact = await reader.GetFieldValueAsync<double>(12, cancellationToken);
                    var endMachiningTime = await reader.GetFieldValueAsync<DateTime>(13, cancellationToken);
                    var setupTimePlan = await reader.GetFieldValueAsync<double>(14, cancellationToken);
                    var setupTimePlanForReport = await reader.GetFieldValueAsync<double>(15, cancellationToken);
                    var singleProductionTimePlan = await reader.GetFieldValueAsync<double>(16, cancellationToken);
                    var productionTimeFact = await reader.GetFieldValueAsync<double>(17, cancellationToken);
                    var machiningTime = await reader.GetFieldValueAsync<long>(18, cancellationToken);
                    var setupDowntimes = await reader.GetFieldValueAsync<double>(19, cancellationToken);
                    var machiningDowntimes = await reader.GetFieldValueAsync<double>(20, cancellationToken);
                    var partialSetupTime = await reader.GetFieldValueAsync<double>(21, cancellationToken);
                    var createNcProgramTime = await reader.GetFieldValueAsync<double>(22, cancellationToken);
                    var maintenanceTime = await reader.GetFieldValueAsync<double>(23, cancellationToken);
                    var toolSearchingTime = await reader.GetFieldValueAsync<double>(24, cancellationToken);
                    var toolChangingTime = await reader.GetFieldValueAsync<double>(25, cancellationToken);
                    var mentoringTime = await reader.GetFieldValueAsync<double>(26, cancellationToken);
                    var contactiongDepartmentsTime = await reader.GetFieldValueAsync<double>(27, cancellationToken);
                    var fixtureMakingTime = await reader.GetFieldValueAsync<double>(28, cancellationToken);
                    var hardwareFailureTime = await reader.GetFieldValueAsync<double>(29, cancellationToken);
                    var operatorComment = await reader.GetFieldValueAsync<string>(30, cancellationToken);
                    var masterSetupComment = await reader.GetValueOrDefaultAsync(31, "", cancellationToken);
                    var masterMachiningComment = await reader.GetValueOrDefaultAsync(32, "", cancellationToken);
                    var specifiedDowntimesComment = await reader.GetValueOrDefaultAsync(33, "", cancellationToken);
                    var unspecifiedDowntimesComment = await reader.GetValueOrDefaultAsync(34, "", cancellationToken);
                    var masterComment = await reader.GetValueOrDefaultAsync(35, "", cancellationToken);
                    var fixedSetupTimePlan = await reader.GetValueOrDefaultAsync(36, 0.0, cancellationToken);
                    var fixedMachineTimePlan = await reader.GetValueOrDefaultAsync(37, 0.0, cancellationToken);
                    var engineerComment = await reader.GetValueOrDefaultAsync(38, "", cancellationToken);
                    var excludeFromReports = await reader.GetValueOrDefaultAsync(39, false, cancellationToken);
                    var longSetupReasonComment = await reader.GetValueOrDefaultAsync(40, "", cancellationToken);
                    var longSetupFixComment = await reader.GetValueOrDefaultAsync(41, "", cancellationToken);
                    var longSetupEngeneerComment = await reader.GetValueOrDefaultAsync(42, "", cancellationToken);
                    var excludedOperationsTime = await reader.GetValueOrDefaultAsync(43, 0.0, cancellationToken);
                    var increaseReason = await reader.GetValueOrDefaultAsync(44, "", cancellationToken);

                    var defectiveCount = await reader.GetValueOrDefaultAsync(46, 0, cancellationToken);
                    var specialDowntime = await reader.GetValueOrDefaultAsync(47, 0.0, cancellationToken);

                    Part part = new(
                        guid,
                        machine,
                        shift,
                        shiftDate,
                        @operator,
                        partName,
                        order,
                        setup,
                        finishedCount,
                        defectiveCount,
                        totalCount,
                        startSetupTime,
                        startMachiningTime,
                        setupTimeFact,
                        endMachiningTime,
                        setupTimePlan,
                        setupTimePlanForReport,
                        singleProductionTimePlan,
                        productionTimeFact,
                        TimeSpan.FromTicks(machiningTime),
                        setupDowntimes,
                        machiningDowntimes,
                        partialSetupTime,
                        createNcProgramTime,
                        maintenanceTime,
                        toolSearchingTime,
                        toolChangingTime,
                        mentoringTime,
                        contactiongDepartmentsTime,
                        fixtureMakingTime,
                        hardwareFailureTime,
                        specialDowntime,
                        operatorComment,
                        masterSetupComment,
                        masterMachiningComment,
                        specifiedDowntimesComment,
                        unspecifiedDowntimesComment,
                        masterComment,
                        fixedSetupTimePlan,
                        fixedMachineTimePlan,
                        engineerComment,
                        excludeFromReports,
                        longSetupReasonComment,
                        longSetupFixComment,
                        longSetupEngeneerComment,
                        excludedOperationsTime,
                        increaseReason);
                    parts.Add(part);
                }
            }
        }

        public static DbResult ReadMasters(this ICollection<string> masters)
        {
            try
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    connection.Open();
                    string query = $"SELECT FullName FROM masters WHERE IsActive = 1 ORDER BY FullName ASC";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                masters.Add(reader.GetString(0));
                            }
                        }
                    }
                }
                return DbResult.Ok;
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        Util.WriteLog(sqlEx, $"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
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

        public static DbResult ReadMachines(this ICollection<string> machines)
        {
            machines.Clear();
            try
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    connection.Open();
                    string query = $"SELECT Name FROM cnc_machines WHERE IsActive = 1 ORDER BY Name ASC";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                machines.Add(reader.GetString(0));
                            }
                        }
                    }
                }
                return DbResult.Ok;
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        Util.WriteLog(sqlEx, $"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
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
        /// Это для MachineFilter, для Machine использовать GetMachinesAsync
        /// </summary>
        /// <param name="machines"></param>
        /// <returns></returns>
        public async static Task<DbResult> ReadMachines(this ICollection<MachineFilter> machines)
        {
            try
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    await connection.OpenAsync();
                    string query = $"SELECT Name, Type FROM cnc_machines WHERE IsActive = 1 ORDER BY Name ASC";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                machines.Add(new(await reader.GetFieldValueAsync<string>(0), await reader.GetFieldValueAsync<string>(1), false));
                            }
                        }
                    }
                }
                return DbResult.Ok;
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        Util.WriteLog(sqlEx, $"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
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

        public static DbResult ReadDeviationReasons(this ICollection<(string, bool)> reasons, DeviationReasonType type)
        {
            try
            {
                reasons.Clear();
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    var typeCondition = type switch
                    {
                        DeviationReasonType.Setup => "Type = 'Setup'",
                        DeviationReasonType.Machining => "Type = 'Machining'",
                        _ => throw new ArgumentException("Неверный аргумент в типе причин."),
                    };
                    connection.Open();
                    string query = $"SELECT Reason, RequireComment FROM cnc_deviation_reasons WHERE Type IS NULL OR {typeCondition} ORDER BY Reason ASC";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                reasons.Add((reader.GetString(0), reader.GetBoolean(1)));
                            }
                        }
                    }
                }
                return DbResult.Ok;
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        Util.WriteLog(sqlEx, $"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
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

        public static DbResult ReadDowntimeReasons(this ICollection<string> reasons)
        {
            try
            {
                reasons.Clear();
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    connection.Open();
                    string query = $"SELECT Reason FROM cnc_downtime_reasons ORDER BY Reason ASC";
                    using (SqlCommand command = new(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                reasons.Add(reader.GetString(0));
                            }
                        }
                    }
                }
                return DbResult.Ok;
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        Util.WriteLog(sqlEx, $"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
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

        public static DbResult WriteShiftInfo(ShiftInfo shiftInfo)
        {
            try
            {
                ReadShiftInfo(shiftInfo, out var shifts);
                if (shifts is { Count: 1 })
                {
                    return UpdateShiftInfo(shiftInfo);
                }
                else if (shifts.Count > 1)
                {
                    MessageBox.Show("Найдена больше чем одна запись за смену, сообщите разработчику.", "Ошибка.", MessageBoxButton.OK, MessageBoxImage.Error);
                    Util.WriteLog("Найдена больше чем одна запись за смену.");
                    return DbResult.Error;
                }
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    if (AppSettings.Instance.DebugMode) Util.WriteLog("Запись в БД информации о смене.");
                    connection.Open();
                    string query = $"INSERT INTO cnc_shifts (ShiftDate, Shift, Machine, Master, UnspecifiedDowntimes, DowntimesComment, CommonComment, IsChecked) " +
                        $"VALUES (@ShiftDate, @Shift, @Machine, @Master, @UnspecifiedDowntimes, @DowntimesComment, @CommonComment, @IsChecked); SELECT SCOPE_IDENTITY()";
                    using (SqlCommand command = new(query, connection))
                    {
                        command.Parameters.AddWithValue("ShiftDate", shiftInfo.ShiftDate);
                        command.Parameters.AddWithValue("Shift", shiftInfo.Shift);
                        command.Parameters.AddWithValue("Machine", shiftInfo.Machine);
                        command.Parameters.AddWithValue("Master", shiftInfo.Master);
                        command.Parameters.AddWithValue("UnspecifiedDowntimes", shiftInfo.UnspecifiedDowntimes);
                        command.Parameters.AddWithValue("DowntimesComment", shiftInfo.DowntimesComment);
                        command.Parameters.AddWithValue("CommonComment", shiftInfo.CommonComment);
                        command.Parameters.AddWithValue("IsChecked", shiftInfo.IsChecked);
                        var result = command.ExecuteScalar();
                        if (AppSettings.Instance.DebugMode) Util.WriteLog($"Смена записана и присвоен ID: {shiftInfo.Id}");
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
                    case 2601 or 2627:
                        Util.WriteLog($"Ошибка №{sqlEx.Number}:\nЗапись в БД уже существует.");
                        return DbResult.Error;
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

        public static DbResult ReadShiftInfo(ShiftInfo shiftInfo, out List<ShiftInfo> shifts)
        {
            shifts = new List<ShiftInfo>();
            try
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    connection.Open();
                    string query = $"SELECT * FROM cnc_shifts WHERE ShiftDate = @ShiftDate AND Shift = @Shift AND Machine = @Machine";
                    using (SqlCommand command = new(query, connection))
                    {
                        command.Parameters.AddWithValue("ShiftDate", shiftInfo.ShiftDate);
                        command.Parameters.AddWithValue("Shift", shiftInfo.Shift);
                        command.Parameters.AddWithValue("Machine", shiftInfo.Machine);
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                shifts.Add(

                                    new ShiftInfo(
                                        reader.GetInt32(0),                 // Id
                                        reader.GetDateTime(1),              // ShiftDate
                                        reader.GetString(2),                // Shift
                                        reader.GetString(3),                // Machine
                                        reader.GetString(4),                // Master
                                        reader.GetDouble(5),                // UnspecifiedDowntimes
                                        reader.GetString(6),                // DowntimesComment
                                        reader.GetString(7),                // CommonComment
                                        reader.GetBoolean(8),               // IsChecked
                                        reader.GetNullableBoolean(9),       // GiverWorkplaceCleaned
                                        reader.GetNullableBoolean(10),      // GiverFailures
                                        reader.GetNullableBoolean(11),      // GiverExtraneousNoises
                                        reader.GetNullableBoolean(12),      // GiverLiquidLeaks
                                        reader.GetNullableBoolean(13),      // GiverToolBreakage
                                        reader.GetNullableDouble(14),       // GiverCoolantConcentration
                                        reader.GetNullableBoolean(15),      // RecieverWorkplaceCleaned
                                        reader.GetNullableBoolean(16),      // RecieverFailures
                                        reader.GetNullableBoolean(17),      // RecieverExtraneousNoises
                                        reader.GetNullableBoolean(18),      // RecieverLiquidLeaks
                                        reader.GetNullableBoolean(19),      // RecieverToolBreakage
                                        reader.GetNullableDouble(20)        // RecieverCoolantConcentration
                                        )
                                    );
                            }
                        }
                    }
                }
                return DbResult.Ok;
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        Util.WriteLog(sqlEx, $"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
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

        public static DbResult GetShiftsByPeriod(ICollection<string> machines, DateTime fromDate, DateTime toDate, Shift shift, out List<ShiftInfo> shifts)
        {
            shifts = new List<ShiftInfo>();
            try
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    connection.Open();
                    //string machineNums = string.Join(", ", machines.Select((_, i) => $"@machine{i}"));
                    string machinesNames = string.Join(", ", machines.Select(m => $"'{m}'"));

                    string query = $"SELECT * FROM cnc_shifts WHERE ShiftDate BETWEEN @FromDate AND @ToDate AND Machine IN ({machinesNames})";
                    if (shift.Type != Types.ShiftType.All) query += $" AND Shift = '{shift.Name}'";
                    using (SqlCommand command = new(query, connection))
                    {
                        command.Parameters.AddWithValue("FromDate", fromDate);
                        command.Parameters.AddWithValue("ToDate", toDate);

                        //for (int i = 0; i < machines.Length; i++)
                        //{
                        //    command.Parameters.AddWithValue($"machine{i}", machines[i]);
                        //}

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                shifts.Add(

                                    new ShiftInfo(
                                        reader.GetInt32(0),                 // Id
                                        reader.GetDateTime(1),              // ShiftDate
                                        reader.GetString(2),                // Shift
                                        reader.GetString(3),                // Machine
                                        reader.GetString(4),                // Master
                                        reader.GetDouble(5),                // UnspecifiedDowntimes
                                        reader.GetString(6),                // DowntimesComment
                                        reader.GetString(7),                // CommonComment
                                        reader.GetBoolean(8),               // IsChecked
                                        reader.GetNullableBoolean(9),       // GiverWorkplaceCleaned
                                        reader.GetNullableBoolean(10),      // GiverFailures
                                        reader.GetNullableBoolean(11),      // GiverExtraneousNoises
                                        reader.GetNullableBoolean(12),      // GiverLiquidLeaks
                                        reader.GetNullableBoolean(13),      // GiverToolBreakage
                                        reader.GetNullableDouble(14),       // GiverCoolantConcentration
                                        reader.GetNullableBoolean(15),      // RecieverWorkplaceCleaned
                                        reader.GetNullableBoolean(16),      // RecieverFailures
                                        reader.GetNullableBoolean(17),      // RecieverExtraneousNoises
                                        reader.GetNullableBoolean(18),      // RecieverLiquidLeaks
                                        reader.GetNullableBoolean(19),      // RecieverToolBreakage
                                        reader.GetNullableDouble(20)        // RecieverCoolantConcentration
                                        )
                                    );
                            }
                        }
                    }
                }
                return DbResult.Ok;
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        Util.WriteLog(sqlEx, $"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
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

        public static DbResult UpdateShiftInfo(ShiftInfo shiftInfo)
        {
            try
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    connection.Open();

                    string query = $"UPDATE cnc_shifts SET Master = @Master, UnspecifiedDowntimes = @UnspecifiedDowntimes, DowntimesComment = @DowntimesComment, CommonComment = @CommonComment, IsChecked = @IsChecked  " +
                        $"WHERE ShiftDate = @ShiftDate AND Shift = @Shift AND Machine = @Machine";
                    using (SqlCommand command = new(query, connection))
                    {
                        command.Parameters.AddWithValue("ShiftDate", shiftInfo.ShiftDate);
                        command.Parameters.AddWithValue("Shift", shiftInfo.Shift);
                        command.Parameters.AddWithValue("Machine", shiftInfo.Machine);
                        command.Parameters.AddWithValue("Master", shiftInfo.Master);
                        command.Parameters.AddWithValue("UnspecifiedDowntimes", shiftInfo.UnspecifiedDowntimes);
                        command.Parameters.AddWithValue("DowntimesComment", shiftInfo.DowntimesComment);
                        command.Parameters.AddWithValue("CommonComment", shiftInfo.CommonComment);
                        command.Parameters.AddWithValue("IsChecked", shiftInfo.IsChecked);
                        int rowsAffected = command.ExecuteNonQuery();
                        if (rowsAffected == 0)
                        {
                            Util.WriteLog("Смена не найдена, добавение новой.");
                            return WriteShiftInfo(shiftInfo);
                        }
                        else
                        {
                            if (AppSettings.Instance.DebugMode) Util.WriteLog($"Смена обновлена.");
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
                    case 2601 or 2627:
                        Util.WriteLog($"Ошибка №{sqlEx.Number}:\nЗапись в БД уже существует.");
                        return DbResult.Error;
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

        public static DbResult DeletePart(this Part part)
        {
            try
            {
                using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
                {
                    connection.Open();
                    string query = $"DELETE FROM parts WHERE GUID = @Guid";
                    using (SqlCommand command = new(query, connection))
                    {
                        command.Parameters.AddWithValue("Guid", part.Guid);
                        command.ExecuteNonQuery();
                    }
                }
                return DbResult.Ok;
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        Util.WriteLog(sqlEx, $"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
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
        /// Получает лимит наладки для заданного станка, используя строку подключения из настроек приложения.
        /// </summary>
        /// <param name="machine">Имя станка для получения лимита наладки.</param>
        /// <returns>
        /// Кортеж, состоящий из:
        /// - <see cref="DbResult"/>: результат выполнения запроса.
        /// - SetupLimit: лимит наладки для станка (nullable int), может быть null, если данных нет.
        /// - Error: строка с описанием ошибки, если она произошла.
        /// </returns>
        /// <exception cref="InvalidOperationException">Выбрасывается, если строка подключения отсутствует в настройках приложения.</exception>
        public static (DbResult Result, int? SetupLimit, string Error) GetMachineSetupLimit(this string machine)
        {
            if (AppSettings.Instance.ConnectionString == null) throw new InvalidOperationException("Невозомжно получить лимит наладки т.к. отсуствтует строка подключения");
            return machine.GetMachineSetupLimit(AppSettings.Instance.ConnectionString);
        }

        /// <summary>
        /// Получает коэффициент наладки для заданного станка, используя строку подключения из настроек приложения.
        /// </summary>
        /// <param name="machine">Имя станка для получения коэффициента наладки.</param>
        /// <returns>
        /// Кортеж, состоящий из:
        /// - <see cref="DbResult"/>: результат выполнения запроса (например, <c>Ok</c>, <c>NotFound</c>, <c>Error</c>).
        /// - SetupCoefficient: коэффициент наладки для станка (nullable double), может быть null, если данных нет.
        /// - Error: строка с описанием ошибки, если она произошла.
        /// </returns>
        /// <exception cref="InvalidOperationException">Выбрасывается, если строка подключения отсутствует в настройках приложения.</exception>
        public static (DbResult Result, double? SetupCoefficient, string Error) GetMachineSetupCoefficient(this string machine)
        {
            if (AppSettings.Instance.ConnectionString == null) throw new InvalidOperationException("Невозомжно получить коэффициент лимита наладки т.к. отсуствтует строка подключения");
            return machine.GetMachineSetupCoefficient(AppSettings.Instance.ConnectionString);
        }

        public static DbResult GetWncConfig(out WncConfig wncConfig)
        {
            wncConfig = null!;
            try
            {
                using (var connection = new SqlConnection(AppSettings.Instance.ConnectionString))
                {
                    connection.Open();
                    var query = $"SELECT * FROM cnc_wnc_cfg;";
                    using (var command = new SqlCommand(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                wncConfig = new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
                                break;
                            }
                        }
                    }
                }
                return DbResult.Ok;
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        Util.WriteLog(sqlEx, $"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
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

        internal static async Task UpdateAppSettings()
        {
            using (SqlConnection connection = new(AppSettings.Instance.ConnectionString))
            {
                await connection.OpenAsync();
                using (SqlCommand command = new("SELECT max_setup_limit, long_setup_limit, NcArchivePath, NcIntermediatePath, Administrators, CncOperations FROM cnc_remelog_config;", connection))
                {
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        var administrators = new List<string>();
                        var operatios = new List<string>();
                        while (await reader.ReadAsync())
                        {
                            if (!reader.IsDBNull(0)) AppSettings.MaxSetupLimit = await reader.GetFieldValueAsync<double>(0);
                            if (!reader.IsDBNull(1)) AppSettings.LongSetupLimit = await reader.GetValueOrDefaultAsync(1, 240.0);
                            if (!reader.IsDBNull(2)) AppSettings.NcArchivePath = await reader.GetValueOrDefaultAsync(2, "");
                            if (!reader.IsDBNull(3)) AppSettings.NcIntermediatePath = await reader.GetValueOrDefaultAsync(3, "");
                            if (!reader.IsDBNull(4)) administrators.Add(await reader.GetFieldValueAsync<string>(4));
                            if (!reader.IsDBNull(5)) operatios.Add(await reader.GetFieldValueAsync<string>(5));
                        }
                        AppSettings.Administrators = administrators.ToArray();
                        AppSettings.CncOperations = operatios.ToArray();
                    }
                }
                AppSettings.MaxSetupLimits.Clear();
                using (SqlCommand command = new("SELECT Name, SetupCoefficient FROM cnc_machines", connection))
                {
                    using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            AppSettings.MaxSetupLimits.Add(await reader.GetFieldValueAsync<string>(0), await reader.GetValueOrDefaultAsync(1, 1.5));
                        }
                    }
                }
                AppSettings.Save();
            }
        }
    }
}
