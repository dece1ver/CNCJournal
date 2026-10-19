using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace remeLog.Models
{
    public class WncConfig
    {
        public WncConfig(string server, string user, string password, string localType)
        {
            Server = server;
            User = user;
            Password = password;
            LocalType = localType;
        }

        public string Server { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string LocalType { get; set; }
    }
}
