using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.Vpn;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Networking;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.Background;
using System.Net.Sockets;
using System.Security.Cryptography;
using Buffer = System.Buffer;
using NetFoundry.VPN.IP;
using NetFoundry.VPN.Util;

namespace NetFoundry.VPN
{
    public sealed class ZitiVPNPlugin : IVpnPlugIn
    {
        internal static int DESIRED_PORT = 8900;
        internal static string DESIRED_HOST = "192.168.1.114";
        StreamSocket tcpTransport = null;

        VpnPluginContext vpnContext = VpnPluginContext.GetActiveContext();

        public ZitiVPNPlugin()
        {
            Task.Run(() =>
            {
                //start up a server that listens locally and just emits what it receives
                NetFoundry.VPN.Debugging.DebugTcpServer.Start();
            });
        }

        public void Connect(VpnChannel channel)
        {
            try
            {
                HostName tcpHostname = new HostName(DESIRED_HOST);
                string tcpPort = DESIRED_PORT.ToString();
                tcpTransport = new StreamSocket();
                tcpTransport.ConnectAsync(tcpHostname, tcpPort).AsTask().Wait(); //this will succeed... proving you CAN connect...
                tcpTransport.CancelIOAsync().AsTask().Wait();
                tcpTransport.Dispose(); //close/dispose of the connection

                tcpTransport = new StreamSocket();
                channel.AssociateTransport(tcpTransport, null);
                tcpTransport.ConnectAsync(tcpHostname, tcpPort).AsTask().Wait(); //fails with "no host is known". AssociateTransport is 'doing something'

                string desiredIP = "169.254.100.101";

                VpnDomainNameAssignment vpnDomainNameAssignment = new VpnDomainNameAssignment();

                List<HostName> dnsServers = new List<HostName>();
                dnsServers.Add(vpnContext.DnsServer);

                List<HostName> proxyServers = new List<HostName>();
                //proxyServers not used yet

                foreach (string suf in vpnContext.suffixes)
                {
                    addSuffix(vpnDomainNameAssignment, suf, dnsServers, proxyServers);
                }

                foreach (string fqdn in vpnContext.fqdns)
                {
                    addFQDN(vpnDomainNameAssignment, fqdn, dnsServers, proxyServers);
                }

                var vpnRouteAssignment = new VpnRouteAssignment { ExcludeLocalSubnets = false };
                var inclusionRoutes = vpnRouteAssignment.Ipv4InclusionRoutes;
                foreach (VpnRoute route in vpnContext.routes)
                {
                    LogHelper.LogLine("Adding an included route: " + route, channel);
                    inclusionRoutes.Add(route);
                }

                VpnInterfaceId vpnInterfaceId = null;

                channel.StartWithMainTransport(
                    new[] { new HostName(desiredIP) }, //this will only succeed if the ip is not 'localhost', 'hostname', '127.0.0.1' etc. must be 'off machine'
                    vpnContext.assignedClientIPv6list,
                    vpnInterfaceId,
                    vpnRouteAssignment,
                    vpnDomainNameAssignment,
                    VpnPluginContext.VPN_MTU,
                    VpnPluginContext.VPN_MAX_FRAME,
                    false,
                    tcpTransport);
            }
            catch (Exception e)
            {
                LogHelper.LogLine(e.Message);
            }
        }

        public void addSuffix(VpnDomainNameAssignment domainNameAssignment, string suffix, IEnumerable<HostName> dnsServers, IEnumerable<HostName> proxyServers)
        {
            var vdni1 = new VpnDomainNameInfo(suffix, VpnDomainNameType.Suffix, dnsServers, proxyServers);
            domainNameAssignment.DomainNameList.Add(vdni1);
        }
        public void addFQDN(VpnDomainNameAssignment domainNameAssignment, string suffix, IEnumerable<HostName> dnsServers, IEnumerable<HostName> proxyServers)
        {
            var vdni1 = new VpnDomainNameInfo(suffix, VpnDomainNameType.FullyQualified, dnsServers, proxyServers);
            domainNameAssignment.DomainNameList.Add(vdni1);
        }

