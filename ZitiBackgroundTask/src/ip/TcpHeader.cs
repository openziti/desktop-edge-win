using System.Net;
using System;
using System.IO;
using System.Text;
using NetFoundry.VPN.Util;

namespace NetFoundry.VPN.IP
{
    /// <summary>
    /// This class encapsulates all the TCP header fields and provides a mechanism
    /// to set and get the details of them through a parameterized contructor
    /// and public properties respectively.
    /// </summary>
    sealed class TcpHeader
    {
        private IpHeader ipHeader = null;

        // Sixteen bits for the source port number.
        private ushort _sourcePort;
        // Sixteen bits for the destination port number.
        private ushort _destinationPort;
        // Thirty two bits for the sequence number.
        private uint _sequenceNumber = 555;
        // Thirty two bits for the acknowledgement number.
        private uint _acknowledgementNumber = 555;
        // Sixteen bits for flags and data offset.
        private ushort _dataOffsetAndFlags = 555;
        // Sixteen bits for the window size.
        private ushort _window = 555;
        // Sixteen bits for the checksum, (checksum can be negative so taken as short).
        private short _checksum = 555;
        // Sixteen bits for the urgent pointer.  
        private ushort _urgentPointer;
        // Header length.
        private byte _headerLength;
        // Length of the data being carried.
        private ushort _messageLength;
        // Data carried by the TCP packet.
        private byte[] _tcpData = new byte[2 << 15];


        internal void WriteTo(System.IO.Stream stream)
        {
            
        }

        public TcpHeader([System.Runtime.InteropServices.WindowsRuntime.ReadOnlyArray()] byte[] byBuffer, int nReceived, IpHeader ipHeader)
        {
            this.ipHeader = ipHeader;
            try
            {
                // Create MemoryStream out of the received bytes.
                MemoryStream memoryStream = new MemoryStream(byBuffer, 0, nReceived);
                // Next we create a BinaryReader out of the MemoryStream.
                BinaryReader binaryReader = new BinaryReader(memoryStream);
                // The first sixteen bits contain the source port.
                _sourcePort = (ushort)IPAddress.NetworkToHostOrder(binaryReader.ReadInt16());
                // The next sixteen contain the destiination port.
                _destinationPort = (ushort)IPAddress.NetworkToHostOrder(binaryReader.ReadInt16());
                // Next thirty two have the sequence number.
                _sequenceNumber = (uint)IPAddress.NetworkToHostOrder(binaryReader.ReadInt32());
                // Next thirty two have the acknowledgement number.
                _acknowledgementNumber = (uint)IPAddress.NetworkToHostOrder(binaryReader.ReadInt32());
                // The next sixteen bits hold the flags and the data offset.
                _dataOffsetAndFlags = (ushort)IPAddress.NetworkToHostOrder(binaryReader.ReadInt16());
                // The next sixteen contain the window size.
                _window = (ushort)IPAddress.NetworkToHostOrder(binaryReader.ReadInt16());
                // In the next sixteen we have the checksum.
                _checksum = (short)IPAddress.NetworkToHostOrder(binaryReader.ReadInt16());
                // The following sixteen contain the urgent pointer.
                _urgentPointer = (ushort)IPAddress.NetworkToHostOrder(binaryReader.ReadInt16());
                // The data offset indicates where the data begins, so using it we
                // calculate the header length.
                _headerLength = (byte)(_dataOffsetAndFlags >> 12);
                _headerLength *= 4;
                // Message length = Total length of the TCP packet - Header length.
                _messageLength = (ushort)(nReceived - _headerLength);
                // Copy the TCP data into the data buffer.
                Array.Copy(byBuffer, _headerLength, _tcpData, 0,
                                        nReceived - _headerLength);
            }
            catch (Exception ex)
            {
                LogHelper.LogLine(ex.Message);
            }
        }

        public string ToShortString()
        {
            string src = ipHeader.SourceAddress.MapToIPv4().ToString();
            string dst = ipHeader.DestinationAddress.MapToIPv4().ToString();

            return string.Format("tcp from {0}:{1} to {2}:{3}. payload: {4}. ", src, SourcePort, dst, DestinationPort, MessageLength);
        }

