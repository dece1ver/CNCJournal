using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QCTasks.Models
{
    public class QcUser
    {
        public int Id { get; set; }
        public string Code1C { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Patronymic { get; set; }
        public bool IsAdministrator { get; set; }

        public QcUser(int id, string code1C, string firstName, string lastName, string? patronymic, bool isAdministrator)
        {
            if (string.IsNullOrEmpty(code1C) || code1C.Length < 1) throw new ArgumentException("Код 1С некорректный: требуется как минимум 1 символ.", nameof(code1C));
            if (string.IsNullOrEmpty(firstName) || firstName.Length < 1) throw new ArgumentException("Имя некорректное: требуется как минимум 1 символ.", nameof(firstName));
            if (string.IsNullOrEmpty(lastName) || lastName.Length < 1) throw new ArgumentException("Фамилия некорректная: требуется как минимум 1 символ.", nameof(lastName));
            
            Id = id;
            Code1C = code1C;
            FirstName = firstName;
            LastName = lastName;
            Patronymic = patronymic ?? "";
            IsAdministrator = isAdministrator;
        }

        /// <summary>
        /// В формате Фамилия И.О. или Фамилия И. (при отсутствии отчёства)
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(Patronymic)
            ? $"{LastName} {GetInitial(FirstName)}."
            : $"{LastName} {GetInitial(FirstName)}. {GetInitial(Patronymic)}.";


        /// <summary>
        /// Фамилия Имя Отчёство (при наличии) полностью
        /// </summary>
        public string FullName => string.IsNullOrEmpty(Patronymic)
            ? $"{LastName} {FirstName}"
            : $"{LastName} {FirstName} {Patronymic}";

        private static string GetInitial(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 1)
                return "?";

            return value[..1].ToUpperInvariant();
        }
    }
}
