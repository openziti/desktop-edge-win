using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetFoundry.VPN.IP
{
    internal static class ProtocolExtensionMethod
    {
        internal static string GetName(this Protocol theEnum)
        {
            return Enum.GetName(typeof(Protocol), theEnum);
        }
    }

    internal enum Protocol
    {
        //see https://en.wikipedia.org/wiki/List_of_IP_protocol_numbers
        Unknown = -1,
        HOPOPT = 0, //IPv6 Hop-by-Hop Option
        ICMP = 1, //Internet Control Message Protocol
        IGMP = 2, //Internet Group Management Protocol
        GGP = 3, //Gateway-to-Gateway Protocol
        IP_in_IP = 4, //IP in IP (encapsulation)
        ST = 5, //Internet Stream Protocol
        TCP = 6, //Transmission Control Protocol
        CBT = 7, //Core-based trees
        EGP = 8, //Exterior Gateway Protocol
        IGP = 9, //Interior Gateway Protocol (any private interior gateway (used by Cisco for their IGRP))
        BBN_RCC_MON = 10, //BBN RCC Monitoring
        NVP_II = 11, //Network Voice Protocol
        PUP = 12, //Xerox PUP
        ARGUS = 13, //ARGUS
        EMCON = 14, //EMCON
        XNET = 15, //Cross Net Debugger
        CHAOS = 16, //Chaos
        UDP = 17, //User Datagram Protocol
        MUX = 18, //Multiplexing
        DCN_MEAS = 19, //DCN Measurement Subsystems
        HMP = 20, //Host Monitoring Protocol
        PRM = 21, //Packet Radio Measurement
        XNS_IDP = 22, //XEROX NS IDP
        TRUNK_1 = 23, //Trunk-1
        TRUNK_2 = 24, //Trunk-2
        LEAF_1 = 25, //Leaf-1
        LEAF_2 = 26, //Leaf-2
        RDP = 27, //Reliable Data Protocol
        IRTP = 28, //Internet Reliable Transaction Protocol
        ISO_TP4 = 29, //ISO Transport Protocol Class 4
        NETBLT = 30, //Bulk Data Transfer Protocol
        MFE_NSP = 31, //MFE Network Services Protocol
        MERIT_INP = 32, //MERIT Internodal Protocol
        DCCP = 33, //Datagram Congestion Control Protocol
        THIRD_PARTY_CONNECT = 34, //Third Party Connect Protocol
        IDPR = 35, //Inter-Domain Policy Routing Protocol
        XTP = 36, //Xpress Transport Protocol
        DDP = 37, //Datagram Delivery Protocol
        IDPR_CMTP = 38, //IDPR Control Message Transport Protocol
        TPplusplus = 39, //TP++ Transport Protocol
        IL = 40, //IL Transport Protocol
        IPv6 = 41, //IPv6 Encapsulation
        SDRP = 42, //Source Demand Routing Protocol
        IPv6_Route = 43, //Routing Header for IPv6
        IPv6_Frag = 44, //Fragment Header for IPv6
        IDRP = 45, //Inter-Domain Routing Protocol
        RSVP = 46, //Resource Reservation Protocol
        GREs = 47, //Generic Routing Encapsulation
        DSR = 48, //Dynamic Source Routing Protocol
        BNA = 49, //Burroughs Network Architecture
        ESP = 50, //Encapsulating Security Payload
        AH = 51, //Authentication Header
        I_NLSP = 52, //Integrated Net Layer Security Protocol
        SwIPe = 53, //SwIPe
        NARP = 54, //NBMA Address Resolution Protocol
        MOBILE = 55, //IP Mobility (Min Encap)
        TLSP = 56, //Transport Layer Security Protocol (using Kryptonet key management)
        SKIP = 57, //Simple Key-Management for Internet Protocol
        IPv6_ICMP = 58, //ICMP for IPv6
        IPv6_NoNxt = 59, //No Next Header for IPv6
        IPv6_Opts = 60, //Destination Options for IPv6
        HOST_INTERNAL_PROTOCOL = 61, //Any host internal protocol
        CFTP = 62, //CFTP
        LOCAL_NETWORK = 63, //Any local network
        SAT_EXPAK = 64, //SATNET and Backroom EXPAK
        KRYPTOLAN = 65, //Kryptolan
        RVD = 66, //MIT Remote Virtual Disk Protocol
        IPPC = 67, //Internet Pluribus Packet Core
        DISTRIBUTED_FS = 68, //Any distributed file system
        SAT_MON = 69, //SATNET Monitoring
        VISA = 70, //VISA Protocol
        IPCU = 71, //Internet Packet Core Utility
        CPNX = 72, //Computer Protocol Network Executive
        CPHB = 73, //Computer Protocol Heart Beat
        WSN = 74, //Wang Span Network
        PVP = 75, //Packet Video Protocol
        BR_SAT_MON = 76, //Backroom SATNET Monitoring
        SUN_ND = 77, //SUN ND PROTOCOL-Temporary
        WB_MON = 78, //WIDEBAND Monitoring
        WB_EXPAK = 79, //WIDEBAND EXPAK
        ISO_IP = 80, //International Organization for Standardization Internet Protocol
        VMTP = 81, //Versatile Message Transaction Protocol
        SECURE_VMTP = 82, //Secure Versatile Message Transaction Protocol
        VINES = 83, //VINES
        TTP = 84, //TTP
        IPTM = 84, //Internet Protocol Traffic Manager
        NSFNET_IGP = 85, //NSFNET-IGP
        DGP = 86, //Dissimilar Gateway Protocol
        TCF = 87, //TCF
        EIGRP = 88, //EIGRP
        OSPF = 89, //Open Shortest Path First
        Sprite_RPC = 90, //Sprite RPC Protocol
        LARP = 91, //Locus Address Resolution Protocol
        MTP = 92, //Multicast Transport Protocol
        AX_25 = 93, //AX.25
        OS = 94, //KA9Q NOS compatible IP over IP tunneling
        MICP = 95, //Mobile Internetworking Control Protocol
        SCC_SP = 96, //Semaphore Communications Sec. Pro
        ETHERIP = 97, //Ethernet-within-IP Encapsulation
        ENCAP = 98, //Encapsulation Header
        PRIVATE_ENCRYPTION = 99, //Any private encryption scheme
        GMTP = 100, //GMTP
        IFMP = 101, //Ipsilon Flow Management Protocol
        PNNI = 102, //PNNI over IP
        PIM = 103, //Protocol Independent Multicast
        ARIS = 104, //IBM's ARIS (Aggregate Route IP Switching) Protocol
        SCPS = 105, //SCPS (Space Communications Protocol Standards)
        QNX = 106, //QNX
        ACTIVE_NETWORKS = 107, //Active Networks
        IPComp = 108, //IP Payload Compression Protocol
        SNP = 109, //Sitara Networks Protocol
        Compaq_Peer = 110, //Compaq Peer Protocol
        IPX_in_IP = 111, //IPX in IP
        VRRP = 112, //Virtual Router Redundancy Protocol, Common Address Redundancy Protocol (not IANA assigned)
        PGM = 113, //PGM Reliable Transport Protocol
        ANY_HOP = 114, //Any 0-hop protocol
        L2TP = 115, //Layer Two Tunneling Protocol Version 3
        DDX = 116, //D-II Data Exchange (DDX)
        IATP = 117, //Interactive Agent Transfer Protocol
        STP = 118, //Schedule Transfer Protocol
        SRP = 119, //SpectraLink Radio Protocol
        UTI = 120, //Universal Transport Interface Protocol
        SMP = 121, //Simple Message Protocol
        SM = 122, //Simple Multicast Protocol
        PTP = 123, //Performance Transparency Protocol
        IS_IS_over_IPv4 = 124, //Intermediate System to Intermediate System (IS-IS) Protocol over IPv4
        FIRE = 125, //Flexible Intra-AS Routing Environment
        CRTP = 126, //Combat Radio Transport Protocol
        CRUDP = 127, //Combat Radio User Datagram
        SSCOPMCE = 128, //Service-Specific Connection-Oriented Protocol in a Multilink and Connectionless Environment
        IPLT = 129, //
        SPS = 130, //Secure Packet Shield
        PIPE = 131, //Private IP Encapsulation within IP
        SCTP = 132, //Stream Control Transmission Protocol
        FC = 133, //Fibre Channel
        RSVP_E2E_IGNORE = 134, //Reservation Protocol (RSVP) End-to-End Ignore
        Mobility_Header = 135, //Mobility Extension Header for IPv6
        UDPLite = 136, //Lightweight User Datagram Protocol
        MPLS_in_IP = 137, //Multiprotocol Label Switching Encapsulated in IP
        manet = 138, //MANET Protocols
        HIP = 139, //Host Identity Protocol
        Shim6 = 140, //Site Multihoming by IPv6 Intermediation
        WESP = 141, //Wrapped Encapsulating Security Payload
        ROHC = 142, //Robust Header Compression

        //Unassigned = 143 - 252, //
        //Use for experimentation and testing = 253 - 254, //
        Reserved = 255, //
    };
}