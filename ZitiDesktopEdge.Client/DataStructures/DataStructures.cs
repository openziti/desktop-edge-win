/*
	Copyright NetFoundry Inc.

	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at

	https://www.apache.org/licenses/LICENSE-2.0

	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
*/

using System;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// These classes represent the data structures that are passed back and forth
/// between the service and the client.
/// </summary>
namespace ZitiDesktopEdge.DataStructures {
    /// <summary>
    /// Cadence selector for the installation maintenance window. The hour-of-day Start/End
    /// still defines the time-of-day; this enum decides which calendar days qualify.
    /// </summary>
    public enum MaintenanceWindowFrequency {
        Daily = 0,
        Weekly = 1,
        Monthly = 2,
    }

    /// <summary>
    /// Sub-mode for <see cref="MaintenanceWindowFrequency.Monthly"/>:
    /// <c>ByDate</c> picks a fixed day of the month (1-28, or LastDay sentinel),
    /// <c>ByWeekday</c> picks the Nth weekday of the month (e.g. "Third Tuesday").
    /// Mirrors SCCM's "Monthly by date" / "Monthly by day of week" split.
    /// </summary>
    public enum MaintenanceWindowMonthlyMode {
        ByDate = 0,
        ByWeekday = 1,
    }

    /// <summary>
    /// Ordinal for <see cref="MaintenanceWindowMonthlyMode.ByWeekday"/>. <c>Last</c>
    /// is a first-class value because "Last Friday" and "Fourth Friday" diverge in
    /// roughly a third of months (the Fourth/Last gap is the most common bug-source
    /// for maintenance-window misfires in SCCM-managed fleets).
    /// </summary>
    public enum MaintenanceWindowMonthlyOrdinal {
        First = 1,
        Second = 2,
        Third = 3,
        Fourth = 4,
        Last = 5,
    }

    /// <summary>
    /// Sentinel value for <c>MaintenanceWindowDayOfMonth</c> meaning "last day of the
    /// current month". Chosen so it is outside the legal 1-28 range and not a valid
    /// month-day; the evaluator substitutes the actual last day at runtime.
    /// </summary>
    public static class MaintenanceWindowDayOfMonthSentinel {
        public const int LastDay = 32;
    }

    public enum LogLevelEnum {
        FATAL = 0,
        ERROR = 1,
        WARN = 2,
        INFO = 3,
        DEBUG = 4,
        VERBOSE = 5,
        TRACE = 6,
    }

    public class SvcResponse {
        public int Code { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }

        override public string ToString() {
            return $"Code: {Code}\nMessage: {Message}\nError: {Error}";
        }
    }

    public class StatusUpdateResponse : SvcResponse {
        public StatusUpdate Data { get; set; }
    }

    public class StatusUpdate {
        public string Operation { get; set; }
        public TunnelStatus Status { get; set; }

        public Identity NewIdentity { get; set; }
    }

    public class NewIdentity {
        public EnrollmentFlags Flags { get; set; }
        public Identity Id { get; set; }
    }

    public class IdentityResponse : SvcResponse {
        public Identity Data { get; set; }
    }

    public class ServiceFunction {
        public string Command { get; set; }
    }

    public class IdentifierFunction : ServiceFunction {
        public IdentifierPayload Data { get; set; }
    }

    public class BooleanPayload {
        public bool OnOff { get; set; }
    }

    public class BooleanFunction : ServiceFunction {
        public BooleanFunction(string commandName, bool theBool) {
            this.Command = commandName;
            this.Data = new BooleanPayload() { OnOff = theBool };
        }
        public BooleanPayload Data { get; set; }
    }

    public class IdentityTogglePayload {
        public bool OnOff { get; set; }
        public string Identifier { get; set; }
    }
    public class SetLogLevelPayload {
        public string Level { get; set; }
    }

