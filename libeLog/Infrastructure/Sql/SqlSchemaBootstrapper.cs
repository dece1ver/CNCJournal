using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static libeLog.Infrastructure.Sql.ApplicationFunctions;

namespace libeLog.Infrastructure.Sql
{
    public static class SqlSchemaBootstrapper
    {

        /// <summary>
        /// Применяет определение всех таблиц в базу данных. Создаёт отсутствующие таблицы, добавляет недостающие столбцы и ограничения.
        /// Безопасно вызывается повторно: если структура уже соответствует, ничего не делает.
        /// </summary>
        /// <param name="connection">Открытое соединение с SQL Server.</param>
        /// <param name="progress">Опциональный вывод прогресса.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static async Task ApplyAllAsync(string connectionString, SqlConnection connection, IProgress<(string, Status?)>? progress = null, CancellationToken cancellationToken = default)
        {
            var fnBuilder = new FunctionSqlBuilder();
            var manager = new FunctionManager(connectionString, fnBuilder);

            manager.Register(new FunctionDefinition
            {
                Name = "NormalizedPartNameWithoutComments",
                Parameters = new List<FunctionParameter>
                {
                    new("@name", "NVARCHAR(255)")
                },
                ReturnType = "NVARCHAR(255)",
                Options = new List<string> { "RETURNS NULL ON NULL INPUT", "SCHEMABINDING" },
                Body = @"
                    IF @name IS NULL OR LEN(LTRIM(RTRIM(@name))) = 0
                        RETURN '';

                    DECLARE @r NVARCHAR(255) = @name;

                    WHILE CHARINDEX('(', @r) > 0
                    BEGIN
                        DECLARE @s INT = CHARINDEX('(', @r);
                        DECLARE @e INT = CHARINDEX(')', @r, @s);
                        IF @e > 0
                            SET @r = STUFF(@r, @s, @e - @s + 1, '');
                        ELSE
                            BREAK;
                    END

                    SET @r = REPLACE(@r, '""', '');

                    WHILE CHARINDEX('  ', @r) > 0
                        SET @r = REPLACE(@r, '  ', ' ');

                    RETURN LOWER(LTRIM(RTRIM(@r)));"
            });
            manager.DeployAll();
            progress?.Report(($"Функции:{string.Join("\n", manager.Functions)}", Status.Ok));
            var tables = GetAllTableDefinitions();
            foreach (var table in tables)
            {
                await ApplyAsync(connectionString, connection, table, progress, cancellationToken);
            }
        }

        public static async Task ApplyAsync(
            string connectionString,
            SqlConnection connection,
            TableDefinition table,
            IProgress<(string, Status?)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var helper = new SqlSchemaDiffHelper(connectionString);
            progress?.Report(($"Таблица: {table.Name}", Status.Sync));

            await helper.ApplyMissingColumnsAndConstraintsAsync(table, progress, cancellationToken);

            if (table.Indexes.Count > 0)
            {
                string indexScript = SqlSchemaAlterHelper.GenerateAddIndexesScript(table.Name, table.Indexes);
                if (!string.IsNullOrWhiteSpace(indexScript))
                {
                    await ExecuteRawAsync(connection, indexScript, cancellationToken);
                    progress?.Report(($"Индексы таблицы {table.Name}", Status.Ok));
                }
            }

            progress?.Report(($"Таблица: {table.Name}", Status.Ok));
        }

