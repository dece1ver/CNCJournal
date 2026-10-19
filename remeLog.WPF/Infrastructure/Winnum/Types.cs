using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Infrastructure.Winnum
{
    public class Types
    {
        /// <summary>
        /// Получать данные по количеству или по времени
        /// </summary>
        public enum SignalType
        {
            /// <summary>
            /// По количеству
            /// </summary>
            ByCount, 
            /// <summary>
            /// По времени
            /// </summary>
            ByTime
        }

        /// <summary>
        /// Порядок сортировки
        /// </summary>
        public enum Ordering
        {
            /// <summary>
            /// По возрастанию
            /// </summary>
            Asc,
            /// <summary>
            /// По убыванию
            /// </summary>
            Desc
        }


        /// <summary>
        /// Приложения
        /// </summary>
        public enum AppId
        {
            /// <summary>Мониторинг (с учетом нагрузки)</summary>
            /// <remarks>Мониторинг станков с ЧПУ с дополнительными критериями</remarks>
            [Description("Мониторинг (с учетом нагрузки)")]
            CNC_MONITORING_WITH_LOAD = 1,

            /// <summary>Мониторинг оборудования</summary>
            /// <remarks>Мониторинг оборудования</remarks>
            [Description("Мониторинг оборудования")]
            CNC_MONITORING = 2,

            /// <summary>OEE (ЧПУ)</summary>
            /// <remarks>Приложение OEE для оборудования с ЧПУ</remarks>
            [Description("OEE (ЧПУ)")]
            OEE_CNC = 3,

            /// <summary>Планировки</summary>
            /// <remarks>2D планировки и 3D сцены</remarks>
            [Description("Планировки")]
            LAYOUTS = 5,

            /// <summary>Мониторинг с учетом ручных режимов работы</summary>
            /// <remarks>Мониторинг с учетом ручных режимов работы</remarks>
            [Description("Мониторинг с учетом ручных режимов работы")]
            CNC_MONITORING_WITH_MANUAL_MODES = 6,

            /// <summary>Мониторинг с учетом терминального учета</summary>
            /// <remarks>Мониторинг с учетом терминального учета (выбора причин простоя)</remarks>
            [Description("Мониторинг с учетом терминального учета")]
            CNC_MONITORING_WITH_TERMINAL = 7,

            /// <summary>Dynamic with loads CNC</summary>
            /// <remarks>Динамическое приложение с расчетом загрузки и операций производства</remarks>
            [Description("Dynamic with loads CNC")]
            DYNAMIC_WITH_LOADS = 8,

            /// <summary>Dynamic with 3D</summary>
            /// <remarks>Динамическое приложение с 3D</remarks>
            [Description("Dynamic with 3D")]
            DYNAMIC_WITH_3D = 9,

            /// <summary>Мониторинг с учетом терминального учета без обеда</summary>
            /// <remarks></remarks>
            [Description("Мониторинг с учетом терминального учета без обеда")]
            CNC_MONITORING_WITH_TERMINAL_NO_LUNCH = 10,

            /// <summary>Тест коннектора Mazak Smooth</summary>
            /// <remarks></remarks>
            [Description("Тест коннектора Mazak Smooth")]
            TEST_MAZAK_SMOOTH = 11,

            /// <summary>ОТК</summary>
            /// <remarks>ОТК</remarks>
            [Description("ОТК")]
            QUALITY_CONTROL = 13,

            /// <summary>123</summary>
            /// <remarks>123</remarks>
            [Description("123")]
            TEST_APP = 14,

            /// <summary>Базовое приложение по мониторингу</summary>
            /// <remarks>Базовое приложение по мониторингу</remarks>
            [Description("Базовое приложение по мониторингу")]
            BASIC_MONITORING = 15,

            /// <summary>ТЕСТ</summary>
            /// <remarks></remarks>
            [Description("ТЕСТ")]
            TEST = 16,

            /// <summary>WINNUM Станки Архив</summary>
            /// <remarks>Данные со старых версий станков</remarks>
            [Description("WINNUM Станки Архив")]
            CNC_ARCHIVE = 17,

            /// <summary>Мониторинг с учетом остановов и ручных режимов</summary>
            /// <remarks>Мониторинг с учетом технологических остановов и ручных режимов работы</remarks>
            [Description("Мониторинг с учетом остановов и ручных режимов")]
            CNC_MONITORING_WITH_STOPS_AND_MANUAL = 18,

            /// <summary>Бюллетени</summary>
            /// <remarks>Бюллетени - динамическое приложение</remarks>
            [Description("Бюллетени")]
            BULLETINS = 19,

            /// <summary>Отчёт OEE</summary>
            /// <remarks>OEE - динамическое приложение</remarks>
            [Description("Отчёт OEE")]
            OEE_REPORT = 20
        }


        /// <summary>
        /// Тэги оборудования
        /// </summary>
        public enum TagId
        {
            /// <summary>Нет тэга</summary>
            [Description("Нет тэга")]
            NONE = 0,

            /// <summary>Станок под нагрузкой</summary>
            [Description("Станок под нагрузкой")]
            NC_WIP = 1,

            /// <summary>Станок включен</summary>
            [Description("Станок включен")]
            NC_ON = 2,

            /// <summary>Станок выключен</summary>
            [Description("Станок выключен")]
            NC_OFF = 3,

            /// <summary>Аварийная остановка</summary>
            [Description("Аварийная остановка")]
            NC_EMERGENCY_STOP = 4,

            /// <summary>Режим auto</summary>
            [Description("Режим auto")]
            NC_MODE_AUTO = 5,

            /// <summary>Режим mda</summary>
            [Description("Режим mda")]
            NC_MODE_MDA = 6,

            /// <summary>Режим jog</summary>
            [Description("Режим jog")]
            NC_MODE_JOG = 7,

            /// <summary>Программа выполняется</summary>
            [Description("Программа выполняется")]
            NC_PROGRAM_RUN = 16,

            /// <summary>Обоснованный простой</summary>
            [Description("Обоснованный простой")]
            NC_DOWNTIME = 17,

            /// <summary>Счетчик деталей</summary>
            [Description("Счетчик деталей")]
            NC_PART_COUNTER = 18,

            /// <summary>Имя программы</summary>
            [Description("Имя программы")]
            NC_PROGRAM_NAME = 19,

            /// <summary>Обработка детали</summary>
            [Description("Обработка детали")]
            NC_PART_TIME = 20,

            /// <summary>Коррекция скорости</summary>
            [Description("Коррекция скорости")]
            NC_SPINDLE_OVERRIDE = 21,

            /// <summary>Коррекция подачи</summary>
            [Description("Коррекция подачи")]
            NC_FEEDRATE_OVERRIDE = 22,

            /// <summary>Ручное выполнение программы</summary>
            [Description("Ручное выполнение программы")]
            NC_MANUAL_PROGRAM_RUN = 23,

            /// <summary>Ручной режим под нагрузкой</summary>
            [Description("Ручной режим под нагрузкой")]
            NC_MANUAL_WIP = 24,

            /// <summary>Коррекция ускоренного хода</summary>
            [Description("Коррекция ускоренного хода")]
            NC_RAPID_OVERRIDE = 57,

            /// <summary>Текущий кадр</summary>
            [Description("Текущий кадр")]
            NC_CURRENT_BLOCK = 79,

            /// <summary>Текст текущего кадра</summary>
            [Description("Текст текущего кадра")]
            NC_CURRENT_BLOCK_TEXT = 80,

            /// <summary>Выработка</summary>
            [Description("Выработка")]
            NC_ECOMONIC_OUTPUT = 81,

            /// <summary>Номер инструмента</summary>
            [Description("Номер инструмента")]
            NC_TOOL_NUMBER = 82,

            /// <summary>Ошибки ПЛК</summary>
            [Description("Ошибки ПЛК")]
            NC_PLC_ERROR = 83,

            /// <summary>Ошибки ПЛК станкостроителя</summary>
            [Description("Ошибки ПЛК станкостроителя")]
            NC_MANUFACTURING_PLC_ERROR = 84,

            /// <summary>Готовность привода оси A</summary>
            [Description("Готовность привода оси A")]
            NC_AXIS_A_READY = 85,

            /// <summary>Готовность привода оси B</summary>
            [Description("Готовность привода оси B")]
            NC_AXIS_B_READY = 86,

            /// <summary>Готовность привода оси C</summary>
            [Description("Готовность привода оси C")]
            NC_AXIS_C_READY = 87,

            /// <summary>Индикация актуального состояния привода</summary>
            [Description("Индикация актуального состояния привода")]
            NC_AXIS_X_READY = 88,

            /// <summary>Готовность привода оси Y</summary>
            [Description("Готовность привода оси Y")]
            NC_AXIS_Y_READY = 89,

            /// <summary>Готовность привода оси Z</summary>
            [Description("Готовность привода оси Z")]
            NC_AXIS_Z_READY = 90,

            /// <summary>Причина простоя</summary>
            [Description("Причина простоя")]
            NC_IDLE_REASON = 91,

            /// <summary>Режим edit</summary>
            [Description("Режим edit")]
            NC_MODE_EDIT = 92,

            /// <summary>Оператор</summary>
            [Description("Оператор")]
            NC_OPERATOR = 93,

            /// <summary>Аварийное сообщение</summary>
            [Description("Аварийное сообщение")]
            NC_ALARM_MESSAGE = 94,

            /// <summary>Набор сигналов FMEA</summary>
            [Description("Набор сигналов FMEA")]
            NC_FMEA_SET = 95,

            /// <summary>Режим наладки в режиме mdi/jog/edit с вращением шпинделя</summary>
            [Description("Режим наладки в режиме mdi/jog/edit с вращением шпинделя")]
            NL_MDI_JOG_EDIT = 96,

            /// <summary>Режим наладки в режиме mdi/jog/edit с перемещением рабочего органа</summary>
            [Description("Режим наладки в режиме mdi/jog/edit с перемещением рабочего органа")]
            NL_MDI_JOG_EDIT_FEED = 97,

            /// <summary>Ремонт оборудования</summary>
            [Description("Ремонт оборудования")]
            DR_MAINTENANCE = 98,

            /// <summary>Наладка</summary>
            [Description("Наладка")]
            MS_NALADKA = 99,

            /// <summary>Подготовительное-заключительное время</summary>
            [Description("Подготовительное-заключительное время")]
            MS_PODGOTOVKA = 100,

            /// <summary>Нет оператора</summary>
            [Description("Нет оператора")]
            MS_NO_OPERATOR = 101,

            /// <summary>ТО и ППР</summary>
            [Description("ТО и ППР")]
            MS_PTO = 102,

            /// <summary>Поломка станка</summary>
            [Description("Поломка станка")]
            MS_REPAIR = 103,

            /// <summary>Плановый останов</summary>
            [Description("Плановый останов")]
            MS_PLAN_STOP = 104,

            /// <summary>Регламентные работы</summary>
            [Description("Регламентные работы")]
            MS_MACHINE_MAINTANCE = 105,

            /// <summary>Тмаш</summary>
            [Description("Тмаш")]
            MS_WORK = 106,

            /// <summary>Обед и перерывы</summary>
            [Description("Обед и перерывы")]
            MS_LUNCH_STOP = 107,

            /// <summary>Проверка детали</summary>
            [Description("Проверка детали")]
            MS_PART_CONTROL = 108,

            /// <summary>Имя финальной программы</summary>
            [Description("Имя финальной программы")]
            NC_FINAL_PROGRAM_NAME = 109,

            /// <summary>Общее время наработки</summary>
            [Description("Общее время наработки")]
            NC_MRO = 110,

            /// <summary>Обслуживание 1</summary>
            [Description("Обслуживание 1")]
            NC_MRO1 = 111,

            /// <summary>Обслуживание 2</summary>
            [Description("Обслуживание 2")]
            NC_MRO2 = 112,

            /// <summary>Обслуживание 3</summary>
            [Description("Обслуживание 3")]
            NC_MRO3 = 113,

            /// <summary>Потребность в ТОиР</summary>
            [Description("Потребность в ТОиР")]
            NC_MRO_REQUIRED = 114,

            /// <summary>Обозначение детали</summary>
            [Description("Обозначение детали")]
            NC_PART_NUMBER = 115,

            /// <summary>Работа по обслуживанию (ТОиР)</summary>
            [Description("Работа по обслуживанию (ТОиР)")]
            NC_MRO_TASK = 116,

            /// <summary>Скорость</summary>
            [Description("Скорость")]
            NC_SPEED = 117,

            /// <summary>Подача</summary>
            [Description("Подача")]
            NC_FEED = 118,

            /// <summary>Экстренный вызов</summary>
            [Description("Экстренный вызов")]
            NC_EMERGENCY_CALL = 119,

            /// <summary>Режим edit+подготовительное</summary>
            [Description("Режим edit+подготовительное")]
            NL_EDIT_PODGOTOVKA = 120,

            /// <summary>Количество годных</summary>
            [Description("Количество годных")]
            NC_PART_COUNTER_VALID = 121,

            /// <summary>Количество бракованных</summary>
            [Description("Количество бракованных")]
            NC_PART_COUNTER_DEFECT = 122,

            /// <summary>Вызов ОТК</summary>
            [Description("Вызов ОТК")]
            MS_OTK = 123,

            /// <summary>Приоритет Наладка</summary>
            [Description("Приоритет Наладка")]
            MS_PR_NALADKA = 124,

            /// <summary>Аварийная остановка выбрана с планшета</summary>
            [Description("Аварийная остановка выбрана с планшета")]
            NC_MANUAL_ALARM = 125,

            /// <summary>Аварийный ремонт</summary>
            [Description("Аварийный ремонт")]
            NC_AR = 126,

            /// <summary>Выходной день</summary>
            [Description("Выходной день")]
            NC_VD = 127,

            /// <summary>Замена (измерение) детали</summary>
            [Description("Замена (измерение) детали")]
            NC_ZD = 128,

            /// <summary>Личные нужды</summary>
            [Description("Личные нужды")]
            NC_PN = 129,

            /// <summary>Наладка станка</summary>
            [Description("Наладка станка")]
            NC_NS = 130,

            /// <summary>Обеденное время</summary>
            [Description("Обеденное время")]
            NC_OV = 131,

            /// <summary>Отработка программы</summary>
            [Description("Отработка программы")]
            NC_OP = 132,

            /// <summary>Отсутствие загрузки</summary>
            [Description("Отсутствие загрузки")]
            NC_OZ = 133,

            /// <summary>Отсутствие оператора</summary>
            [Description("Отсутствие оператора")]
            NC_OO = 134,

            /// <summary>ПЗО потери при запуске оборудования</summary>
            [Description("ПЗО потери при запуске оборудования")]
            NC_PZO = 135,

            /// <summary>Причина не указана</summary>
            [Description("Причина не указана")]
            DR_NO_REASON = 136,

            /// <summary>Самостоятельное обслуживание операторами</summary>
            [Description("Самостоятельное обслуживание операторами")]
            NC_SOO = 137,

            /// <summary>ТОиР</summary>
            [Description("ТОиР")]
            NC_TOiR = 138,

            /// <summary>Подготовительное</summary>
            [Description("Подготовительное")]
            CNC_PRE_WORK = 139,

            /// <summary>Вид брака</summary>
            [Description("Вид брака")]
            NC_DEFECT_TYPE = 140,

            /// <summary>Оборудование</summary>
            [Description("Оборудование")]
            NC_MACHINE = 141,

            /// <summary>Останов по М0</summary>
            [Description("Останов по М0")]
            NC_M0 = 142,

            /// <summary>Останов по М1</summary>
            [Description("Останов по М1")]
            NC_M1 = 143,

            /// <summary>G0 движение</summary>
            [Description("G0 движение")]
            NC_G0 = 144,

            /// <summary>Остановка подачи</summary>
            [Description("Остановка подачи")]
            NC_FEED_STOP = 145,

            /// <summary>Программа ЧПУ была изменена</summary>
            [Description("Программа ЧПУ была изменена")]
            NC_PROGRAM_CHANGED = 146,

            /// <summary>Ось A</summary>
            [Description("Ось A")]
            NC_AXIS_A = 147,

            /// <summary>Ось B</summary>
            [Description("Ось B")]
            NC_AXIS_B = 148,

            /// <summary>Ось C</summary>
            [Description("Ось C")]
            NC_AXIS_C = 149,

            /// <summary>Ось X</summary>
            [Description("Ось X")]
            NC_AXIS_X = 150,

            /// <summary>Ось Y</summary>
            [Description("Ось Y")]
            NC_AXIS_Y = 151,

            /// <summary>Ось Z</summary>
            [Description("Ось Z")]
            NC_AXIS_Z = 152,

            /// <summary>Сервисный вызов</summary>
            [Description("Сервисный вызов")]
            NC_SERVICE_CALL = 153,

            /// <summary>Технологический останов по M0/M1</summary>
            [Description("Технологический останов по M0/M1")]
            NC_M0M1 = 159,

            /// <summary>Нагрузка на шпиндель</summary>
            [Description("Нагрузка на шпиндель")]
            NC_LOAD_1 = 160,

            /// <summary>Режим работы ЧПУ</summary>
            [Description("Режим работы ЧПУ")]
            NC_CNC_MODE = 161
        }
    }
}
