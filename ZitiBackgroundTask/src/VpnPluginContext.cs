using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Networking.Vpn;

namespace NetFoundry.VPN
{
    public sealed class VpnPluginContext
    {
        internal static VpnPluginContext CURRENT_CONTEXT = new VpnPluginContext();

        public static VpnPluginContext GetActiveContext()
        {
            return CURRENT_CONTEXT;
        }
        public static void ResetActiveContext()
        {
            CURRENT_CONTEXT = new VpnPluginContext();
        }

        internal IReadOnlyList<HostName> assignedClientIPv4list = null;
        internal IReadOnlyList<HostName> assignedClientIPv6list = null;
        internal VpnDomainNameAssignment vpnDomainNameAssignmenta = new VpnDomainNameAssignment();
        internal VpnRouteAssignment vpnRouteAssignmenta = new VpnRouteAssignment { ExcludeLocalSubnets = false };

        internal const uint VPN_MTU = 0x4000;//0xFF00; //16 * 1024; //(2 << 15) - 1;

        //internal const uint VPN_MAX_FRAME = 0xFF00; // 16* 1024; // * 32; //(2 << 15) - 1;
        internal const uint VPN_MAX_FRAME = 0x00F0; // 16* 1024; // * 32; //(2 << 15) - 1;

        internal List<string> suffixes = new List<string>();
        internal List<string> fqdns = new List<string>();
        internal List<VpnRoute> routes = new List<VpnRoute>();
        internal List<string> ipsToCapture = new List<string>();

        private HostName dns { get; set; }
        public HostName DnsServer
        {
            get
            {
                return dns;
            }
            set
            {
                dns = value;
                //dnsServers.Clear();
                //dnsServers.Add(dns);
            }
        }

        public void addSuffix(string suffix)
        {
            suffixes.Add(suffix);
        }
        public void addFQDN(string suffix)
        {
            fqdns.Add(suffix);
        }
        /* Maybe implement this some day
        public void AddCIDR(string ip)
        {
            string[] parts = ip.Split('/');
            if (parts.Length != 2)
            {
                throw new Exception("Invalid CIDR format supplied.");
            }
            routes.Add(new VpnRoute(new HostName(parts[0]), Byte.Parse(parts[1])));
        }*/

        public void AddIP(string ip)
        {
            ipsToCapture.Add(ip);
            routes.Add(new VpnRoute(new HostName(ip), 32));
        }
    }
}
