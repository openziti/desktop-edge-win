using System.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;


namespace ZitiBackgroundTask
{
    public enum Protocol
    {
        TCP = 6,
        UDP = 17,
        Unknown = -1
    };

    /// <summary>
    /// This class encapsulates all the IP header fields and provides a mechanism
    /// to set and get the details of them through a parameterized contructor
    /// and public properties respectively.
    /// </summary>
    class IpHeader
    {
        // Eight bits for version and header length.
        public byte _versionAndHeaderLength;
        // Eight bits for differentiated services (TOS).
        public byte _differentiatedServices;
        // Sixteen bits for total length of the datagram (header + message).
        public ushort _totalLength;
        // Sixteen bits for identification.
        public ushort _identification;
        // Eight bits for flags and fragmentation offset.
        public ushort _flagsAndOffset;
        // Eight bits for TTL (Time To Live).
        public byte _ttl;
        // Eight bits for the underlying protocol.
        public byte _protocol;
        // Sixteen bits containing the checksum of the header
        // (checksum can be negative so taken as short).
        public short _checksum;
        // Thirty two bit source IP Address.
        internal uint _sourceIPAddress;
        // Thirty two bit destination IP Address.
        internal uint _destinationIPAddress;
        // Header length.
        public byte _headerLength;
        // Data carried by the datagram.
        public byte[] _ipData = default;//new byte[2 << 15];

        public void WriteTo(TextWriter writer)
        {
            writer.WriteLine("_vionAndHeaderLength: " + _versionAndHeaderLength);
            writer.WriteLine("ByDifferentiatedServices: " + _differentiatedServices);
            writer.WriteLine("UsTotalLength: " + _totalLength);
            writer.WriteLine("UsIdentification: " + _identification);
            writer.WriteLine("usFlagsAndOffset: " + _flagsAndOffset);
            writer.WriteLine("_ttl: " + _ttl);
            writer.WriteLine("_protocol: " + _protocol);
            writer.WriteLine("SChecksum: " + _checksum);
            writer.WriteLine("UiSourceIPAddress: " + _sourceIPAddress);
            writer.WriteLine("UiDestinationIPAddress: " + _destinationIPAddress);
            writer.WriteLine("_headerLength: " + _headerLength);
            writer.WriteLine("As Hex: ");
            writer.WriteLine(BinaryVisualiser.FormatAsHex(Data, _totalLength));
            writer.Flush();
        }

        public void WriteTo(System.IO.Stream stream)
        {/*
            write(stream, _versionAndHeaderLength);
            write(stream, _differentiatedServices);
            write(stream, _totalLength);
            write(stream, _identification);
            write(stream, _fagsAndOffset);
            write(stream, _ttl);
            write(stream, _protocol);
            write(stream, SChecksum);
            write(stream, _sourceIPAddress);
            write(stream, _destinationIPAddress);
            write(stream, _headerLength);
            writeIpData(stream);*/

            System.IO.BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(_versionAndHeaderLength);
            writer.Write(_differentiatedServices);
            writer.Write(_totalLength);
            writer.Write(_identification);
            writer.Write(_flagsAndOffset);
            writer.Write(_ttl);
            writer.Write(_protocol);
            writer.Write(_checksum);
            writer.Write(_sourceIPAddress);
            writer.Write(_destinationIPAddress);
            writer.Write(_headerLength);
            writer.Write(_ipData, 0, MessageLength);
            writer.Flush();
        }
        /*
        private void write(BinaryWriter target, byte toWrite)
        {
            target.WriteByte(toWrite);
        }
        private void write(BinaryWriter target, ushort toWrite)
        {
            byte[] bytes = BitConverter.GetBytes(toWrite);
            target.Write(bytes, 0, bytes.Length);
        }
        private void write(BinaryWriter target, short toWrite)
        {
            byte[] bytes = BitConverter.GetBytes(toWrite);
            target.Write(bytes, 0, bytes.Length);
        }
        private void write(BinaryWriter target, uint toWrite)
        {
            byte[] bytes = BitConverter.GetBytes(toWrite);
            target.Write(bytes, 0, bytes.Length);
        }
        private void writeIpData(BinaryWriter target)
        {
            target.Write(ByIPData, 0, MessageLength);
        }*/

