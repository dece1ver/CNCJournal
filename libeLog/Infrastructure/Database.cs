using Dapper;
using libeLog.Extensions;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using static libeLog.Infrastructure.Db.DbHelper;

namespace libeLog.Infrastructure
{
    public static class Database
    {
        public static int RemoveById(string connectionString, string id, string table)
        {
            using var conn = OpenConnection(connectionString);
            return conn.Execute($"DELETE FROM {table} WHERE Id = @Id", new { Id = id });
        }

        public static async Task<int> RemoveByIdAsync(string connectionString, string id, string table)
        {
            await using var conn = await OpenConnectionAsync(connectionString);
            return await conn.ExecuteAsync($"DELETE FROM {table} WHERE Id = @Id", new { Id = id });
        }

        public static DbResult<int?> GetMachineSetupLimit(this string machine, string connectionString)
        {
            try
            {
                using var conn = OpenConnection(connectionString);
                var limit = conn.QueryFirstOrDefault<int?>("SELECT SetupLimit FROM cnc_machines WHERE Name = @Name", new { Name = machine });
                return limit.HasValue
                    ? DbResult<int?>.Ok(limit)
                    : DbResult<int?>.NotFound();
            }
            catch (SqlException sqlEx)
            {
                return sqlEx.Number switch
                {
                    18456 => DbResult<int?>.Fail(DbResult.AuthError, sqlEx.Number.ToString()),
                    _ => DbResult<int?>.Fail(DbResult.Error, sqlEx.Number.ToString())
                };
            }
            catch (Exception ex)
            {
                return DbResult<int?>.FailWithError(ex.Message);
            }
        }

        public static DbResult<double?> GetMachineSetupCoefficient(this string machine, string connectionString)
        {
            try
            {
                using var conn = OpenConnection(connectionString);
                var coeff = conn.QueryFirstOrDefault<double?>("SELECT SetupCoefficient FROM cnc_machines WHERE Name = @Name", new { Name = machine });
                return coeff.HasValue
                    ? DbResult<double?>.Ok(coeff)
                    : DbResult<double?>.NotFound();
            }
            catch (SqlException sqlEx)
            {
                return sqlEx.Number switch
                {
                    18456 => DbResult<double?>.Fail(DbResult.AuthError, sqlEx.Number.ToString()),
                    _ => DbResult<double?>.Fail(DbResult.Error, sqlEx.Number.ToString())
                };
            }
            catch (Exception ex)
            {
                return DbResult<double?>.FailWithError(ex.Message);
            }
        }

        public static async Task<(string BaseUri, string User, string Pass, string NcProgramFolder)> GetWinnumConfigAsync(string connectionString)
        {
            await using var conn = await OpenConnectionAsync(connectionString);
            const string sql = "SELECT [BaseUri], [User], [Pass], [NcProgramFolder] FROM cnc_winnum_cfg";
            var row = await conn.QueryFirstOrDefaultAsync<(string BaseUri, string User, string Pass, string NcProgramFolder)>(sql);
            return row is (null, null, null, null) ? default : row;
        }

