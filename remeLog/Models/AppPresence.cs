using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Models
{
    public sealed class AppPresence
    {
        public Guid SessionId { get; init; }
        public string MachineName { get; init; } = string.Empty;
        public string UserName { get; init; } = string.Empty;
        public string AppVersion { get; init; } = string.Empty;
        public DateTime StartedLocal { get; init; }
        public DateTime LastSeenLocal { get; init; }

        public bool IsOnline => (DateTime.Now - LastSeenLocal).TotalSeconds <= 30;
    }
}
