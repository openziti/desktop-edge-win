using System;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// These classes represent the data structures that are passed back and forth
/// between the service and the client.
/// </summary>
namespace ZitiDesktopEdge.DataStructures {
    public enum LogLevelEnum {
        FATAL = 0,
        ERROR = 1,
        WARN = 2,
        INFO = 3,
        DEBUG = 4,
        VERBOSE = 5,
        TRACE = 6,
    }

    public class SvcResponse
    {
        public int Code { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }

        override public string ToString()
        {
            return $"Code: {Code}\nMessage: {Message}\nError: {Error}";
        }
    }

    public class StatusUpdateResponse : SvcResponse
    {
        public StatusUpdate Data { get; set; }
    }

    public class StatusUpdate
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

    public class IdentityResponse : SvcResponse
    {
        public Identity Data { get; set; }
    }

    public class ServiceFunction
    {
        public string Command { get; set; }
    }

    public class IdentifierFunction : ServiceFunction
    {
        public IdentifierPayload Data { get; set; }
    }

    public class BooleanPayload
    {
        public bool OnOff { get; set; }
    }

    public class BooleanFunction : ServiceFunction
    {
        public BooleanFunction(string commandName, bool theBool)
        {
            this.Command = commandName;
            this.Data = new BooleanPayload() { OnOff = theBool };
        }
        public BooleanPayload Data { get; set; }
    }

    public class IdentityTogglePayload
    {
        public bool OnOff { get; set; }
        public string Identifier { get; set; }
    }
    public class SetLogLevelPayload
    {
        public string Level { get; set; }
    }

    public class IdentityToggleFunction : ServiceFunction
    {
        public IdentityToggleFunction(string identifier, bool theBool)
        {
            this.Command = "IdentityOnOff";
            this.Data = new IdentityTogglePayload()
            {
                OnOff = theBool,
                Identifier = identifier,
            };
        }
        public IdentityTogglePayload Data { get; set; }
    }

    public class EnableMFAFunction : ServiceFunction {
        public EnableMFAFunction(string identifier) {
            this.Command = "EnableMFA";
            this.Data = new EnableMFAFunctionPayload() {
                Identifier = identifier
            };
        }
        public EnableMFAFunctionPayload Data { get; set; }
    }
    public class EnableMFAFunctionPayload {
        public string Identifier { get; set; }
    }

    public class VerifyMFAFunction : ServiceFunction {
        public VerifyMFAFunction(string identifier, string code) {
            this.Command = "VerifyMFA";
            this.Data = new VerifyMFAFunctionPayload() {
                Identifier = identifier,
                Code = code
            };
        }
        public VerifyMFAFunctionPayload Data { get; set; }
    }
    public class VerifyMFAFunctionPayload {
        public string Identifier { get; set; }
        public string Code { get; set; }
    }
    public class RemoveMFAFunction : ServiceFunction {
        public RemoveMFAFunction(string identifier, string code) {
            this.Command = "RemoveMFA";
            this.Data = new RemoveMFAFunctionPayload() {
                Identifier = identifier,
                Code = code
            };
        }
        public RemoveMFAFunctionPayload Data { get; set; }
    }
    public class RemoveMFAFunctionPayload {
        public string Identifier { get; set; }
        public string Code { get; set; }
    }

    public class AuthMFAFunction : ServiceFunction {
        public AuthMFAFunction(string identifier, string code) {
            this.Command = "SubmitMFA";
            this.Data = new AuthMFAFunctionPayload() {
                Identifier = identifier,
                Code = code
            };
        }
        public AuthMFAFunctionPayload Data { get; set; }
    }
    public class AuthMFAFunctionPayload {
        public string Identifier { get; set; }
        public string Code { get; set; }
    }

    public class GetMFACodesFunction : ServiceFunction {
        public GetMFACodesFunction(string identifier, string code) {
            this.Command = "GetMFACodes";
            this.Data = new GetMFACodesFunctionPayload() {
                Identifier = identifier,
                Code = code,
            };
        }
        public GetMFACodesFunctionPayload Data { get; set; }
    }
    public class GetMFACodesFunctionPayload {
        public string Identifier { get; set; }
        public string Code { get; set; }
    }

    public class GenerateMFACodesFunction : ServiceFunction {
        public GenerateMFACodesFunction(string identifier, string code) {
            this.Command = "GenerateMFACodes";
            this.Data = new GenerateMFACodesFunctionPayload() {
                Identifier = identifier,
                Code = code,
            };
        }
        public GenerateMFACodesFunctionPayload Data { get; set; }
    }
    public class GenerateMFACodesFunctionPayload {
        public string Identifier { get; set; }
        public string Code { get; set; }
    }