    public class IdentityToggleFunction : ServiceFunction {
        public IdentityToggleFunction(string identifier, bool theBool) {
            this.Command = "IdentityOnOff";
            this.Data = new IdentityTogglePayload() {
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

    public class ExternalAuthFunction {
        public string Identifier { get; set; }
        public string Provider { get; set; }
    }
    public class ExternalAuthLogin : ServiceFunction {
        public ExternalAuthLogin(string identifier, string extProvider) {
            this.Command = "ExternalAuth";
            this.Data = new ExternalAuthFunction() {
                Identifier = identifier,
                Provider = extProvider,
            };
        }
        public ExternalAuthFunction Data { get; set; }
    }
    public class ExternalAuthLoginResponse : SvcResponse {
        public ExternalAuthLoginPayload Data { get; set; }
    }
    public class ExternalAuthLoginPayload {
        public string identifier { get; set; }
        public string url { get; set; }
    }

    public class IdentifierPayload {
        public string Identifier { get; set; }
    }

    public class EnrollIdentifierPayload {
        public bool UseKeychain { get; set; }
        public string IdentityFilename { get; set; }
        public string JwtContent { get; set; }
        public string Key { get; set; }
        public string Certificate { get; set; }
        public string ControllerURL { get; set; }
        public string EnrollMode { get; set; }
        public string Provider { get; set; }
    }

    public class EnrollIdentifierFunction : ServiceFunction {
        public EnrollIdentifierPayload Data { get; set; }
    }

    public class Id {
        public string key { get; set; }
        public string cert { get; set; }
    }

    public class Config {
        public string ztAPI { get; set; }
        public Id id { get; set; }
        public object configTypes { get; set; }
    }

    public class Metrics {
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
        public int MfaMinTimeout { get; set; }
        public int MfaMaxTimeout { get; set; }
        public int MfaMinTimeoutRem { get; set; }
        public int MfaMaxTimeoutRem { get; set; }
        public int MinTimeoutRemInSvcEvent { get; set; }
        public int MaxTimeoutRemInSvcEvent { get; set; }
        public DateTime MfaLastUpdatedTime { get; set; }
        public bool NeedsExtAuth { get; set; }
        public List<string> ExtAuthProviders { get; set; }
        // Populated on the AddIdentity response when enrollment needs external auth — zet returns
        // the OIDC authorize URL so ZDEW can open the browser for the user to sign in.
        public string Url { get; set; }
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
        public Permissions Permissions { get; set; }
    }

    public class Permissions {
        public bool Bind { get; set; }
        public bool Dial { get; set; }
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

    public class EnrollmentFlags {
        public string JwtString { get; set; }
        public string CertFile { get; set; }
        public string KeyFile { get; set; }
        public string AdditionalCAs { get; set; }
    }

    public class IpInfo {
        public string Ip { get; set; }
        public string Subnet { get; set; }
        public UInt16 MTU { get; set; }
        public string DNS { get; set; }
    }

    public class ServiceVersion {
        public string Version { get; set; }
        public string Revision { get; set; }
        public string BuildDate { get; set; }

        public override string ToString() {
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

    public class ZitiTunnelStatus : SvcResponse {
        public TunnelStatus Status { get; set; }
    }

    public class TunnelStatus {
        public long Duration { get; set; }

        public List<Identity> Identities { get; set; }

        public IpInfo IpInfo { get; set; }

        public string LogLevel { get; set; }

        public ServiceVersion ServiceVersion { get; set; }
        public bool AddDns { get; set; }
        public int ApiPageSize { get; set; }
        public bool L2Enabled { get; set; }
        public string PcapInterface { get; set; }

#if DEBUG
        public void Dump(System.IO.TextWriter writer) {
            try {
                writer.WriteLine($"     LogLevel         : {LogLevel}");
                writer.WriteLine($"     EvaluatedLogLevel: {EvaluateLogLevel()}");
                foreach (Identity id in Identities) {
                    writer.WriteLine($"  Identifier: {id.Identifier}");
                    writer.WriteLine($"    Name    : {id.Name}");
                    writer.WriteLine($"    Active  : {id.Active}");
                    writer.WriteLine($"    Status  : {id.Status}");
                    writer.WriteLine($"    Services:");
                    if (id.Services != null) {
                        foreach (Service s in id?.Services) {
                            //xxfix writer.WriteLine($"      Name: {s.Name} Protocols: {string.Join(",", s.Protocols)} Addresses: {string.Join(",", s.Addresses)} Ports: {string.Join(",", s.Ports)}");
                        }
                    }
                    writer.WriteLine("=============================================");
                }
            } catch (Exception e) {
                if (writer != null) writer.WriteLine(e.ToString());
            }
        }
#endif

        public LogLevelEnum EvaluateLogLevel() {
            try {
                LogLevelEnum l = (LogLevelEnum)Enum.Parse(typeof(LogLevelEnum), LogLevel.ToUpper());
                return l;
            } catch {
                return LogLevelEnum.INFO;
            }
        }
    }

    public class ServiceException : System.Exception {
        public ServiceException(string Message, SvcResponse resp, string AdditionalInfo) : base(Message) {
            this.Code = resp.Code;
            this.AdditionalInfo = AdditionalInfo;
            this.OriginalResponse = resp;
        }

        public int Code { get; }
        public string AdditionalInfo { get; }
        public SvcResponse OriginalResponse { get; }
    }

    public class StatusEvent {
        public string Op { get; set; }
    }

    public class ActionEvent : StatusEvent {
        public string Action { get; set; }
    }

    public class TunnelStatusEvent : StatusEvent {
        public TunnelStatus Status { get; set; }
    }

    public class MetricsEvent : StatusEvent {
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

    public class IdentityEvent : ActionEvent {
        public Identity Id { get; set; }
    }

    public class LogLevelEvent : ActionEvent {
        public string LogLevel { get; set; }
    }

    /// <summary>
    /// Inbound IPC request that atomically updates every maintenance-window field. The
    /// monitor service validates the whole payload, then writes settings.json once. Replaces
    /// the prior 7-call per-field burst (SetMaintenanceWindowStart / End / Frequency /
    /// MonthlyMode / DayOfWeek / DayOfMonth / MonthlyOrdinal) so there's no half-applied
    /// state on partial failure and no 7x JSON rewrite.
    /// </summary>
    public class MaintenanceWindowConfigRequest : ActionEvent {
        public int? Start { get; set; }
        public int? End { get; set; }
        public MaintenanceWindowFrequency Frequency { get; set; }
        public MaintenanceWindowMonthlyMode MonthlyMode { get; set; }
        public int? DayOfWeek { get; set; }
        public int? DayOfMonth { get; set; }
        public MaintenanceWindowMonthlyOrdinal? MonthlyOrdinal { get; set; }
    }

    public class MonitorServiceStatusEvent : SvcResponse {
        public string Status { get; set; }
        public string ReleaseStream { get; set; }
        public string AutomaticUpgradeDisabled { get; set; }
        public bool   AutomaticUpgradeDisabledLocked { get; set; }
        public string AutomaticUpgradeURL { get; set; }
        public bool   AutomaticUpgradeURLLocked { get; set; }
        public int?   AlivenessChecksBeforeAction { get; set; }
        public bool   AlivenessChecksBeforeActionLocked { get; set; }
        public string UpdateInterval { get; set; }
        public bool   UpdateIntervalLocked { get; set; }
        public string InstallationReminder { get; set; }
        public bool   InstallationReminderLocked { get; set; }
        public string InstallationCritical { get; set; }
        public bool   InstallationCriticalLocked { get; set; }
        public int?   MaintenanceWindowStart { get; set; }
        public bool   MaintenanceWindowStartLocked { get; set; }
        public int?   MaintenanceWindowEnd { get; set; }
        public bool   MaintenanceWindowEndLocked { get; set; }
        public MaintenanceWindowFrequency MaintenanceWindowFrequency { get; set; }
        public bool   MaintenanceWindowFrequencyLocked { get; set; }
        public int?   MaintenanceWindowDayOfWeek { get; set; }
        public bool   MaintenanceWindowDayOfWeekLocked { get; set; }
        public int?   MaintenanceWindowDayOfMonth { get; set; }
        public bool   MaintenanceWindowDayOfMonthLocked { get; set; }
        public MaintenanceWindowMonthlyMode MaintenanceWindowMonthlyMode { get; set; }
        public bool   MaintenanceWindowMonthlyModeLocked { get; set; }
        public MaintenanceWindowMonthlyOrdinal? MaintenanceWindowMonthlyOrdinal { get; set; }
        public bool   MaintenanceWindowMonthlyOrdinalLocked { get; set; }
        public bool   DeferInstallToRestartLocked { get; set; }

        /// <summary>
        /// True when the user requested an immediate install but the service deferred it
        /// to the next maintenance window. Lives only in service memory, never persisted.
        /// </summary>
        public bool   DeferredInstallPending  { get; set; }
        public bool   DeferToRestartPending   { get; set; }
        public bool   StagingDownloadPending  { get; set; }

        public bool IsStopped() {
            return "Stopped" == this.Status;
        }
        public string Type { get; set; }
    }

    public class StatusCheck : MonitorServiceStatusEvent {
        public bool UpdateAvailable { get; set; }
    }

    public class InstallationNotificationEvent : MonitorServiceStatusEvent {
        public string ZDEVersion { get; set; }
        public DateTime InstallTime { get; set; }
        public DateTime PublishTime { get; set; }
        public TimeSpan NotificationDuration { get; set; }
    }

    public class UrlUpdateEvent : MonitorServiceStatusEvent {
        public string URL { get; set; }
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
    public class AuthenticationEvent : ActionEvent {
        public string Identifier { get; set; }
    }

    public class MfaRecoveryCodes {
        public string[] RecoveryCodes { get; set; }
        public string Identifier { get; set; }

    }

    public class MfaRecoveryCodesResponse : SvcResponse {
        public MfaRecoveryCodes Data { get; set; }
    }

    public class L3Options {
        public string TunIPv4 { get; set; }
        public int TunPrefixLength { get; set; }
        public bool AddDns { get; set; }
        public int ApiPageSize { get; set; }
    }

    public class L2Options {
        public bool Enabled { get; set; }
        public string PcapInterface { get; set; }
    }

    public class InterfaceConfigPayload {
        public L3Options L3 { get; set; }
        public L2Options L2 { get; set; }
    }

    public class InterfaceConfigUpdateFunction : ServiceFunction {
        public InterfaceConfigUpdateFunction(string tunIPv4, int tunPrefixLength, bool addDns, int apiPageSize, bool l2Enabled, string pcapInterface) {
            this.Command = "UpdateInterfaceConfig";
            this.Data = new InterfaceConfigPayload() {
                L3 = new L3Options() {
                    TunIPv4 = tunIPv4,
                    TunPrefixLength = tunPrefixLength,
                    AddDns = addDns,
                    ApiPageSize = apiPageSize,
                },
                L2 = new L2Options() {
                    Enabled = l2Enabled,
                    PcapInterface = pcapInterface,
                },
            };
        }
        public InterfaceConfigPayload Data { get; set; }
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