        private byte[] _ipPacket = new byte[2 << 15]; //65535 is the biggest packet possible

        private IpHeader()
        {
            //private use only
        }

        public IpHeader([System.Runtime.InteropServices.WindowsRuntime.ReadOnlyArray()]
            byte[] byBuffer, int nReceived) : this(byBuffer, 0, nReceived)
        {
        }

        public IpHeader(System.IO.Stream stream)
        {
            BinaryReader binaryReader = new BinaryReader(stream);
        }

        public IpHeader([System.Runtime.InteropServices.WindowsRuntime.ReadOnlyArray()]
            byte[] byBuffer, int position, int nReceived) :
            this(new BinaryReader(new MemoryStream(byBuffer, position, nReceived - position)))
        {/*
            // Create MemoryStream out of the received bytes.
            MemoryStream memoryStream = new MemoryStream(byBuffer, position, nReceived - position);
            // Next we create a BinaryReader out of the MemoryStream.
            BinaryReader binaryReader = new BinaryReader(memoryStream);

            return new IpHeader(binaryReader);*/
        }

        public IpHeader(BinaryReader binaryReader)
        {
            this.initialize(binaryReader);
        }

        private void initialize(BinaryReader binaryReader)
        {
            try
            {
                // The first eight bits of the IP header contain the version and
                // header length so we read them.
                _versionAndHeaderLength = binaryReader.ReadByte();
                // The next eight bits contain the Differentiated services.
                _differentiatedServices = binaryReader.ReadByte();
                // Next eight bits hold the total length of the datagram.
                _totalLength = (ushort)IPAddress.NetworkToHostOrder(
                                            binaryReader.ReadInt16());
                // Next sixteen have the identification bytes.
                _identification = (ushort)IPAddress.NetworkToHostOrder(
                                            binaryReader.ReadInt16());
                // Next sixteen bits contain the flags and fragmentation offset.
                _flagsAndOffset = (ushort)IPAddress.NetworkToHostOrder(
                                            binaryReader.ReadInt16());
                // Next eight bits have the TTL value.
                _ttl = binaryReader.ReadByte();
                // Next eight represnts the protocol encapsulated in the datagram.
                _protocol = binaryReader.ReadByte();
                // Next sixteen bits contain the checksum of the header.
                _checksum = IPAddress.NetworkToHostOrder(binaryReader.ReadInt16());
                // Next thirty two bits have the source IP address.
                _sourceIPAddress = (uint)(binaryReader.ReadInt32());
                // Next thirty two hold the destination IP address.
                _destinationIPAddress = (uint)(binaryReader.ReadInt32());
                // Now we calculate the header length.
                _headerLength = _versionAndHeaderLength;
                // The last four bits of the version and header length field contain the
                // header length, we perform some simple binary airthmatic operations to
                // extract them.
                _headerLength <<= 4;
                _headerLength >>= 4;
                // Multiply by four to get the exact header length.
                _headerLength *= 4;
                // Copy the data carried by the data gram into another array so that
                // according to the protocol being carried in the IP datagram
                _ipData = binaryReader.ReadBytes(_totalLength - _headerLength);
/*                Array.Copy(byBuffer, _headerLength, _ipData, 0,
                            _totalLength - _headerLength);*/
            }
            catch (Exception ex)
            {

                VPNHelper.LogLine(ex.Message);
            }
        }

        public byte[] IpPacket
        {
            get { return _ipPacket; }
        }