    public class SetLogLevelFunction : ServiceFunction {
        public SetLogLevelFunction(string level) {
            this.Command = "SetLogLevel";
            this.Data = new SetLogLevelPayload() {
                Level = level
            };
        }
        public SetLogLevelPayload Data { get; set; }
    }

    public class ZitiDumpPayloadFunction {
        public string DumpPath { get; set; }

    }

    public class ZitiDumpFunction : ServiceFunction {
        public ZitiDumpFunction(string dumpPath) {
            this.Command = "ZitiDump";
            this.Data = new ZitiDumpPayloadFunction() {
                DumpPath = dumpPath
            };
        }
        public ZitiDumpPayloadFunction Data { get; set; }
    }

    public class IdentifierPayload
    {
        public string Identifier { get; set; }
    }

    public class EnrollIdentifierPayload 
    {
        public string JwtFileName { get; set; }
        public string JwtContent { get; set; }
	}

    public class EnrollIdentifierFunction : ServiceFunction 
    {
        public EnrollIdentifierPayload Data { get; set; }
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

    public class Identity {
        public string Name { get; set; }
        public string FingerPrint { get; set; }
        public string Identifier { get; set; }
        public bool Active { get; set; }
        public Config Config { get; set; }
        public string Status { get; set; }
        public List<Service> Services { get; set; }
        public Metrics Metrics { get; set; }
        public string ControllerVersion { get; set; }
        public bool MfaEnabled { get; set; }
        public bool MfaNeeded { get; set; }
        public int MinTimeout { get; set; }
        public int MaxTimeout { get; set; }
        public DateTime MfaLastUpdatedTime { get; set; }

    }

    public class Service {
        public string Name { get; set; }
        public string[] Protocols { get; set; }
        public Address[] Addresses { get; set; }
        public PortRange[] Ports { get; set; }
        public bool OwnsIntercept { get; set; }
        public string AssignedIP { get; set; }
        public PostureCheck[] PostureChecks { get; set; }
        public bool IsAccessible { get; set; }
		public int Timeout { get; set; }
		public int TimeoutRemaining { get; set; }
	}

    public class Address {
        public bool IsHost { get; set; }
        public string Hostname { get; set; }
        public string IP { get; set; }
        public int Prefix { get; set; }

        public override string ToString() {
            if (IsHost) {
                return Hostname;
            } else if (Prefix == 0) {
                return IP;
            } else {
                return IP + "/" + Prefix;
            }
        }
    }

    public class PortRange {
        public int High { get; set; }
        public int Low { get; set; }

        public override string ToString() {
            if (Low == High) {
                return Low.ToString();
            } else {
                return Low + "-" + High;
            }
        }
    }

    public class PostureCheck {
        public bool IsPassing { get; set; }
        public string QueryType { get; set; }
        public string Id { get; set; }
    }

    public class EnrollmentFlags
    {
        public string JwtString { get; set; }
        public string CertFile { get; set; }
        public string KeyFile { get; set; }
        public string AdditionalCAs { get; set; }
    }

    public class IpInfo
    {
        public string Ip { get; set; }
        public string Subnet { get; set; }
        public UInt16 MTU { get; set; }
        public string DNS { get; set; }
    }

    public class ServiceVersion
    {
        public string Version { get; set; }
        public string Revision { get; set; }
        public string BuildDate { get; set; }

        public override string ToString()
        {
            return $"Version: {Version}, Revision: {Revision}, BuildDate: {BuildDate}";
        }
    }

    public class Notification {
        public string IdentityName { get; set; }
        public string Identifier { get; set; }
        public string Fingerprint { get; set; }
        public string Message { get; set; }
        public int MfaMinimumTimeout { get; set; }
        public int MfaMaximumTimeout { get; set; }
        public int MfaTimeDuration { get; set; }
        public string Severity { get; set; }

    }

    public class ZitiTunnelStatus : SvcResponse
    {
        public TunnelStatus Status { get; set; }
    }

    public class TunnelStatus
    {
        public bool Active { get; set; }

        public long Duration { get; set; }

        public List<Identity> Identities { get; set; }

        public IpInfo IpInfo { get; set; }

        public string LogLevel { get; set; }

