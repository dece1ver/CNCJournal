using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace libeLog
{
    public static class Constants
    {
        /// <summary>
        /// Максимальный размер файла логов.
        /// </summary>
        public const long MaxLogSize = 8388608;

        public const string ShortDateFormat = "dd.MM.yy";
        public const string DateTimeFormat = "dd.MM.yyyy HH:mm";
        public const string HHmmFormat = "HH:mm";
        public const string HHmmssFormat = "HH:mm:ss";
        public const string DateTimeWithSecsFormat = "dd.MM.yyyy HH:mm:ss";
        public const string TimeSpanFormat = @"hh\:mm\:ss";
        public static readonly HashSet<string> ComparisonOperators = new()
        {
            "<=", "<", ">=", "=", ">", "!="
        };

        public class StatusTips
        {
            public const string Ok = "Всё в порядке";
            public const string Checking = "Проверка";
            public const string AccessError = "Ошибка при проверке доступа";
            public const string InvalidPath = "Некоррктный путь";
            public const string GeneralError = "Общая ошибка ввода-вывода";

            public const string NoFile = "Файл не существует";
            public const string FileInUse = "Файл занят";

            public const string NoAccessToDirectory = "Директория не существует или отсутствуют права на её чтение";
            public const string NoWriteAccess = "Нет доступа на запись. Работа в режиме чтения";

            public const string NoConnectionToDb = "Сервер БД не найден или недоступен";
            public const string AuthFailedToDb = "Неверные учетные данные";

            public const string GsError = "Не удалось подключиться к таблице";
            public const string NotSet = "Не указано";

        }

        public struct SqlErrorCode
        {
            public const int NoConnection = -1;
            public const int AuthError = 18456;
        }

        public static class WorkTime
        {
            public static readonly DateTime DayShiftFirstBreak = new(1, 1, 1, 9, 0, 0);
            public static readonly DateTime DayShiftSecondBreak = new(1, 1, 1, 12, 30, 0);
            public static readonly DateTime DayShiftThirdBreak = new(1, 1, 1, 15, 15, 0);
            public static readonly DateTime NightShiftFirstBreak = new(1, 1, 1, 22, 30, 0);
            public static readonly DateTime NightShiftSecondBreak = new(1, 1, 1, 1, 30, 0);
            public static readonly DateTime NightShiftThirdBreak = new(1, 1, 1, 4, 30, 0);
        }

        public static class Machines
        {
            public const string GoodwayGs1500 = "Goodway GS-1500";
            public const string HuyndaiSkt21_104 = "Hyundai WIA SKT21 №104";
            public const string HuyndaiSkt21_105 = "Hyundai WIA SKT21 №105";
            public const string HuyndaiL230A = "Hyundai L230A";
            public const string HuyndaiXH6300 = "Hyundai XH6300";
            public const string MazakQts200Ml = "Mazak QTS200ML";
            public const string MazakQts350 = "Mazak QTS350";
            public const string MazakNexus5000 = "Mazak Nexus 5000";
            public const string MazakIntegrexI200 = "Mazak Integrex i200";
            public const string QuaserMv134 = "Quaser MV134";
            public const string VictorA110 = "Victor A110";
            public const string RontekHTC550MY = "Rontek HTC550MY";
            public const string RontekHTC650M = "Rontek HTC650M";
            public const string RontekVMC40C = "Rontek VMC40C";
            public const string RontekHB1316 = "Rontek HB1316";

            public static readonly string[] MachinesArray =
            {
                GoodwayGs1500,
                HuyndaiSkt21_104,
                HuyndaiSkt21_105,
                HuyndaiL230A,
                HuyndaiXH6300,
                MazakQts200Ml,
                MazakQts350,
                MazakNexus5000,
                MazakIntegrexI200,
                QuaserMv134,
                VictorA110,
                RontekHTC550MY,
                RontekHTC650M,
                RontekVMC40C,
                RontekHB1316
            };
        }

        public static class Dates
        {
            public static List<DateTime> Holidays { get; set; } = new();
        }

        public static class Colors 
        {
            public static readonly SolidColorBrush AreopagBlue = new(Color.FromRgb(0, 0x24, 0x3D));
            public static readonly SolidColorBrush AreopagBlue90 = new(Color.FromRgb(0x30, 0x3c, 0x60));
            public static readonly SolidColorBrush AreopagBlue80 = new(Color.FromRgb(0x47, 0x51, 0x72));
            public static readonly SolidColorBrush AreopagBlue70 = new(Color.FromRgb(0x5e, 0x67, 0x84));
            public static readonly SolidColorBrush AreopagBlue60 = new(Color.FromRgb(0x75, 0x7C, 0x95));
            public static readonly SolidColorBrush AreopagBlue30 = new(Color.FromRgb(0xBA, 0xBE, 0xCA));
            public static readonly SolidColorBrush AreopagBlue20 = new(Color.FromRgb(0xD1, 0xD4, 0xDC));
            public static readonly SolidColorBrush AreopagGreen = new(Color.FromRgb(0x00, 0xAD, 0x68));
            public static readonly SolidColorBrush AreopagGreen90 = new(Color.FromRgb(0x1A, 0xB5, 0x77));
            public static readonly SolidColorBrush AreopagGreen20 = new(Color.FromRgb(0xCD, 0xEE, 0xE1));
            public static readonly SolidColorBrush AreopagRed = new(Color.FromRgb(0xE6, 0x3C, 0x2F));
            public static readonly SolidColorBrush AreopagRed90 = new(Color.FromRgb(0xE8, 0x4F, 0x44));
            public static readonly SolidColorBrush AreopagRed80 = new(Color.FromRgb(0xEB, 0x63, 0x58));
            public static readonly SolidColorBrush AreopagRed20 = new(Color.FromRgb(0xFA, 0xD8, 0xD6));
            public static readonly SolidColorBrush Gray = new(Color.FromRgb(0x9E, 0x9E, 0x9E));
        }
    }
}