        public string Version
        {
            get
            {
                // Calculate the IP version. The four bits of the IP header
                // contain the IP version.
                if ((_versionAndHeaderLength >> 4) == 4)
                {
                    return "IP v4";
                }
                else if ((_versionAndHeaderLength >> 4) == 6)
                {
                    return "IP v6";
                }
                else
                {
                    return "Unknown";
                }
            }
        }

        public string HeaderLength
        {
            get
            {
                return _headerLength.ToString();
            }
        }

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
                return string.Format("0x{0:x2} ({1})", _differentiatedServices,
                                     _differentiatedServices);
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

        public ushort TotalLength
        {
            get
            {
                return _totalLength;
            }
        }

        public string Identification
        {
            get
            {
                return _identification.ToString();
            }
        }

        public byte[] Data
        {
            get
            {
                return _ipData;
            }
        }


        public string ToLoooongString()
        {
            StringBuilder b = new StringBuilder();
            b.Append("Checksum: ");
            b.Append(this.Checksum);
            //b.Append("Data: ");
            //b.Append(this.Data);
            b.Append(" DestinationAddress: ");
            b.Append(this.DestinationAddress);
            b.Append(" DifferentiatedServices: ");
            b.Append(this.DifferentiatedServices);
            b.Append(" Flags: ");
            b.Append(this.Flags);
            b.Append(" FragmentationOffset: ");
            b.Append(this.FragmentationOffset);
            b.Append(" HeaderLength: ");
            b.Append(this.HeaderLength);
            b.Append(" Identification: ");
            b.Append(this.Identification);
            b.Append(" MessageLength: ");
            b.Append(this.MessageLength);
            b.Append(" ProtocolType: ");
            b.Append(this.ProtocolType);
            b.Append(" ChecksumIP: ");
            b.Append(this.Checksum);
            b.Append(" SourceAddress: ");
            b.Append(this.SourceAddress);
            b.Append(" TTL: ");
            b.Append(this.TTL);
            b.Append(" TotalLength: ");
            b.Append(this.TotalLength);
            b.Append(" Version: ");
            b.Append(this.Version);

            return b.ToString();
        }


        public static IEnumerable<IpHeader> FromStream(Stream stream)
        {
            IpHeader header = new IpHeader();

            byte[] _ipPacket = header._ipPacket;
            int readResult = stream.Read(_ipPacket, 0, 4);
            while (readResult > 0)
            {
                byte[] pktBytes = new byte[2] { _ipPacket[2], _ipPacket[3] };
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(pktBytes);
                }

                ushort utotalIpPacketLen = BitConverter.ToUInt16(pktBytes, 0);

                int totalIpPacketLen = utotalIpPacketLen - 4;
                readResult = stream.Read(_ipPacket, 4, totalIpPacketLen);
                if (readResult > 0)
                {
                    using (MemoryStream ms = new MemoryStream(_ipPacket))
                    using (BinaryReader br = new BinaryReader(ms))
                    {
                        header.initialize(br);
                    }
                }

                yield return header;

                header = new IpHeader();
                _ipPacket = header._ipPacket;
                readResult = stream.Read(_ipPacket, 0, 4);
            }
        }

    }

    internal class Hex
    {
        public static byte[] StringToByteArrayFastest(string hex)
        {
            if (hex.Length % 2 == 1)
                throw new Exception("The binary key cannot have an odd number of digits");

            byte[] arr = new byte[hex.Length >> 1];

            for (int i = 0; i < hex.Length >> 1; ++i)
            {
                arr[i] = (byte)((GetHexVal(hex[i << 1]) << 4) + (GetHexVal(hex[(i << 1) + 1])));
            }

            return arr;
        }

        public static int GetHexVal(char hex)
        {
            int val = (int)hex;
            //For uppercase A-F letters:
            return val - (val < 58 ? 48 : 55);
            //For lowercase a-f letters:
            //return val - (val < 58 ? 48 : 87);
            //Or the two combined, but a bit slower:
            //return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }
    }
}
