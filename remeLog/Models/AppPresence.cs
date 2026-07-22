using remeLog.Infrastructure.Types;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace remeLog.Models
{
    public sealed class AppPresence : INotifyPropertyChanged
    {
        public Guid SessionId { get; init; }
        public string MachineName { get; init; } = string.Empty;
        public string UserName { get; init; } = string.Empty;
        public string AppVersion { get; init; } = string.Empty;
        public string IpAddress { get; init; } = string.Empty;

        /// <summary>Битовая маска активных фич экземпляра.</summary>
        public int EnabledFeatures { get; init; }

        /// <summary>Человекочитаемый список фич экземпляра.</summary>
        public string FeaturesText
        {
            get
            {
                var names = ((RemeLogFeature)EnabledFeatures).Names();
                return names.Length > 0 ? string.Join(", ", names) : "—";
            }
        }

        public DateTime StartedLocal { get; init; }
        public DateTime LastSeenLocal { get; init; }

        public bool IsOnline => (DateTime.Now - LastSeenLocal).TotalSeconds <= 30;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
