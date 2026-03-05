using libeLog.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libeLog.Models
{
    public class ProductionTaskData : ViewModel
    {
        public string PartName { get; set; }

        public string Order { get; set; }

        public string PartsCount { get; set; }

        public string Date { get; set; }

        public string PlantComment { get; set; }

        public string Priority { get; set; }

        public string EngeneersComment { get; set; }

        public string SetupTechnician { get; set; }

        public string PdComment { get; set; }


        private string _NcProgramHref;
        /// <summary> Описание </summary>
        public string NcProgramHref
        {
            get => _NcProgramHref;
            set => Set(ref _NcProgramHref, value);
        }


        public bool NcProgramButtonEnabled => IsSelected && NcProgramHref != "-" && !string.IsNullOrEmpty(NcProgramHref);

        private bool _IsSelected;
        public bool IsSelected
        {
            get => _IsSelected;
            set
            {
                if (Set(ref _IsSelected, value))
                    OnPropertyChanged(nameof(NcProgramButtonEnabled));
            }
        }

        public ProductionTaskData(string partName, string order, string partsCount, string date, string plantComment, string priority, string engeneersComment, string setupTechnician, string pdComment, string ncProgramHref)
        {
            PartName = partName;
            Order = order;
            PartsCount = partsCount;
            Date = date;
            PlantComment = plantComment;
            Priority = priority;
            EngeneersComment = engeneersComment;
            SetupTechnician = setupTechnician;
            PdComment = pdComment;
            _NcProgramHref = ncProgramHref;
        }
    }
}
