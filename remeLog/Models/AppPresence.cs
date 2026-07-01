using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace remeLog.Models
{
    public sealed class AppPresence : INotifyPropertyChanged
    {
        public Guid SessionId { get; init; }
        public string MachineName { get; init; } = string.Empty;
        public string UserName { get; init; } = string.Empty;
        public string AppVersion { get; init; } = string.Empty;
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
