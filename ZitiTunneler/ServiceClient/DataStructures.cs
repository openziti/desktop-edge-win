using System;
using System.Collections.Generic;

/// <summary>
/// These classes represent the data structures that are passed back and forth
/// between the service and the client.
/// </summary>
namespace ZitiTunneler.ServiceClient
{
    class SvcResponse
    {
        public int Code;
        public string Message;
        public string Error;

        public string ToString()
        {
            return $"Code: {Code}\nMessage: {Message}\nError: {Error}";
        }
    }

    public class NewIdentity
    {
        public EnrollmentFlags Flags { get; set; }
        public Identity Id { get; set; }
    }

    class NewIdentityResponse : SvcResponse
    {
        public Identity Payload { get; set; }
    }

    public class ServiceFunction
    {
        public string Function { get; set; }
    }

    public class FingerprintFunction : ServiceFunction
    {
        public FingerprintPayload Payload { get; set; }
    }

    public class BooleanPayload
    {
        public bool OnOff { get; set; }
    }

    public class BooleanFunction : ServiceFunction
    {
        public BooleanFunction(string functionName, bool theBool)
        {
            this.Function = functionName;
            this.Payload = new BooleanPayload() { OnOff = theBool };
        }
        public BooleanPayload Payload { get; set; }
    }

    public class IdentityTogglePayload
    {
        public bool OnOff { get; set; }
        public string Fingerprint { get; set; }
    }

    public class IdentityToggleFunction : ServiceFunction
    {
        public IdentityToggleFunction(string fingerprint, bool theBool)
        {
            this.Function = "IdentityOnOff";
            this.Payload = new IdentityTogglePayload()
            {
                OnOff = theBool,
                Fingerprint = fingerprint
            };
        }
        public IdentityTogglePayload Payload { get; set; }
    }

    public class FingerprintPayload
    {
        public string Fingerprint { get; set; }
    }

    public class Id
    {
        public string key { get; set; }
        public string cert { get; set; }
    }

    public class Config
    {
        public string ztAPI { get; set; }
        public Id id { get; set; }
        public object configTypes { get; set; }
    }

    public class Metrics
    {
        public int TotalBytes { get; set; }
    }

    public class Identity
    {
        public string Name { get; set; }
        public string FingerPrint { get; set; }
        public bool Active { get; set; }
        public Config Config { get; set; }
        public string Status { get; set; }
        public List<Service> Services { get; set; }
        public Metrics Metrics { get; set; }
    }
    public class Service
    {
        public string Name { get; set; }
        public string HostName { get; set; }
        public UInt16 Port { get; set; }
    }

    public class EnrollmentFlags
    {
        public string JwtString { get; set; }
        public string CertFile { get; set; }
        public string KeyFile { get; set; }
        public string AdditionalCAs { get; set; }
    }

    class IpInfo
    {
        public string Ip { get; set; }
        public string Subnet { get; set; }
        public UInt16 MTU { get; set; }
        public string DNS { get; set; }
    }

    class ZitiTunnelStatus : SvcResponse
    {
        public bool TunnelActive { get; set; }
        public List<Identity> Identities { get; set; }
        
        public IpInfo IpInfo { get; set; }

        public void Dump(System.IO.TextWriter writer)
        {
            writer.WriteLine($"Tunnel Active: {TunnelActive}");
            foreach (Identity id in Identities)
            {
                writer.WriteLine($"  FingerPrint: {id.FingerPrint}");
                writer.WriteLine($"    Name: {id.Name}");
                writer.WriteLine($"    Active: {id.Active}");
                writer.WriteLine($"    Status: {id.Status}");
                writer.WriteLine($"    Services:");
                foreach (Service s in id.Services)
                {
                    writer.WriteLine($"      Name: {s.Name} HostName: {s.HostName} Port: {s.Port}");
                }
                writer.WriteLine("=============================================");
            }
        }
    }

    public class ServiceException : System.Exception
    {
        public ServiceException(string Message, int Code, string AdditionalInfo) : base(Message)
        {
            this.Code = Code;
            this.AdditionalInfo = AdditionalInfo;
        }

        public int Code { get; }
        public string AdditionalInfo { get; }
    }
}