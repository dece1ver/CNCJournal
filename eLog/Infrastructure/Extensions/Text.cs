namespace eLog.Infrastructure.Extensions
{
    public static class Text
    {
        public const string WithoutOrderItem = " ";
        public const string WithoutOrderDescription = "Без М/Л";
        public const string DayShift = "День";
        public const string NightShift = "Ночь";

        public static readonly string[] Shifts = { DayShift, NightShift };

        public static class DownTimes
        {
            public const string CreateNcProgram = "Написание УП";
            public const string Maintenance = "Обслуживание";
            public const string ToolSearching = "Поиск и получение инструмента";
            public const string ToolChanging = "Замена инструмента";
            public const string Mentoring = "Помощь / обучение";
            public const string ContactingDepartments = "Обращение в другие службы";
            public const string FixtureMaking = "Изготовление оснастки и калибров";
            public const string HardwareFailure = "Отказ оборудования";
            public const string PartialSetup = "Частичная наладка";
        }

        public static class ValidationErrors
        {
            public const string PartName =
                "Наименование детали.\n\nНе может быть пустым\n\nДолжно совпадать с наименованием в М/Л\n(записи в скобках можно не указывать)\n\n" +
                "Пример ввода:\nКорпус клапана АР110-01-001";

            public const string StartSetupTime = "Время начала наладки.\n\nПри изготовлении без наладки - время начала изготовления.\n\nНе может быть пустым.\n\n" +
                                                 "Пример ввода (формат важен):\n01.01.2023 07:00";
            public const string StartMachiningTime = "Время завершения наладки.\n\nПри изготовлении без наладки блокируется кнопкой \"Убрать наладку\".\n\n" +
                                                     "Если наладка производится, но не завершена, то поле оставляется пустым.\n\n" +
                                                     "Пример ввода (формат важен):\n01.01.2023 08:00";
            public const string EndMachiningTime = "Время завершения изготовления.\n\n" +
                                                   "Если изготовление не завершено, то поле оставляется пустым.\n\n" +
                                                   "При изготовлении более одной детали должно быть позже времени завершения наладки.\n\n" +
                                                   "Пример ввода (формат важен):\n01.01.2023 11:30";
            public const string EndMachiningTimeOnePart = "Время завершения изготовления.\n\n" +
                                                   "При изготовлении одной детали время завершения изготовления должно совпадать со временем завершения наладки, т.к. одна деталь выполняется в процессе наладки.\n\n" +
                                                   "Пример ввода (формат важен):\n01.01.2023 11:30";
            public const string MachineTime = "Машинное время.\n\nМожет быть пустым, только если не завершено изготовление.\n\n" +
                                              "Допустимые форматы:\n" +
                                              "<мм> (допускается с дробной частью)\n" +
                                              "<мм>:<сс>\n" +
                                              "<чч>:<мм>:<сс>" +
                                              "\n\nПримеры для 5 минут:\n5\n5:00\n00:05:00" +
                                              "\n\nПримеры для 2 минут 30 секунд:\n2.5\n2:30\n00:02:30";
            public const string DefectiveCount = "Количество бракованных деталей.\nМожет быть только целым числом. (пустой ввод = 0)";
            public const string FinishedCount = "Количество лично изготовленных деталей.\n\nМожет быть пустым, только если не завершено изготовление.";
            public const string FinishedCountLessOne = "При указанной наладке, количество изготовленных деталей должно быть более 1, т.к. одна уже произведена в наладке.";
            public const string TotalCount = "Количество деталей по заказу.\n\nНе может быть пустым.\n\n" +
                                             "Допускается только ввод целых чисел";
            public const string PartSetupTimePlan = "Норматив на наладку (в минутах).\n\nНе может быть пустым.\n\nЗначение берется из технологической карты.\n\nПри отсутствии норматива вводится \"-\"\n\n" +
                                                    "Допускается ввод целых чисел, либо знака \"-\"";
            public const string SingleProductionTimePlan = "Норматив на изготовление одной детали (в минутах).\n\nНе может быть пустым.\n\nЗначение берется из технологической карты.\n\nПри отсутствии норматива вводится \"-\"\n\n" +
                                                           "Допускается ввод целых и дробных чисел, либо знака \"-\"";
            public const string OrderText = "Номер заказа по М/Л исключая подразделение и месяц." +
                                            "\n\nПример ввода для УЧ-01/00035.5.2:\n" +
                                            "00035.5.2";
        }
    }
}