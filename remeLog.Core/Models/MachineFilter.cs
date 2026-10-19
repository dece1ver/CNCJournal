using remeLog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Models
{
    public class MachineFilter : ObservableObject
    {
        public MachineFilter(string machine, string type, bool filter)
        {
            _Machine = machine;
            _Type = type;
            _Filter = filter;
        }

        private string _Machine;
        /// <summary> Название станка </summary>
        public string Machine
        {
            get => _Machine;
            set => Set(ref _Machine, value);
        }


        private string _Type;
        /// <summary> Тип </summary>
        public string Type
        {
            get => _Type;
            set => Set(ref _Type, value);
        }



        private bool _Filter;
        /// <summary> Фильтровать ли по данному станку </summary>
        public bool Filter
        {
            get => _Filter;
            set => Set(ref _Filter, value);
        }

    }
}
