using remeLog.Core;
using System;
using System.ComponentModel;

namespace remeLog.Models
{
    public class OperatorInfo : ObservableObject, IDataErrorInfo
    {
        private string _originalFirstName;
        private string _originalLastName;
        private string _originalPatronymic;
        private int _originalQualification;
        private bool _originalIsActive;

        public OperatorInfo(int id, string firstName, string lastName, string patronymic, int qualification, bool isActive)
        {
            Id = id;
            _FirstName = firstName;
            _LastName = lastName;
            _Patronymic = patronymic;
            _Qualification = qualification;
            IsActive = isActive;
            _originalFirstName = _FirstName;
            _originalLastName = _LastName;
            _originalPatronymic = _Patronymic;
            _originalQualification = _Qualification;
            _originalIsActive = IsActive;
            IsModified = !IsOriginalState();
        }

        public OperatorInfo()
        {
            Id = -1;
            _FirstName = "";
            _LastName = "";
            _Patronymic = "";
            _Qualification = 0;
            IsActive = false;
            _originalFirstName = _FirstName;
            _originalLastName = _LastName;
            _originalPatronymic = _Patronymic;
            _originalQualification = _Qualification;
            _originalIsActive = IsActive;
        }

        private bool _IsModified;
        public bool IsModified
        {
            get => _IsModified;
            set => Set(ref _IsModified, value);
        }

        public int Id { get; set; }

        private string _FirstName;
        /// <summary> Имя </summary>
        public string FirstName
        {
            get => _FirstName;
            set
            {
                if (Set(ref _FirstName, value))
                {
                    IsModified = !IsOriginalState();
                }
            }
        }

        private string _LastName;
        /// <summary> Фамилия </summary>
        public string LastName
        {
            get => _LastName;
            set
            {
                if (Set(ref _LastName, value))
                {
                    IsModified = !IsOriginalState();
                }
            }
        }

        private string _Patronymic;
        /// <summary> Отчество </summary>
        public string Patronymic
        {
            get => _Patronymic;
            set
            {
                if (Set(ref _Patronymic, value))
                {
                    IsModified = !IsOriginalState();
                }
            }
        }

        private int _Qualification;
        /// <summary> Разряд </summary>
        public int Qualification
        {
            get => _Qualification;
            set
            {
                if (Set(ref _Qualification, value))
                {
                    IsModified = !IsOriginalState();
                }
            }
        }

        private bool _IsActive;
        public bool IsActive
        {
            get => _IsActive;
            set
            {
                if (Set(ref _IsActive, value))
                {
                    IsModified = !IsOriginalState();
                }
            }
        }

        public string FullName => $"{LastName} {FirstName} {Patronymic ?? ""}";
        public string DisplayName
        {
            get
            {
                var result = LastName;
                if (string.IsNullOrEmpty(FirstName)) return result;
                result += " " + FirstName[0] + ".";
                if (!string.IsNullOrEmpty(Patronymic))
                    result += " " + Patronymic[0] + ".";
                return result;
            }
        }

        public string this[string columnName]
        {
            get
            {
                string error = null!;
                switch (columnName)
                {
                    case nameof(FirstName):
                        if (string.IsNullOrWhiteSpace(FirstName))
                            error = "Имя оператора не может быть пустым.";
                        break;
                    case nameof(LastName):
                        if (string.IsNullOrWhiteSpace(LastName))
                            error = "Фамилия оператора не может быть пустой.";
                        break;
                    case nameof(Qualification):
                        if (Qualification < 0 || Qualification > 6)
                            error = "Разряд должен быть от 0 до 6.";
                        break;
                }
                return error;
            }
        }

        public string Error => null!;

        private bool IsOriginalState()
        {
            return FirstName == _originalFirstName && LastName == _originalLastName && Patronymic == _originalPatronymic && Qualification == _originalQualification && IsActive == _originalIsActive;
        }
    }
}