        public string ToLoooongString()
        {
            StringBuilder b = new StringBuilder();
            //b.Append(ipHeader.ToLoooongString());
            b.Append(" SequenceNumber: ");
            b.Append(this.SequenceNumber);
            b.Append(" AckNo: ");
            b.Append(this.AcknowledgementNumber);
            b.Append(" ChecksumTCP: ");
            b.Append(this.Checksum);
            //b.Append("Data: ");
            //b.Append(this.Data);
            b.Append(" DestinationPort: ");
            b.Append(this.DestinationPort);
            b.Append(" Flags: ");
            b.Append(this.Flags);
            b.Append(" HeaderLength: ");
            b.Append(this.HeaderLength);
            b.Append(" MessageLength: ");
            b.Append(this.MessageLength);
            b.Append(" SourcePort: ");
            b.Append(this.SourcePort);
            b.Append(" UrgentPointer: ");
            b.Append(this.UrgentPointer);
            b.Append(" WindowSize: ");
            b.Append(this.WindowSize);

            return b.ToString();
        }
        
        public void WriteTo(TextWriter writer)
        {/*
            writer.WriteLine("ByVersionAndHeaderLength: " + ByVersionAndHeaderLength);
            writer.WriteLine("ByDifferentiatedServices: " + ByDifferentiatedServices);
            writer.WriteLine("UsTotalLength: " + UsTotalLength);
            writer.WriteLine("UsIdentification: " + UsIdentification);
            writer.WriteLine("usFlagsAndOffset: " + usFlagsAndOffset);
            writer.WriteLine("ByTTL: " + ByTTL);
            writer.WriteLine("ByProtocol: " + ByProtocol);
            writer.WriteLine("SChecksum: " + SChecksum);
            writer.WriteLine("UiSourceIPAddress: " + _sourceIPAddress);
            writer.WriteLine("UiDestinationIPAddress: " + UiDestinationIPAddress);
            writer.WriteLine("ByHeaderLength: " + ByHeaderLength);
            writer.WriteLine("As Hex: ");
            writer.WriteLine(BinaryVisualiser.FormatAsHex(Data, UsTotalLength));*/
            writer.WriteLine("NOT IMPLEMENTED YET");
            writer.Flush();
        }



        public string SourcePort
        {
            get
            {
                return _sourcePort.ToString();
            }
        }

        public string DestinationPort
        {
            get
            {
                return _destinationPort.ToString();
            }
        }

        public string SequenceNumber
        {
            get
            {
                return _sequenceNumber.ToString();
            }
        }

        public string AcknowledgementNumber
        {
            get
            {
                // If the ACK flag is set then only we have a valid value in the
                // acknowlegement field, so check for it beore returning anything.
                if ((_dataOffsetAndFlags & 0x10) != 0)
                {
                    return _acknowledgementNumber.ToString();
                }
                else
                {
                    return "";
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

        public string WindowSize
        {
            get
            {
                return _window.ToString();
            }
        }

        public string UrgentPointer
        {
            get
            {
                // If the URG flag is set then only we have a valid value in the urgent
                // pointer field, so check for it beore returning anything.
                if ((_dataOffsetAndFlags & 0x20) != 0)
                {
                    return _urgentPointer.ToString();
                }
                else
                {
                    return "";
                }
            }
        }

        public string Flags
        {
            get
            {
                // The last six bits of data offset and flags contain the control bits.
                // First we extract the flags.
                int nFlags = _dataOffsetAndFlags & 0x3F;
                string strFlags = string.Format("0x{0:x2} (", nFlags);
                // Now we start looking whether individual bits are set or not.
                if ((nFlags & 0x01) != 0)
                {
                    strFlags += "FIN, ";
                }
                if ((nFlags & 0x02) != 0)
                {
                    strFlags += "SYN, ";
                }
                if ((nFlags & 0x04) != 0)
                {
                    strFlags += "RST, ";
                }
                if ((nFlags & 0x08) != 0)
                {
                    strFlags += "PSH, ";
                }
                if ((nFlags & 0x10) != 0)
                {
                    strFlags += "ACK, ";
                }
                if ((nFlags & 0x20) != 0)
                {
                    strFlags += "URG";
                }
                strFlags += ")";

                if (strFlags.Contains("()"))
                {
                    strFlags = strFlags.Remove(strFlags.Length - 3);
                }
                else if (strFlags.Contains(", )"))
                {
                    strFlags = strFlags.Remove(strFlags.Length - 3, 2);
                }
                return strFlags;
            }
        }

        public string Checksum
        {
            get
            {
                // Return the checksum in hexadecimal format.
                return string.Format("0x{0:x2}", _checksum);
            }
        }

        public byte[] Data
        {
            get
            {
                return _tcpData;
            }
        }

        public ushort MessageLength
        {
            get
            {
                return _messageLength;
            }
        }
    }
}