        public static async Task<List<SerialPart>> GetSerialPartsAsync(
            string connectionString,
            IProgress<string>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Строка подключения не может быть пустой", nameof(connectionString));

            progress?.Report("Открываем соединение с БД...");
            await using var conn = await OpenConnectionAsync(connectionString);

            var partsById = new Dictionary<int, SerialPart>();
            var opsById = new Dictionary<int, CncOperation>();
            var setupsById = new Dictionary<int, CncSetup>();

            progress?.Report("Читаем детали...");
            var parts = (await conn.QueryAsync<(int Id, string PartName, int YearCount)>(
                "SELECT Id, PartName, YearCount FROM cnc_serial_parts ORDER BY PartName")).AsList();

            foreach (var (id, name, year) in parts)
            {
                var part = new SerialPart
                {
                    Id = id,
                    PartName = name,
                    YearCount = year,
                    Operations = new ObservableCollection<CncOperation>()
                };
                partsById[id] = part;
            }

            progress?.Report("Читаем операции...");
            var ops = (await conn.QueryAsync<(int Id, int SerialPartId, string Name)>(
                "SELECT Id, SerialPartId, Name FROM cnc_operations ORDER BY Id")).AsList();

            foreach (var (id, partId, name) in ops)
            {
                var op = new CncOperation(name)
                {
                    Id = id,
                    Setups = new ObservableCollection<CncSetup>()
                };
                opsById[id] = op;
                if (partsById.TryGetValue(partId, out var part))
                    part.Operations.Add(op);
            }

            progress?.Report("Читаем установки...");
            var setups = (await conn.QueryAsync<(int Id, int CncOperationId, byte Number)>(
                "SELECT Id, CncOperationId, Number FROM cnc_setups ORDER BY Number")).AsList();

            foreach (var (id, opId, number) in setups)
            {
                var setup = new CncSetup
                {
                    Id = id,
                    Number = number,
                    Normatives = new ObservableCollection<NormativeEntry>()
                };
                setupsById[id] = setup;
                if (opsById.TryGetValue(opId, out var op))
                    op.Setups.Add(setup);
            }

            progress?.Report("Читаем нормативы...");
            var normatives = (await conn.QueryAsync<(int Id, int CncSetupId, byte NormativeType, double Value, DateTime EffectiveFrom, bool IsAproved)>(
                "SELECT Id, CncSetupId, NormativeType, Value, EffectiveFrom, IsAproved FROM cnc_normatives ORDER BY EffectiveFrom")).AsList();

            foreach (var (id, setupId, type, value, ef, apr) in normatives)
            {
                var entry = new NormativeEntry
                {
                    Id = id,
                    Type = (NormativeEntry.NormativeType)type,
                    Value = value,
                    EffectiveFrom = ef,
                    IsApproved = apr,
                };
                if (setupsById.TryGetValue(setupId, out var setup))
                    setup.Normatives.Add(entry);
            }

            progress?.Report($"Загрузка завершена: деталей={parts.Count}");
            return partsById.Values.ToList();
        }

