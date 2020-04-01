using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;

namespace NetFoundry.IP
{
    internal class Packet
    {
        internal IpHeader ipHeader;
        internal byte[] bytes;

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

namespace ZitiBackgroundTask.Ziti
{
    public sealed class IpPacket
    {
        private NetFoundry.IP.Packet ipPacket;

        public IpPacket([ReadOnlyArray]byte[] networkBytes)
        {
            ipPacket = new NetFoundry.IP.Packet(networkBytes);
        }
    }
}