        public ServiceVersion ServiceVersion { get; set; }
        public bool AddDns { get; set; }
        public int ApiPageSize { get; set; }

        public void Dump(System.IO.TextWriter writer)
        {
            try {
                writer.WriteLine($"Tunnel Active: {Active}");
                writer.WriteLine($"     LogLevel         : {LogLevel}");
                writer.WriteLine($"     EvaluatedLogLevel: {EvaluateLogLevel()}");
                foreach (Identity id in Identities) {
                    writer.WriteLine($"  Identifier: {id.Identifier}");
                    writer.WriteLine($"    Name    : {id.Name}");
                    writer.WriteLine($"    Active  : {id.Active}");
                    writer.WriteLine($"    Status  : {id.Status}");
                    writer.WriteLine($"    Services:");
                    if (id.Services != null)
                    {
                        foreach (Service s in id?.Services)
                        {
                           //xxfix writer.WriteLine($"      Name: {s.Name} Protocols: {string.Join(",", s.Protocols)} Addresses: {string.Join(",", s.Addresses)} Ports: {string.Join(",", s.Ports)}");
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

    public class StatusEvent
    {
        public string Op { get; set; }
    }

    public class ActionEvent : StatusEvent
    {
        public string Action { get; set; }
    }

    public class TunnelStatusEvent : StatusEvent
    {
        public TunnelStatus Status { get; set; }
    }

    public class MetricsEvent : StatusEvent
    {
        public List<Identity> Identities { get; set; }
    }

    public class NotificationEvent : StatusEvent {
        public List<Notification> Notification { get; set; }

    }

    public class ServiceEvent : ActionEvent {
        public string Identifier { get; set; }
        public Service Service { get; set; }
    }

    public class BulkServiceEvent : ActionEvent {
        public string Identifier { get; set; }
        public List<Service> AddedServices { get; set; }
        public List<Service> RemovedServices { get; set; }
    }

    public class IdentityEvent : ActionEvent
    {
        public Identity Id { get; set; }
    }
    
    public class LogLevelEvent : ActionEvent
    {
        public string LogLevel { get; set; }
    }

    public class MonitorServiceStatusEvent : SvcResponse {
        public string Status { get; set; }
        public string ReleaseStream { get; set; }
        public string AutomaticUpgradeDisabled { get; set; }

        public bool IsStopped() {
            return "Stopped" == this.Status;
        }
        public string Type { get; set; }
    }

    public class StatusCheck : MonitorServiceStatusEvent {
        public bool UpdateAvailable { get; set; }
    }

    public class InstallationNotificationEvent : MonitorServiceStatusEvent
    {
        public string ZDEVersion { get; set; }
        public DateTime InstallTime { get; set; }
    }

    public class MfaEvent : ActionEvent {
        public string Identifier { get; set; }
        public bool Successful { get; set; }
        public string ProvisioningUrl { get; set; }
        public List<string> RecoveryCodes { get; set; }
    }

    public class ControllerEvent : ActionEvent {
        public string Identifier { get; set; }
    }

    public class MfaRecoveryCodes {
        public string[] RecoveryCodes { get; set; }
        public string Identifier { get; set; }

    }

    public class MfaRecoveryCodesResponse : SvcResponse {
        public MfaRecoveryCodes Data { get; set; }
    }

    public class ConfigPayload
    {
        public string TunIPv4 { get; set; }
        public int TunPrefixLength { get; set; }
        public bool AddDns { get; set; }
        public int ApiPageSize { get; set; }
    }

    public class ConfigUpdateFunction : ServiceFunction
    {
        public ConfigUpdateFunction(string tunIPv4, int tunPrefixLength, bool addDns, int apiPageSize)
        {
            this.Command = "UpdateTunIpv4";
            this.Data = new ConfigPayload()
            {
                TunIPv4 = tunIPv4,
                TunPrefixLength = tunPrefixLength,
                AddDns = addDns,
                ApiPageSize = apiPageSize,
            };
        }
        public ConfigPayload Data { get; set; }
    }

    public class NotificationFrequencyPayload {
        public int NotificationFrequency { get; set; }
    }

    public class NotificationFrequencyFunction : ServiceFunction {
        public NotificationFrequencyFunction(int notificationFrequency) {
            this.Command = "UpdateFrequency";
            this.Data = new NotificationFrequencyPayload() {
                NotificationFrequency = notificationFrequency
            };
        }
		
        public NotificationFrequencyPayload Data { get; set; }
	}


}
