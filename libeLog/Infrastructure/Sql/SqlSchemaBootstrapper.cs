using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
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
        public static async Task ApplyAllAsync(SqlConnection connection, IProgress<(string, Status?)>? progress = null, CancellationToken cancellationToken = default)
        {
            var fnBuilder = new FunctionSqlBuilder();
            var manager = new FunctionManager(connection.ConnectionString, fnBuilder);

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
                await ApplyAsync(connection, table, progress, cancellationToken);
            }
        }

        public static async Task ApplyAsync(SqlConnection connection, TableDefinition table, IProgress<(string, Status?)>? progress = null, CancellationToken cancellationToken = default)
        {
            var helper = new SqlSchemaDiffHelper(connection.ConnectionString);
            progress?.Report(($"Таблица: {table.Name}", Status.Sync));
            var updated = await helper.ApplyMissingColumnsAndConstraintsAsync(table, progress, cancellationToken);
            if (updated)
                progress?.Report(($"Таблица: {table.Name}", Status.Ok));
            else
                progress?.Report(($"Таблица: {table.Name}", Status.Ok));   
        }

        /// <summary>
        /// Возвращает все таблицы приложения, описанные через TableBuilder.
        /// </summary>
        public static List<TableDefinition> GetAllTableDefinitions() => new()
        {
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
