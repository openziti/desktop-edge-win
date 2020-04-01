using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;

namespace NetFoundry.VPN.IP
{
    internal class Packet
    {
        internal IpHeader ipHeader;

        internal Packet() 
        {
            //for the subclasses...
        }

        public Packet([ReadOnlyArray]byte[] networkBytes)
        {
            ipHeader = new IpHeader(networkBytes);
        }
    }
}