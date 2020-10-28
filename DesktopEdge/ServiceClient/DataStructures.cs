using System;
using System.Collections.Generic;

/// <summary>
/// These classes represent the data structures that are passed back and forth
/// between the service and the client.
/// </summary>
namespace ZitiDesktopEdge.ServiceClient
{
    class SvcResponse
    {
        public int Code { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }

        override public string ToString()
        {
            return $"Code: {Code}\nMessage: {Message}\nError: {Error}";
        }
    }

    class StatusUpdateResponse : SvcResponse
    {
        public StatusUpdate Payload { get; set; }
    }

    class StatusUpdate
    {
        public string Operation { get; set; }
        public TunnelStatus Status { get; set; }

        public Identity NewIdentity { get; set; }
    }

    public class NewIdentity
    {
        public EnrollmentFlags Flags { get; set; }
        public Identity Id { get; set; }
    }

    class IdentityResponse : SvcResponse
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
    public class SetLogLevelPayload
    {
        public string Level { get; set; }
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

    public class SetLogLevelFunction : ServiceFunction
    {
        public SetLogLevelFunction(string level)
        {
            this.Function = "SetLogLevel";
            this.Payload = new SetLogLevelPayload()
            {
                Level = level
            };
        }
        public SetLogLevelPayload Payload { get; set; }
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
        public long Up { get; set; }
        public long Down { get; set; }
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
        public string InterceptHost { get; set; }
        public UInt16 InterceptPort { get; set; }
        public bool OwnsIntercept { get; set; }
        public string AssignedIP { get; set; }
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

    class ServiceVersion
    {
        public string Version { get; set; }
        public string Revision { get; set; }
        public string BuildDate { get; set; }

        public override string ToString()
        {
            return $"Version: {Version}, Revision: {Revision}, BuildDate: {BuildDate}";
        }
    }

    class ZitiTunnelStatus : SvcResponse
    {
        public TunnelStatus Status { get; set; }
    }

    class TunnelStatus
    {
        public bool Active { get; set; }

        public long Duration { get; set; }

        public List<Identity> Identities { get; set; }

        public IpInfo IpInfo { get; set; }

        public string LogLevel { get; set; }

        public ServiceVersion ServiceVersion { get; set; }

        public void Dump(System.IO.TextWriter writer)
        {
            try {
                writer.WriteLine($"Tunnel Active: {Active}");
                writer.WriteLine($"     LogLevel         : {LogLevel}");
                writer.WriteLine($"     EvaluatedLogLevel: {EvaluateLogLevel()}");
                foreach (Identity id in Identities) {
                    writer.WriteLine($"  FingerPrint: {id.FingerPrint}");
                    writer.WriteLine($"    Name    : {id.Name}");
                    writer.WriteLine($"    Active  : {id.Active}");
                    writer.WriteLine($"    Status  : {id.Status}");
                    writer.WriteLine($"    Services:");
                    if (id.Services != null)
                    {
                        foreach (Service s in id?.Services)
                        {
                            writer.WriteLine($"      Name: {s.Name} HostName: {s.InterceptHost} Port: {s.InterceptPort}");
                        }
                    }
                    writer.WriteLine("=============================================");
                }
            } catch (Exception e) {
                if (writer!=null) writer.WriteLine(e.ToString());
            }   
        
        }

        public LogLevelEnum EvaluateLogLevel()
        {
            try
            {
                LogLevelEnum l = (LogLevelEnum) Enum.Parse(typeof(LogLevelEnum), LogLevel.ToUpper());
                return l;
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine("Could not parse response LogLevel from sevice - guessing INFO? " + LogLevel);
                return LogLevelEnum.INFO;
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

    class StatusEvent
    {
        public string Op { get; set; }
    }

    class ActionEvent : StatusEvent
    {
        public string Action { get; set; }
    }

    class TunnelStatusEvent : StatusEvent
    {
        public TunnelStatus Status { get; set; }
        public int ApiVersion { get; set; }
    }

    class MetricsEvent : StatusEvent
    {
        public List<Identity> Identities { get; set; }
    }

    class ServiceEvent : ActionEvent
    {
        public string Fingerprint { get; set; }
        public Service Service { get; set; }
    }

    class IdentityEvent : ActionEvent
    {
        public Identity Id { get; set; }
    }
}