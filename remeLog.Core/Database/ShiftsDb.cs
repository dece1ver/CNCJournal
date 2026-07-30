using Microsoft.Data.SqlClient;
using remeLog.Core;
using remeLog.Core.Db;
using remeLog.Core.Extensions;
using remeLog.Infrastructure.Types;
using remeLog.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace remeLog.Infrastructure
{
    public static partial class Database
    {
        public static DbResult<bool> WriteShiftInfo(ShiftInfo shiftInfo)
        {
            try
            {
                var readResult = ReadShiftInfo(shiftInfo);
                if (readResult.IsOk && readResult.Value is { Count: 1 })
                {
                    return UpdateShiftInfo(shiftInfo);
                }
                else if (readResult.IsOk && readResult.Value.Count > 1)
                {
                    const string message = "Найдена больше чем одна запись за смену, сообщите разработчику.";
                    Log.Write(message);
                    return DbResult<bool>.Fail(DbResult.Error, message);
                }
                using (SqlConnection connection = new(DomainSettings.ConnectionString))
                {
                    if (DomainSettings.DebugMode) Log.Write("Запись в БД информации о смене.");
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
                        if (DomainSettings.DebugMode) Log.Write($"Смена записана и присвоен ID: {shiftInfo.Id}");
                    }
                    return DbResult<bool>.Ok(true);
                }
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case -1:
                        Log.Write("База данных недоступна.");
                        return DbResult<bool>.Fail(DbResult.NoConnection, "База данных недоступна.");
                    case 2601 or 2627:
                        Log.Write($"Ошибка №{sqlEx.Number}:\nЗапись в БД уже существует.");
                        return DbResult<bool>.Fail(DbResult.Error, $"Запись в БД уже существует.");
                    case 18456:
                        Log.Write($"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
                        return DbResult<bool>.Fail(DbResult.AuthError, "Ошибка авторизации.");
                    default:
                        Log.WriteError(sqlEx,$"Ошибка №{sqlEx.Number}:");
                        return DbResult<bool>.Fail(DbResult.Error, $"Ошибка №{sqlEx.Number}:");
                }
            }
            catch (Exception ex)
            {
                Log.WriteError(ex, null);
                return DbResult<bool>.FailWithError(ex.Message);
            }
        }

        public static DbResult<List<ShiftInfo>> ReadShiftInfo(ShiftInfo shiftInfo)
        {
            var shifts = new List<ShiftInfo>();
            try
            {
                using (SqlConnection connection = new(DomainSettings.ConnectionString))
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
                return DbResult<List<ShiftInfo>>.Ok(shifts);
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        Log.WriteError(sqlEx,$"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
                        return DbResult<List<ShiftInfo>>.Fail(DbResult.AuthError, "Ошибка авторизации.");
                    default:
                        Log.WriteError(sqlEx,$"Ошибка №{sqlEx.Number}:");
                        return DbResult<List<ShiftInfo>>.Fail(DbResult.Error, $"Ошибка №{sqlEx.Number}:");
                }
            }
            catch (Exception ex)
            {
                Log.WriteError(ex, null);
                return DbResult<List<ShiftInfo>>.FailWithError(ex.Message);
            }
        }

        public static DbResult<List<ShiftInfo>> GetShiftsByPeriod(ICollection<string> machines, DateTime fromDate, DateTime toDate, Shift shift)
        {
            var shifts = new List<ShiftInfo>();
            try
            {
                using (SqlConnection connection = new(DomainSettings.ConnectionString))
                {
                    connection.Open();
                    string machinesNames = string.Join(", ", machines.Select(m => $"'{m}'"));

                    string query = $"SELECT * FROM cnc_shifts WHERE ShiftDate BETWEEN @FromDate AND @ToDate AND Machine IN ({machinesNames})";
                    if (shift.Type != Types.ShiftType.All) query += $" AND Shift = '{shift.Name}'";
                    using (SqlCommand command = new(query, connection))
                    {
                        command.Parameters.AddWithValue("FromDate", fromDate);
                        command.Parameters.AddWithValue("ToDate", toDate);

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
                return DbResult<List<ShiftInfo>>.Ok(shifts);
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case 18456:
                        Log.WriteError(sqlEx,$"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
                        return DbResult<List<ShiftInfo>>.Fail(DbResult.AuthError, "Ошибка авторизации.");
                    default:
                        Log.WriteError(sqlEx,$"Ошибка №{sqlEx.Number}:");
                        return DbResult<List<ShiftInfo>>.Fail(DbResult.Error, $"Ошибка №{sqlEx.Number}:");
                }
            }
            catch (Exception ex)
            {
                Log.WriteError(ex, null);
                return DbResult<List<ShiftInfo>>.FailWithError(ex.Message);
            }
        }

        public static DbResult<bool> UpdateShiftInfo(ShiftInfo shiftInfo)
        {
            try
            {
                using (SqlConnection connection = new(DomainSettings.ConnectionString))
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
                            Log.Write("Смена не найдена, добавение новой.");
                            return WriteShiftInfo(shiftInfo);
                        }
                        else
                        {
                            if (DomainSettings.DebugMode) Log.Write($"Смена обновлена.");
                        }
                    }
                    return DbResult<bool>.Ok(true);
                }
            }
            catch (SqlException sqlEx)
            {
                switch (sqlEx.Number)
                {
                    case -1:
                        Log.Write("База данных недоступна.");
                        return DbResult<bool>.Fail(DbResult.NoConnection, "База данных недоступна.");
                    case 2601 or 2627:
                        Log.Write($"Ошибка №{sqlEx.Number}:\nЗапись в БД уже существует.");
                        return DbResult<bool>.Fail(DbResult.Error, $"Запись в БД уже существует.");
                    case 18456:
                        Log.Write($"Ошибка №{sqlEx.Number}:\nОшибка авторизации.");
                        return DbResult<bool>.Fail(DbResult.AuthError, "Ошибка авторизации.");
                    default:
                        Log.WriteError(sqlEx,$"Ошибка №{sqlEx.Number}:");
                        return DbResult<bool>.Fail(DbResult.Error, $"Ошибка №{sqlEx.Number}:");
                }
            }
            catch (Exception ex)
            {
                Log.WriteError(ex, null);
                return DbResult<bool>.FailWithError(ex.Message);
            }
        }
    }
}
