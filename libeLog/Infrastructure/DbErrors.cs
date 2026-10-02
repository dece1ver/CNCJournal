using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace libeLog.Infrastructure
{
    /// <summary>
    /// Определение ошибок «сервер недоступен».
    /// Такие ошибки повторяются каждые несколько секунд, пока нет сети, и обычная запись в лог
    /// (со стектрейсом на 2-3 КБ) забивает файл за несколько часов. Их пишем одним разом на обрыв,
    /// см. <see cref="Logs"/>.
    /// </summary>
    public static class DbErrors
    {
        /// <summary>
        /// Номера SqlException, означающие, что сервер не отвечает или недоступен.
        /// Ошибки данных (усечение, дубликат, авторизация) сюда не входят — их нужно писать как есть.
        /// </summary>
        private static readonly HashSet<int> ConnectionErrorNumbers = new()
        {
            -2,      // истекло время ожидания выполнения команды
            53,      // сервер не найден / не удалось открыть соединение
            64,      // сетевой путь не найден (именованные каналы)
            233,     // отказано в соединении (порт закрыт)
            3609,    // истекло время ожидания в ходе операции
            4060,    // не удалось открыть базу данных по имени
            40613,   // база данных недоступна
            10053,   // соединение прервано приложением
            10054,   // соединение закрыто удалённым узлом
            10060,   // истекло время ожидания при подключении
            10928,   // недостаточно ресурсов на сервере
            10929,   // сервер слишком мал для обработки запроса
            11001,   // такого узла нет
        };

        /// <summary>
        /// Сеть недоступна или сервер не отвечает. Ошибки данных и авторизации не считаются сетевыми.
        /// </summary>
        /// <param name="exception">Исключение (проверяется и цепочка вложенных).</param>
        public static bool IsConnectionError(Exception? exception)
        {
            var depth = 0;
            for (var e = exception; e is not null && depth < 8; e = e.InnerException, depth++)
            {
                switch (e)
                {
                    case SqlException sql:
                        if (ConnectionErrorNumbers.Contains(sql.Number)) return true;
                        break;
                    case SocketException:
                    case NetworkInformationException:
                    case TimeoutException:
                        return true;
                }
            }
            return false;
        }
    }
}