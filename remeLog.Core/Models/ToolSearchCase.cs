using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Models
{
    public class ToolSearchCase
    {
        public ToolSearchCase() { }

        public ToolSearchCase(string @operator, string part, string machine, string type, string description, DateTime startTime, DateTime endTime, bool? isSuccess)
        {
            Operator = @operator;
            Part = part;
            Machine = machine;
            Type = type;
            Description = description;
            StartTime = startTime;
            EndTime = endTime;
            IsSuccess = isSuccess;
        }

        public string Operator { get; set; }
        public string Part { get; set; }
        public string Machine { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Time => EndTime - StartTime;
        public bool? IsSuccess { get; set; }
    }
}
