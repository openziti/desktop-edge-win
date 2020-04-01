using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NetFoundry.IP
{
    internal sealed class IpHeader
    {
        const ushort fragmentOffsetMask = 0b0001_1111_1111_1111; // 13 bits for fragment offset

        // The entire IP packet
        private byte[] networkBytes;

        public IpHeader(byte[] networkBytes)
        {
            this.networkBytes = networkBytes;

            using (MemoryStream ms = new MemoryStream(networkBytes))
            using (BinaryReader br = new BinaryReader(ms))
            {
                initialize(br);
            }
        }

        private void initialize(BinaryReader networkBytes)
        {
            try
            {
                // see https://tools.ietf.org/html/rfc791#page-11
                //  0                   1                   2                   3
                //  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |Version|  IHL  |Type of Service|          Total Length         |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |         Identification        |Flags|      Fragment Offset    |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |  Time to Live |    Protocol   |         Header Checksum       |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |                       Source Address                          |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |                    Destination Address                        |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                // |                    Options                    |    Padding    |
                // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                //
                //                  Example Internet Datagram Header

                // byte 1 - The first eight bits of the IP header contain the version and
                // header length. read the byte and then shift it to get the version
                // and header len
                byte versionAndHeaderLength = networkBytes.ReadByte();

                VersionAsInt = versionAndHeaderLength >> 4; // shift the byte to get the bits we want

                // Calculate the IP version. The four bits of the IP header
                // contain the IP version.
                if (VersionAsInt == 4)
                {
                    Version = "IP v4";
                }
                else if (VersionAsInt == 6)
                {
                    Version = "IP v6";
                }
                else
                {
                    Version = "Unknown";
                }

                // ip header length is contained in the last 4 bytes...
                //mask off the first 4 bytes and then per the spec the result
                //is a 4 bit field tells us the length of the IP header in 32 bit increments (4 bytes)
                HeaderLength = (versionAndHeaderLength & 0xF) * 4;

                // byte 2
                TypeOfService = networkBytes.ReadByte();

                // bytes 3, 4
                TotalLength = (ushort)IPAddress.NetworkToHostOrder(networkBytes.ReadInt16());

                // bytes 5, 6 - identification bytes.
                Identification = (ushort)IPAddress.NetworkToHostOrder(networkBytes.ReadInt16());

                // bytes 7, 8 - flags and fragment offset
                ushort flagsAndFragmentOffset = (ushort)IPAddress.NetworkToHostOrder(networkBytes.ReadInt16());
                Flags = (byte)(flagsAndFragmentOffset >> 13); // shift 13 bits to get the first 3
                FragmentOffset = (ushort)(flagsAndFragmentOffset & fragmentOffsetMask); //mask the lower 13 bits

                // byte 9
                TimeToLive = networkBytes.ReadByte();

                // byte 10
                Protocol = (Protocol)networkBytes.ReadByte();

                // byte 11, 12
                HeaderChecksum = (ushort)IPAddress.NetworkToHostOrder(networkBytes.ReadInt16());

                // bytes 13 - 16
                SourceAddress = new IPAddress(networkBytes.ReadBytes(4));

                // bytes 17 - 20
                DestinationAddress = new IPAddress(networkBytes.ReadBytes(4)); 
            }
            catch (Exception ex)
            {
                ZitiBackgroundTask.ZitiVPNPlugin.LogLine(ex.Message);
            }
        }

        // first 4 bits of the first byte. using int for convinience with other apis
        public int VersionAsInt { get; set; }
        public string Version { get; set; }

        // last 4 bits of the first byte is the total HeaderLength length. using int for convinience with other apis
        public int HeaderLength { get; set; }

        // 1 byte
        public byte TypeOfService { get; set; }

        // 2 bytes
        public ushort TotalLength { get; set; }

        // 2 bytes
        public ushort Identification { get; set; }

        // 3 bits - byte closest thing
        public byte Flags { get; set; }

        // 13 bits - ushort closest thing...
        public ushort FragmentOffset { get; set; }

        // 1 byte
        public int TimeToLive { get; set; }

        // 1 byte
        public Protocol Protocol { get; set; }

        // 2 bytes
        public ushort HeaderChecksum { get; set; }

        // 4 bytes
        public IPAddress SourceAddress { get; set; }

        // 4 bytes
        public IPAddress DestinationAddress { get; set; }

        // 4 bytes
        public byte[] Options { get; set; }

        /*
        public ushort MessageLength
        {
            get
            {
                // MessageLength = Total length of the datagram - Header length.
                return (ushort)(_totalLength - _headerLength);
            }
        }

        public string DifferentiatedServices
        {
            get
            {
                // Returns the differentiated services in hexadecimal format.
                return string.Format("0x{0:x2} ({1})", _differentiatedServices, _differentiatedServices);
            }
        }

        public string Flags
        {
            get
            {
                // The first three bits of the flags and fragmentation field 
                // represent the flags (which indicate whether the data is 
                // fragmented or not).
                int nFlags = _flagsAndOffset >> 13;
                if (nFlags == 2)
                {
                    return "Don't fragment";
                }
                else if (nFlags == 1)
                {
                    return "More fragments to come";
                }
                else
                {
                    return nFlags.ToString();
                }
            }
        }

        public string FragmentationOffset
        {
            get
            {
                // The last thirteen bits of the flags and fragmentation field 
                // contain the fragmentation offset.
                int nOffset = _flagsAndOffset << 3;
                nOffset >>= 3;
                return nOffset.ToString();
            }
        }

        public string TTL
        {
            get
            {
                return _ttl.ToString();
            }
        }

        public Protocol ProtocolType
        {
            get
            {
                // The protocol field represents the protocol in the data portion
                // of the datagram.
                if (_protocol == 6) // A value of six represents the TCP protocol.
                {
                    return Protocol.TCP;
                }
                else if (_protocol == 17)  // Seventeen for UDP.
                {
                    return Protocol.UDP;
                }
                else
                {
                    return Protocol.Unknown;
                }
            }
        }

        public string Checksum
        {
            get
            {
                // Returns the checksum in hexadecimal format.
                return string.Format("0x{0:x2}", _checksum);
            }
        }

        public IPAddress SourceAddress
        {
            get
            {
                return new IPAddress(_sourceIPAddress);
            }
        }

        public IPAddress DestinationAddress
        {
            get
            {
                return new IPAddress(_destinationIPAddress);
            }
        }

        public string Identification
        {
            get
            {
                return _identification.ToString();
            }
        }*/
    }
}
 