        public async void Disconnect(VpnChannel channel)
        {
            VpnPluginContext.ResetActiveContext();
            try
            {
                await tcpTransport?.CancelIOAsync();
                tcpTransport?.Dispose();
            }catch(Exception e)
            {
                LogHelper.LogLine("bad stuff: " + e.Message);
            }
        }

        public void GetKeepAlivePayload(VpnChannel channel, out VpnPacketBuffer keepAlivePacket)
        {
            throw new NotImplementedException();
        }

        public void Encapsulate(VpnChannel channel, VpnPacketBufferList packets, VpnPacketBufferList encapulatedPackets)
        {
            while (packets?.Size > 0)
            {
                var packet = packets.RemoveAtBegin();
                VpnAppId packetAppId = packet.AppId;
                Windows.Storage.Streams.Buffer packetBuffer = packet.Buffer;
                DataReader fromBuffer = DataReader.FromBuffer(packetBuffer);
                uint fromBufferUnconsumedBufferLength = fromBuffer.UnconsumedBufferLength;
                byte[] bytes = new byte[fromBufferUnconsumedBufferLength];
                fromBuffer.ReadBytes(bytes);

                int bytesRead = bytes.Count();


                IpHeader ipHeader = new IpHeader(bytes, bytesRead);
                string sourcePort = "0";
                string destPort = "0";

                // Now according to the protocol being carried by the IP datagram we parse
                // the data field of the datagram.
                switch (ipHeader.Protocol)
                {
                    case Protocol.TCP:
                        TcpHeader tcpHeader = new TcpHeader(ipHeader.Data, ipHeader.MessageLength, ipHeader);

                        // If the port is equal to 53 then the underlying protocol is DNS.
                        // Note: DNS can use either TCP or UDP hence checking is done twice.
                        if (tcpHeader.DestinationPort == "53" || tcpHeader.SourcePort == "53")
                        {
                            //DnsHeader dnsHeader = new DnsHeader(ipHeader.Data, (int)ipHeader.MessageLength);
                            //dnsHeader.PrintDebug();
                            //LogLine(dnsHeader.ToString());
                        }

                        sourcePort = tcpHeader.SourcePort;
                        destPort = tcpHeader.DestinationPort;


                        LogHelper.LogLine("ENCAP: " + tcpHeader.ToShortString());
                        LogHelper.LogLine("ENCAP: " + tcpHeader.ToLoooongString());
                        //LogLine("ENCAP: " + BinaryVisualiser.FormatAsHex(ipHeader.Data, ipHeader.MessageLength));
                        LogHelper.LogLine("ENCAP: TOTAL BYTES:" + bytesRead + "\n" +
                                BinaryVisualiser.FormatAsHex(bytes, bytesRead));

                        break;
                    case Protocol.UDP:
                        UdpHeader udpHeader = new UdpHeader(ipHeader.Data, (int)ipHeader.MessageLength);
                        //TreeNode udpNode = MakeUDPTreeNode(udpHeader);
                        //rootNode.Nodes.Add(udpNode);
                        // If the port is equal to 53 then the underlying protocol is DNS.
                        // Note: DNS can use either TCP or UDP, thats the reason
                        // why the checking has been done twice.
                        if (udpHeader.DestinationPort == "53" || udpHeader.SourcePort == "53")
                        {
                            //DnsHeader dnsHeader = new DnsHeader(ipHeader.Data, (int)ipHeader.MessageLength);
                            //dnsHeader.PrintDebug();
                            //LogLine(dnsHeader.ToString());
                        }

                        sourcePort = udpHeader.SourcePort;
                        destPort = udpHeader.DestinationPort;
                        break;
                    default:
                        LogHelper.LogLine("THE PACKET WAS NOT TCP NOR UDP: Protocol = " + ipHeader.Protocol.GetName());
                        break;
                }

                string msg = null;


                if (vpnContext.ipsToCapture.Contains(ipHeader.DestinationAddress.ToString()))
                {
                    msg = string.Format("SUCCESS!!!! type:{0}. from {1}:{3} to {2}:{4}", ipHeader.Protocol,
                        ipHeader.SourceAddress, ipHeader.DestinationAddress, sourcePort, destPort);
                    encapulatedPackets.Append(packet);
                }
                else
                {
                    msg = string.Format("refusing to send {4} from {0}:{2} to {1}:{3}", ipHeader.SourceAddress,
                        ipHeader.DestinationAddress, sourcePort, destPort, ipHeader.Protocol);

                }

                LogHelper.LogLine(msg, channel);
            }
        }