        /// <summary>
        /// Выполняет произвольный DDL/DML-скрипт через SqlCommand
        /// Скрипт разбивается на батчи по разделителю GO (как SSMS).
        /// Удобно для индексов, представлений и прочего, что не описывается через TableBuilder.
        /// </summary>
        /// <param name="connection">Соединение с базой данных (открывать не нужно — метод управляет состоянием).</param>
        /// <param name="sql">Скрипт, возможно многобатчевый (с GO).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static async Task ExecuteRawAsync(
            SqlConnection connection,
            string sql,
            CancellationToken cancellationToken = default)
        {
            var batches = Regex.Split(sql, @"^\s*GO\s*($|--.*)", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            bool wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen)
                await connection.OpenAsync(cancellationToken);

            try
            {
                foreach (var batch in batches)
                {
                    string text = batch.Trim();
                    if (string.IsNullOrEmpty(text)) continue;

                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = text;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            finally
            {
                if (!wasOpen)
                    connection.Close();
            }
        }

        /// <summary>
        /// Возвращает все таблицы приложения, описанные через TableBuilder.
        /// </summary>
        public static List<TableDefinition> GetAllTableDefinitions() => new()
        {
            new TableBuilder("qc_inspections")
                .AddIdColumn()
                .AddStringColumn("part_name", 255, false)
                .AddStringColumn("order_number", 100, false)
                .AddStringColumn("parts_count", 50)
                .AddDateTimeColumn("started_at")
                .AddDateTimeColumn("completed_at")
                .AddStringColumn("result", 20)
                .AddStringColumn("operator", 100, false, "SUSER_SNAME()")
                .AddStringColumn("comment", -1, true)
                .AddIndex(
                    columns: new[] { "part_name", "order_number" },
                    filter:  "completed_at IS NULL",
                    name:    "IX_qc_inspections_active")
                .Build(),

            new TableBuilder("qc_users")
                .AddIdColumn()
                .AddStringColumn("Code1C", 255, false)
                .AddCompositeUnique("Code1C")
                .AddStringColumn("FirstName", 50, false)
                .AddStringColumn("LastName", 50, false)
                .AddStringColumn("Patronymic", 50)
                .AddBoolColumn("IsAdministrator", false, false)
                .Build(),

            new TableBuilder("cnc_deviation_reasons")
                .AddIdColumn()
                .AddStringColumn("Reason", -1, false)
                .AddNCharColumn("Type", 9)
                .AddBoolColumn("RequireComment", false)
                .Build(),

            new TableBuilder("cnc_downtime_reasons")
                .AddIdColumn()
                .AddStringColumn("Reason", 50, false)
                .Build(),

            new TableBuilder("cnc_elog_config")
                .AddIdColumn()
                .AddStringColumn("SearchToolTypes", 50)
                .AddStringColumn("UpdatePath", -1)
                .AddStringColumn("LogPath", -1)
                .AddStringColumn("OrderPrefixes", -1)
                .AddStringColumn("AssignedPartsGsId")
                .AddStringColumn("OrdersXlPath")
                .Build(),

            new TableBuilder("cnc_machines")
                .AddIdColumn()
                .AddStringColumn("Name", 50, false)
                .AddBoolColumn("IsActive", false)
                .AddBoolColumn("IsSerial", false, false)
                .AddStringColumn("Type", 50, false)
                .AddIntColumn("SetupLimit", false)
                .AddDoubleColumn("SetupCoefficient", false)
                .AddIntColumn("WnId", false)
                .AddGuidColumn("WnUuid", false, false)
                .AddStringColumn("WnCounterSignal", 8, true)
                .AddStringColumn("WnNcNameSignal", 8, true)
                .AddStringColumn("WnNcPartNameSignal", 8, true)
                .AddStringColumn("WnCurrentCSSignal", 8, true)
                .AddStringColumn("WnCurrentPlaneSignal", 8, true)
                .AddStringColumn("WnCurrentBlockNumberSignal", 8, true)
                .AddStringColumn("WnCurrentBlockTextSignal", 8, true)
                .AddStringColumn("WnNcModeSignal", 8, true)
                .AddStringColumn("WnFeedHoldSignal", 8, true)
                .AddStringColumn("WnSBKSignal", 8, true)
                .AddStringColumn("WnDryRunSignal", 8, true)
                .AddStringColumn("WnMSTLKSignal", 8, true)
                .AddStringColumn("WnMLKSignal", 8, true)
                .AddStringColumn("WnRapidMultiplierSignal", 8, true)
                .AddStringColumn("WnFeedMultiplierSignal", 8, true)
                .AddStringColumn("WnSpindleSpeed1MultiplierSignal", 8, true)
                .AddStringColumn("WnSpindleSpeed2MultiplierSignal", 8, true)
                .AddStringColumn("WnProgramRunningSignal", 8, true)
                .AddStringColumn("WnActSpindle1SpeedSignal", 8, true)
                .AddStringColumn("WnActSpindle2SpeedSignal", 8, true)
                .AddStringColumn("WnActCutSpeedSignal", 8, true)
                .AddStringColumn("WnActFeedPerMinSignal", 8, true)
                .AddStringColumn("WnActFeedPerRevSignal", 8, true)
                .AddStringColumn("WnStopSignal", 8, true)
                .AddStringColumn("WnOpStopSignal", 8, true)
                .AddStringColumn("WnMcodeSignal", 8, true)
                .AddStringColumn("WnGMoveSignal", 8, true)
                .AddStringColumn("WnGCycleSignal", 8, true)
                .AddStringColumn("WnGcodeSignal", 8, true)
                .AddStringColumn("WnAbsXSignal", 8, true)
                .AddStringColumn("WnAbsYSignal", 8, true)
                .AddStringColumn("WnAbsZSignal", 8, true)
                .AddStringColumn("WnAbsZASignal", 8, true)
                .AddStringColumn("WnAbsASignal", 8, true)
                .AddStringColumn("WnAbsBSignal", 8, true)
                .AddStringColumn("WnAbsCSignal", 8, true)
                .AddStringColumn("WnAbsWSignal", 8, true)
                .AddStringColumn("WnRelXSignal", 8, true)
                .AddStringColumn("WnRelYSignal", 8, true)
                .AddStringColumn("WnRelZSignal", 8, true)
                .AddStringColumn("WnRelZASignal", 8, true)
                .AddStringColumn("WnRelASignal", 8, true)
                .AddStringColumn("WnRelBSignal", 8, true)
                .AddStringColumn("WnRelCSignal", 8, true)
                .AddStringColumn("WnRelWSignal", 8, true)
                .AddStringColumn("WnMachXSignal", 8, true)
                .AddStringColumn("WnMachYSignal", 8, true)
                .AddStringColumn("WnMachZSignal", 8, true)
                .AddStringColumn("WnMachZASignal", 8, true)
                .AddStringColumn("WnMachASignal", 8, true)
                .AddStringColumn("WnMachBSignal", 8, true)
                .AddStringColumn("WnMachCSignal", 8, true)
                .AddStringColumn("WnMachWSignal", 8, true)
                .AddStringColumn("WnToolSignal", 8, true)
                .AddStringColumn("WnToolHSignal", 8, true)
                .AddStringColumn("WnToolDSignal", 8, true)
                .AddStringColumn("WnToolVectorSignal", 8, true)
                .AddStringColumn("WnToolGeomRSignal", 8, true)
                .AddStringColumn("WnToolGeomXSignal", 8, true)
                .AddStringColumn("WnToolGeomYSignal", 8, true)
                .AddStringColumn("WnToolGeomZSignal", 8, true)
                .AddStringColumn("WnToolWearRSignal", 8, true)
                .AddStringColumn("WnToolWearXSignal", 8, true)
                .AddStringColumn("WnToolWearYSignal", 8, true)
                .AddStringColumn("WnToolWearZSignal", 8, true)
                .AddStringColumn("WnAlarmMessageSignal", 8, true)
                .AddStringColumn("WnLoadXSignal", 8, true)
                .AddStringColumn("WnLoadYSignal", 8, true)
                .AddStringColumn("WnLoadZSignal", 8, true)
                .AddStringColumn("WnLoadCSignal", 8, true)
                .AddStringColumn("WnLoadBSignal", 8, true)
                .AddStringColumn("WnLoadWSignal", 8, true)
                .AddStringColumn("WnLoadSpindle1Signal", 8, true)
                .AddStringColumn("WnLoadSpindle2Signal", 8, true)
                .AddStringColumn("WnG54X", 8, true)
                .AddStringColumn("WnG54Y", 8, true)
                .AddStringColumn("WnG54Z", 8, true)
                .AddStringColumn("WnG54A", 8, true)
                .AddStringColumn("WnG54B", 8, true)
                .AddStringColumn("WnG54C", 8, true)
                .AddStringColumn("WnG54W", 8, true)
                .AddStringColumn("WnG54ZA", 8, true)
                .AddStringColumn("WnG55X", 8, true)
                .AddStringColumn("WnG55Y", 8, true)
                .AddStringColumn("WnG55Z", 8, true)
                .AddStringColumn("WnG55A", 8, true)
                .AddStringColumn("WnG55B", 8, true)
                .AddStringColumn("WnG55C", 8, true)
                .AddStringColumn("WnG55W", 8, true)
                .AddStringColumn("WnG55ZA", 8, true)
                .AddStringColumn("WnG56X", 8, true)
                .AddStringColumn("WnG56Y", 8, true)
                .AddStringColumn("WnG56Z", 8, true)
                .AddStringColumn("WnG56A", 8, true)
                .AddStringColumn("WnG56B", 8, true)
                .AddStringColumn("WnG56C", 8, true)
                .AddStringColumn("WnG56W", 8, true)
                .AddStringColumn("WnG56ZA", 8, true)
                .AddStringColumn("WnG57X", 8, true)
                .AddStringColumn("WnG57Y", 8, true)
                .AddStringColumn("WnG57Z", 8, true)
                .AddStringColumn("WnG57A", 8, true)
                .AddStringColumn("WnG57B", 8, true)
                .AddStringColumn("WnG57C", 8, true)
                .AddStringColumn("WnG57W", 8, true)
                .AddStringColumn("WnG57ZA", 8, true)
                .AddStringColumn("WnG58X", 8, true)
                .AddStringColumn("WnG58Y", 8, true)
                .AddStringColumn("WnG58Z", 8, true)
                .AddStringColumn("WnG58A", 8, true)
                .AddStringColumn("WnG58B", 8, true)
                .AddStringColumn("WnG58C", 8, true)
                .AddStringColumn("WnG58W", 8, true)
                .AddStringColumn("WnG58ZA", 8, true)
                .AddStringColumn("WnG59X", 8, true)
                .AddStringColumn("WnG59Y", 8, true)
                .AddStringColumn("WnG59Z", 8, true)
                .AddStringColumn("WnG59A", 8, true)
                .AddStringColumn("WnG59B", 8, true)
                .AddStringColumn("WnG59C", 8, true)
                .AddStringColumn("WnG59W", 8, true)
                .AddStringColumn("WnG59ZA", 8, true)
                .Build(),

            new TableBuilder("cnc_operators")
                .AddIdColumn()
                .AddStringColumn("FirstName", 50, false)
                .AddStringColumn("LastName", 50, false)
                .AddStringColumn("Patronymic", 50)
                .AddIntColumn("Qualification", false)
                .AddBoolColumn("IsActive", false)
                .Build(),

            new TableBuilder("cnc_qualifications")
                .AddIdColumn("Qualification")
                .AddDoubleColumn("EfficiencyValueHH", false, 1.05)
                .AddDoubleColumn("EfficiencyCoefficientHH", false, 1.4)
                .AddDoubleColumn("EfficiencyValueH", false, 0.95)
                .AddDoubleColumn("EfficiencyCoefficientH", false, 1.2)
                .AddDoubleColumn("EfficiencyValueN", false, 0.85)
                .AddDoubleColumn("EfficiencyCoefficientN", false, 1.0)
                .AddDoubleColumn("EfficiencyValueL", false, 0.75)
                .AddDoubleColumn("EfficiencyCoefficientL", false, 0.8)
                .AddDoubleColumn("EfficiencyValueLL", false, 0.65)
                .AddDoubleColumn("EfficiencyCoefficientLL", false, 0.6)
                .AddDoubleColumn("EfficiencyValueLLL", false, 0)
                .AddDoubleColumn("EfficiencyCoefficientLLL", false, 0.5)
                .AddDoubleColumn("DownTimesValueHH", false, 0.05)
                .AddDoubleColumn("DownTimesCoefficientHH", false, 1.2)
                .AddDoubleColumn("DownTimesValueH", false, 0.15)
                .AddDoubleColumn("DownTimesCoefficientH", false, 1.1)
                .AddDoubleColumn("DownTimesValueN", false, 0.2)
                .AddDoubleColumn("DownTimesCoefficientN", false, 1)
                .AddDoubleColumn("DownTimesValueL", false, 0.25)
                .AddDoubleColumn("DownTimesCoefficientL", false, 0.8)
                .AddDoubleColumn("DownTimesValueLL", false, 0.3)
                .AddDoubleColumn("DownTimesCoefficientLL", false, 0.8)
                .AddDoubleColumn("DownTimesValueLLL", false, 1)
                .AddDoubleColumn("DownTimesCoefficientLLL", false, .5)
                .AddDoubleColumn("NonSerialEfficiencyValueHH", false, 1)
                .AddDoubleColumn("NonSerialEfficiencyCoefficientHH", false, 1.4)
                .AddDoubleColumn("NonSerialEfficiencyValueH", false, 0.9)
                .AddDoubleColumn("NonSerialEfficiencyCoefficientH", false, 1.2)
                .AddDoubleColumn("NonSerialEfficiencyValueN", false, 0.8)
                .AddDoubleColumn("NonSerialEfficiencyCoefficientN", false, 1.0)
                .AddDoubleColumn("NonSerialEfficiencyValueL", false, 0.7)
                .AddDoubleColumn("NonSerialEfficiencyCoefficientL", false, 0.8)
                .AddDoubleColumn("NonSerialEfficiencyValueLL", false, 0.6)
                .AddDoubleColumn("NonSerialEfficiencyCoefficientLL", false, 0.6)
                .AddDoubleColumn("NonSerialEfficiencyValueLLL", false, 0)
                .AddDoubleColumn("NonSerialEfficiencyCoefficientLLL", false, 0.5)
                .Build(),

            new TableBuilder("cnc_remelog_config")
                .AddIdColumn()
                .AddDoubleColumn("max_setup_limit")
                .AddDoubleColumn("long_setup_limit")
                .AddStringColumn("NcArchivePath")
                .AddStringColumn("NcIntermediatePath")
                .AddStringColumn("CncOperations")
                .AddStringColumn("Administrators")
                .AddSmallDateTimeColumn("Holidays")
                .Build(),

            new TableBuilder("cnc_serial_parts")
                .AddIdColumn()
                .AddStringColumn("PartName", 255, false)
                .AddCompositeUnique("PartName")
                .AddIntColumn("YearCount", false)
                .AddComputedColumn("NormalizedPartName", "dbo.NormalizedPartNameWithoutComments(PartName)")
                .Build(),

            new TableBuilder("cnc_operations")
                .AddIdColumn()
                .AddIntColumn("SerialPartId", false)
                .AddStringColumn("Name", 255, false)
                .AddCompositeUnique("Name", "SerialPartId")
                .AddForeignKey("SerialPartId", "cnc_serial_parts", "Id", ForeignKeyAction.Cascade, ForeignKeyAction.Cascade)
                .AddIntColumn("OrderIndex", false, 0)
                .Build(),

            new TableBuilder("cnc_setups")
                .AddIdColumn()
                .AddIntColumn("CncOperationId", false)
                .AddByteColumn("Number", false)
                .AddCompositeUnique("CncOperationId", "Number")
                .AddForeignKey("CncOperationId", "cnc_operations", "Id", ForeignKeyAction.Cascade, ForeignKeyAction.Cascade)
                .Build(),

            new TableBuilder("cnc_normatives")
                .AddIdColumn()
                .AddIntColumn("CncSetupId", false)
                .AddByteColumn("NormativeType", false)
                .AddDoubleColumn("Value", false)
                .AddSmallDateTimeColumn("EffectiveFrom", false, "GETDATE()")
                .AddForeignKey("CncSetupId", "cnc_setups", "Id", ForeignKeyAction.Cascade, ForeignKeyAction.Cascade)
                .AddBoolColumn("IsAproved", false, false)
                .Build(),
            
            new TableBuilder("cnc_shifts")
                .AddIdColumn()
                .AddSmallDateTimeColumn("ShiftDate", false)
                .AddNCharColumn("Shift", 4, false)
                .AddStringColumn("Machine", -1, false)
                .AddStringColumn("Master", -1, false)
                .AddDoubleColumn("UnspecifiedDowntimes", false)
                .AddStringColumn("DowntimesComment", -1, false)
                .AddStringColumn("CommonComment", -1, false)
                .AddBoolColumn("IsChecked", false)
                .AddBoolColumn("GiverWorkplaceCleaned")
                .AddBoolColumn("GiverFailures")
                .AddBoolColumn("GiverExtraneousNoises")
                .AddBoolColumn("GiverLiquidLeaks")
                .AddBoolColumn("GiverToolBreakage")
                .AddDoubleColumn("GiverCoolantConcentration")
                .AddBoolColumn("RecieverWorkplaceCleaned")
                .AddBoolColumn("RecieverFailures")
                .AddBoolColumn("RecieverExtraneousNoises")
                .AddBoolColumn("RecieverLiquidLeaks")
                .AddBoolColumn("RecieverToolBreakage")
                .AddDoubleColumn("RecieverCoolantConcentration")
                .Build(),

            new TableBuilder("cnc_tool_search_cases")
                .AddIdColumn()
                .AddGuidColumn("PartGuid", nullable: false)
                .AddStringColumn("ToolType", 50, false)
                .AddStringColumn("Value", -1, false)
                .AddSmallDateTimeColumn("StartTime", false)
                .AddSmallDateTimeColumn("EndTime", false)
                .AddBoolColumn("IsSuccess")
                .AddForeignKey("PartGuid", "parts", "Guid", ForeignKeyAction.Cascade, ForeignKeyAction.Cascade)
                .Build(),

            new TableBuilder("cnc_wnc_cfg")
                .AddStringColumn("Server", 50, false)
                .AddStringColumn("User", 50, false)
                .AddStringColumn("Pass", 50, false)
                .AddStringColumn("LocalType", 50, false)
                .Build(),

            new TableBuilder("cnc_winnum_cfg")
                .AddIdColumn()
                .AddStringColumn("BaseUri", 255, false)
                .AddStringColumn("User", 50, false)
                .AddStringColumn("Pass", 50, false)
                .AddStringColumn("NcProgramFolder")
                .Build(),

            new TableBuilder("masters")
                .AddIdColumn()
                .AddStringColumn("FullName", -1, false)
                .AddBoolColumn("IsActive", false)
                .Build(),

            new TableBuilder("parts")
                .AddGuidColumn("Guid", isPrimaryKey: true)
                .AddStringColumn("Machine", -1, false)
                .AddNCharColumn("Shift", 4, false)
                .AddSmallDateTimeColumn("ShiftDate", false)
                .AddStringColumn("Operator", -1, false)
                .AddStringColumn("PartName", -1, false)
                .AddComputedColumn("NormalizedPartName", "dbo.NormalizedPartNameWithoutComments(PartName)")
                .AddStringColumn("Order", 50, false)
                .AddIntColumn("Setup", false)
                .AddDoubleColumn("FinishedCount", false)
                .AddIntColumn("DefectiveCount", false, 0)
                .AddIntColumn("TotalCount", false)
                .AddSmallDateTimeColumn("StartSetupTime", false)
                .AddSmallDateTimeColumn("StartMachiningTime", false)
                .AddDoubleColumn("SetupTimeFact", false)
                .AddSmallDateTimeColumn("EndMachiningTime", false)
                .AddDoubleColumn("SetupTimePlan", false)
                .AddDoubleColumn("SetupTimePlanForReport", false)
                .AddDoubleColumn("SingleProductionTimePlan", false)
                .AddDoubleColumn("ProductionTimeFact", false)
                .AddSqlServerColumn("MachiningTime", "BIGINT", o => o.Nullable(false))
                .AddDoubleColumn("SetupDowntimes", false)
                .AddDoubleColumn("MachiningDowntimes", false)
                .AddDoubleColumn("PartialSetupTime", false)
                .AddDoubleColumn("CreateNcProgramTime", false)
                .AddDoubleColumn("MaintenanceTime", false)
                .AddDoubleColumn("ToolSearchingTime", false)
                .AddDoubleColumn("ToolChangingTime", false)
                .AddDoubleColumn("MentoringTime", false)
                .AddDoubleColumn("ContactingDepartmentsTime", false)
                .AddDoubleColumn("FixtureMakingTime", false)
                .AddDoubleColumn("HardwareFailureTime", false)
                .AddStringColumn("OperatorComment", -1, false)
                .AddStringColumn("MasterSetupComment", -1)
                .AddStringColumn("MasterMachiningComment", -1)
                .AddStringColumn("SpecifiedDowntimesComment", -1)
                .AddStringColumn("UnspecifiedDowntimeComment", -1)
                .AddStringColumn("MasterComment", -1)
                .AddDoubleColumn("FixedSetupTimePlan")
                .AddDoubleColumn("FixedProductionTimePlan")
                .AddStringColumn("EngineerComment", -1)
                .AddBoolColumn("ExcludeFromReports")
                .AddStringColumn("LongSetupReasonComment", -1)
                .AddStringColumn("LongSetupFixComment", -1)
                .AddStringColumn("LongSetupEngeneerComment", -1)
                .AddDoubleColumn("ExcludedOperationsTime")
                .AddStringColumn("IncreaseReason", -1)
                .AddDoubleColumn("SpecialDowntimeTime", false, 0)
                .Build()
        };
    }
}