        public static async Task SaveSerialPartAsync(
            SerialPart part,
            string connectionString,
            IProgress<string>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Пустая строка подключения", nameof(connectionString));
            if (part == null)
                throw new ArgumentNullException(nameof(part));

            await using var conn = await OpenConnectionAsync(connectionString);
            using var tx = conn.BeginTransaction();

            try
            {
                progress?.Report($"Сохраняем деталь {part.PartName}");

                if (part.Id == 0)
                {
                    var id = await conn.ExecuteScalarAsync<int>(
                        "INSERT INTO cnc_serial_parts(PartName, YearCount) VALUES(@name, @year); SELECT SCOPE_IDENTITY();",
                        new { name = part.PartName ?? string.Empty, year = part.YearCount }, tx);
                    part.Id = id;
                }
                else
                {
                    var rows = await conn.ExecuteAsync(
                        "UPDATE cnc_serial_parts SET PartName=@name, YearCount=@year WHERE Id=@id",
                        new { id = part.Id, name = part.PartName ?? string.Empty, year = part.YearCount }, tx);
                    if (rows == 0)
                        throw new InvalidOperationException($"Деталь с ID {part.Id} не найдена для обновления");
                }

                if (part.Id != 0)
                {
                    var dbOpIds = (await conn.QueryAsync<int>(
                        "SELECT Id FROM cnc_operations WHERE SerialPartId=@pid", new { pid = part.Id }, tx)).AsList();

                    var keepOpIds = part.Operations?.Select(o => o.Id).Where(id => id != 0).ToList() ?? new();
                    var delOpIds = dbOpIds.Except(keepOpIds).ToList();

                    if (delOpIds.Any())
                    {
                        await conn.ExecuteAsync(
                            "DELETE N FROM cnc_normatives N JOIN cnc_setups S ON N.CncSetupId = S.Id WHERE S.CncOperationId IN @ids",
                            new { ids = delOpIds }, tx);
                        await conn.ExecuteAsync(
                            "DELETE FROM cnc_setups WHERE CncOperationId IN @ids",
                            new { ids = delOpIds }, tx);
                        await conn.ExecuteAsync(
                            "DELETE FROM cnc_operations WHERE Id IN @ids",
                            new { ids = delOpIds }, tx);
                    }

                    if (part.Operations != null)
                    {
                        foreach (var op in part.Operations.Where(o => o.Id != 0))
                        {
                            var dbSetupIds = (await conn.QueryAsync<int>(
                                "SELECT Id FROM cnc_setups WHERE CncOperationId=@oid", new { oid = op.Id }, tx)).AsList();

                            var keepSetupIds = op.Setups?.Select(s => s.Id).Where(id => id != 0).ToList() ?? new();
                            var delSetupIds = dbSetupIds.Except(keepSetupIds).ToList();

                            if (delSetupIds.Any())
                            {
                                await conn.ExecuteAsync(
                                    "DELETE FROM cnc_normatives WHERE CncSetupId IN @ids",
                                    new { ids = delSetupIds }, tx);
                                await conn.ExecuteAsync(
                                    "DELETE FROM cnc_setups WHERE Id IN @ids",
                                    new { ids = delSetupIds }, tx);
                            }

                            if (op.Setups != null)
                            {
                                foreach (var setup in op.Setups.Where(s => s.Id != 0))
                                {
                                    var dbNormIds = (await conn.QueryAsync<int>(
                                        "SELECT Id FROM cnc_normatives WHERE CncSetupId=@sid",
                                        new { sid = setup.Id }, tx)).AsList();

                                    var keepNormIds = setup.Normatives?.Select(n => n.Id).Where(id => id != 0).ToList() ?? new();
                                    var delNormIds = dbNormIds.Except(keepNormIds).ToList();

                                    if (delNormIds.Any())
                                    {
                                        await conn.ExecuteAsync(
                                            "DELETE FROM cnc_normatives WHERE Id IN @ids",
                                            new { ids = delNormIds }, tx);
                                    }
                                }
                            }
                        }
                    }
                }

                if (part.Operations != null)
                {
                    foreach (var op in part.Operations)
                    {
                        progress?.Report($"Сохраняем операцию «{op.Name}»...");

                        if (op.Id == 0)
                        {
                            var id = await conn.ExecuteScalarAsync<int>(
                                "INSERT INTO cnc_operations(SerialPartId, Name, OrderIndex) VALUES(@pid, @name, @oid); SELECT SCOPE_IDENTITY();",
                                new { pid = part.Id, name = op.Name ?? string.Empty, oid = op.OrderIndex }, tx);
                            op.Id = id;
                        }
                        else
                        {
                            await conn.ExecuteAsync(
                                "UPDATE cnc_operations SET Name=@name, OrderIndex=@oid WHERE Id=@id",
                                new { id = op.Id, name = op.Name ?? string.Empty, oid = op.OrderIndex }, tx);
                        }

                        if (op.Setups != null)
                        {
                            foreach (var setup in op.Setups)
                            {
                                progress?.Report($"Сохраняем установку №{setup.Number}...");

                                if (setup.Id == 0)
                                {
                                    var id = await conn.ExecuteScalarAsync<int>(
                                        "INSERT INTO cnc_setups(CncOperationId, Number) VALUES(@oid, @num); SELECT SCOPE_IDENTITY();",
                                        new { oid = op.Id, num = setup.Number }, tx);
                                    setup.Id = id;
                                }
                                else
                                {
                                    await conn.ExecuteAsync(
                                        "UPDATE cnc_setups SET Number=@num WHERE Id=@id",
                                        new { id = setup.Id, num = setup.Number }, tx);
                                }

                                if (setup.Normatives != null)
                                {
                                    foreach (var norm in setup.Normatives.Where(n => n.Id == 0))
                                    {
                                        progress?.Report($"Добавляем норматив {norm.Type}={norm.Value}...");
                                        await conn.ExecuteAsync(
                                            "INSERT INTO cnc_normatives(CncSetupId, NormativeType, Value, EffectiveFrom, IsAproved) VALUES(@sid, @type, @val, @ef, @apr)",
                                            new { sid = setup.Id, type = (byte)norm.Type, val = norm.Value, ef = norm.EffectiveFrom, apr = norm.IsApproved }, tx);
                                    }
                                }
                            }
                        }
                    }
                }

                await tx.CommitAsync();
                progress?.Report("Сохранение завершено");
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }
}