        public void Decapsulate(VpnChannel channel, VpnPacketBuffer encapBuffer, VpnPacketBufferList decapsulatedPackets, VpnPacketBufferList controlPacketsToSend)
        {
            try
            {
                var buf = channel.GetVpnReceivePacketBuffer();


                if (encapBuffer.Buffer.Length > buf.Buffer.Capacity)
                {
                    LogHelper.LogLine(
                        "DROPPING PACKET!!! " + encapBuffer.Buffer.Length +
                        " bytes. This is bigger than the buffer capacity of: " + encapBuffer.Buffer.Length, channel);
                }
                else
                {
                    byte[] bytes = encapBuffer.Buffer.ToArray();

                    LogHelper.LogLine("DECAP: OVERALL PAYLOAD:" + bytes.Length + "\n" +
                            BinaryVisualiser.FormatAsHex(bytes, bytes.Length));


                    int srcOffset = 0;
                    foreach (IpHeader h in IpHeader.FromStream(new MemoryStream(bytes)))
                    {
                        if (buf == null)
                        {
                            buf = channel.GetVpnReceivePacketBuffer();
                        }

                        TcpHeader tcpHeader = new TcpHeader(h.Data, h.MessageLength, h);

                        LogHelper.LogLine("DECAP: " + tcpHeader.ToShortString());
                        LogHelper.LogLine("DECAP: " + tcpHeader.ToLoooongString());
                        LogHelper.LogLine("DECAP: TOTAL BYTES:" + h.TotalLength + "\n" +
                                BinaryVisualiser.FormatAsHex(h.IpPacket, h.TotalLength));

                        encapBuffer.Buffer.CopyTo((uint)srcOffset, buf.Buffer, 0, h.TotalLength);

                        buf.Buffer.Length = h.TotalLength;

                        decapsulatedPackets.Append(buf);
                        buf = null;
                        srcOffset += h.TotalLength;
                    }

                    if (encapBuffer != null) return;

                    int remainingBytes = (int)encapBuffer.Buffer.Length;
                    Span<byte> bytesAsSpan = new Span<byte>(bytes);
                    System.IO.Stream stream = encapBuffer.Buffer.AsStream();

                    while (remainingBytes > 0)
                    {
                        IpHeader ipHeader = new IpHeader(stream);

                        LogHelper.LogLine(ipHeader.ToLoooongString());
                        LogHelper.LogLine(BinaryVisualiser.FormatAsHex(bytes, ipHeader.TotalLength));

                        encapBuffer.Buffer.CopyTo((uint)srcOffset, buf.Buffer, 0, ipHeader.TotalLength);
                        buf.Buffer.Length = ipHeader.TotalLength;
                        decapsulatedPackets.Append(buf);

                        srcOffset += ipHeader.TotalLength;
                        remainingBytes -= ipHeader.TotalLength;

                        buf = channel.GetVpnReceivePacketBuffer(); //get a new buffer...
                    }

                    return; 
                }
            }
            catch (Exception e)
            {
                LogHelper.LogLine("exception???? " + e.Message);
            }
        }
    }
}