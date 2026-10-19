using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Models
{
    public class WncObject
    {
        public WncObject(string name, string id, string link, string version, string state, string container, string type, string modifyDate, string createDate, string objectId, bool isCadDocument)
        {
            Name = name;
            Id = id;
            Link = link;
            Version = version;
            State = state;
            Container = container;
            Type = type;
            ModifyDate = modifyDate;
            CreateDate = createDate;
            ObjectId = objectId;
            IsCadDocument = isCadDocument;
        }

        public string Name { get; set; }
        public string Id { get; set; }
        public string Link { get; set; }

        /// <summary> Внутренний идентификатор объекта в Windchill (например, "OR:wt.epm.EPMDocument:531624000") — нужен для точечных запросов вроде поиска PDF (см. WindchillClient.FindPdfAsync). </summary>
        public string ObjectId { get; set; }

        /// <summary> Откуда пришёл объект: true — CADDocuments (CADDocumentMgmt), false — Documents (DocMgmt). Разные entity set — разный путь запроса по ObjectId. </summary>
        public bool IsCadDocument { get; set; }
        public string Version { get; set; }
        public string State { get; set; }
        public string PrettyState
        {
            get
            {
                return State switch
                {
                    "LITERA_A" => "Литера А",
                    "In Work" => "В работе",
                    "Released" => "Выпущено",
                    _ => State,
                };
            }
            
        }
        public string Container { get; set; }
        public string Type { get; set; }

        public string PrettyType
        {
            get
            {
                return Type switch
                {
                    "CAD Part" => "Модель",
                    "CAD Part Generic" => "Модель с семейством",
                    "CAD Part Instance" => "Модель из семейства",
                    "Assembly" => "Сборка",
                    "Assembly Generic" => "Сборка с семейством",
                    "Assembly Instance" => "Сборка из семейства",
                    "Drawing" => "Чертеж",
                    "NC Assembly" => "Сборка ЧПУ",
                    "Machine control data file" => "УП",
                    _ => Type,
                };
            }
        }
        public string ModifyDate { get; set; }
        public string CreateDate { get; set; }

        public override string ToString()
        {
            return $"Наименование: {Name}\n" +
                   $"Обозначение: {Id}\n" +
                   $"Версия: {Version}\n" +
                   $"Состояние: {State}\n" +
                   $"Контекст: {Container}\n" +
                   $"Тип: {Type}\n" +
                   $"Изменен: {ModifyDate}\n" +
                   $"Создан: {CreateDate}\n" +
                   $"Ссылка: {Link}\n";
        }
    }

}
