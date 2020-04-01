using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.Vpn;

namespace ZitiBackgroundTask.Ziti.Route
{ 
    internal class Intercept
    {
        public string ServiceName { get; set; }

        public string DomainName { get; set; }

        public VpnDomainNameType DomainType { get; set; }

        public int Port { get; set; }

    }
}
