using System;

namespace remeLog.Models
{
    public class CommandEntry
    {
        public Guid Id { get; init; }
        public string CommandType { get; init; } = string.Empty;
        public string TargetMachine { get; init; } = string.Empty;
        public string TargetUser { get; init; } = string.Empty;
        public string Payload { get; init; } = string.Empty;
        public DateTime CreatedUtc { get; init; }

        public string RecipientDisplay =>
            string.IsNullOrEmpty(TargetUser) ? TargetMachine : $"{TargetMachine}\\{TargetUser}";
    }
}
