using Dapper;
using libeLog.Infrastructure;
using libeLog.Models;
using Microsoft.Data.SqlClient;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static libeLog.Infrastructure.Db.DbHelper;

namespace remeLog.Infrastructure
{
    public static partial class Database
    {
        public static string GetLicenseKey(string licenseName)
        {
            try
            {
                using var conn = OpenConnection(AppSettings.Instance.ConnectionString);
                return conn.QueryFirstOrDefault<string>(
                    "SELECT license_key FROM licensing WHERE license_name = @name",
                    new { name = licenseName }) ?? "";
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return "";
            }
        }

        public static async Task<int> RemoveNormativeAsync(NormativeEntry normative)
        {
            return await libeLog.Infrastructure.Database.RemoveByIdAsync(
                AppSettings.Instance.ConnectionString!, normative.Id.ToString(), "cnc_normatives");
        }

        public static List<OperatorInfo> GetOperators()
        {
            using var conn = OpenConnection(AppSettings.Instance.ConnectionString);
            return conn.Query<OperatorInfo>(
                "SELECT Id, FirstName, LastName, Patronymic, Qualification, IsActive FROM cnc_operators ORDER BY LastName ASC").AsList();
        }

        public async static Task<List<OperatorInfo>> GetOperatorsAsync(IProgress<string>? progress = null)
        {
            progress?.Report("Подключение к БД...");
            try
            {
                await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
                progress?.Report("Чтение данных из БД...");
                var result = (await conn.QueryAsync<OperatorInfo>(
                    "SELECT Id, FirstName, LastName, Patronymic, Qualification, IsActive FROM cnc_operators ORDER BY LastName ASC")).AsList();
                progress?.Report("Чтение завершено");
                return result;
            }
            catch (Exception ex)
            {
                Util.WriteLog(ex);
                return new List<OperatorInfo>();
            }
        }

        public static async Task SaveOperatorAsync(OperatorInfo operatorInfo, IProgress<string> progress)
        {
            const string query = @"IF EXISTS (SELECT 1 FROM cnc_operators WHERE Id = @Id)
                BEGIN UPDATE cnc_operators SET FirstName=@FirstName, LastName=@LastName, Patronymic=@Patronymic, Qualification=@Qualification, IsActive=@IsActive WHERE Id=@Id; END
                ELSE BEGIN INSERT INTO cnc_operators(FirstName, LastName, Patronymic, Qualification, IsActive) VALUES(@FirstName, @LastName, @Patronymic, @Qualification, @IsActive); END";
            progress.Report("Подключение к БД...");
            await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
            progress.Report($"Сохранение оператора '{operatorInfo.DisplayName}' в БД...");
            await conn.ExecuteAsync(query, new
            {
                operatorInfo.Id,
                operatorInfo.FirstName,
                operatorInfo.LastName,
                operatorInfo.Patronymic,
                operatorInfo.Qualification,
                operatorInfo.IsActive
            });
            progress.Report($"Оператор '{operatorInfo.DisplayName}' успешно сохранен.");
        }

        public static async Task SaveOperatorsAsync(IEnumerable<OperatorInfo> operators, IProgress<string> progress)
        {
            progress.Report("Сохранение операторов в БД");
            foreach (var op in operators)
                await SaveOperatorAsync(op, progress);
            progress.Report("Сохранение операторов в БД выполнено");
        }

        public static async Task DeleteOperatorAsync(int operatorId, IProgress<string> progress)
        {
            progress.Report("Удаление оператора из БД...");
            await using var conn = await OpenConnectionAsync(AppSettings.Instance.ConnectionString);
            var rows = await conn.ExecuteAsync("DELETE FROM cnc_operators WHERE Id = @Id", new { Id = operatorId });
            progress.Report(rows > 0 ? "Оператор успешно удален." : "Оператор не найден.");
        }
    }
}
