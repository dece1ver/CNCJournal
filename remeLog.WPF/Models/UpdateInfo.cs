using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using libeLog.Infrastructure;

namespace remeLog.Models
{
    public class UpdateInfo
    {
        public string Message { get; set; }
        public Status? Icon { get; set; }

        public UpdateInfo(string message, Status? icon = null)
        {
            Message = message;
            Icon = icon;
        }
    }
}
