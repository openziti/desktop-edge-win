using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Threading.Tasks;

using Newtonsoft.Json;
using NLog;

using ZitiDesktopEdge.DataStructures;

/// <summary>
/// The implementation will abstract away the setup of the communication to
/// the service. This implementation will communicate to the service over a
/// a NamedPipe.
/// 
/// All communication is effectively serial - one or more messages sent and 
/// one or more messages returned.
/// 
/// </summary>
namespace ZitiDesktopEdge.ServiceClient {
    public class DataClient : AbstractClient {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        protected override Logger Logger { get { return _logger; } }

        public const int EXPECTED_API_VERSION = 1;

        public event EventHandler<TunnelStatusEvent> OnTunnelStatusEvent;
        public event EventHandler<List<Identity>> OnMetricsEvent;
        public event EventHandler<IdentityEvent> OnIdentityEvent;
        public event EventHandler<ServiceEvent> OnServiceEvent;
        public event EventHandler<LogLevelEvent> OnLogLevelEvent;
        public event EventHandler<MfaEvent> OnMfaEvent;
        public event EventHandler<BulkServiceEvent> OnBulkServiceEvent;
        public event EventHandler<NotificationEvent> OnNotificationEvent;
        public event EventHandler<ControllerEvent> OnControllerEvent;

        protected override void ShutdownEvent(StatusEvent e) {
            Logger.Debug("Clean shutdown detected from ziti");
            CleanShutdown = true;
            base.ShutdownEvent(e);
        }

        protected virtual void TunnelStatusEvent(TunnelStatusEvent e) {
            OnTunnelStatusEvent?.Invoke(this, e);
        }

        protected virtual void MetricsEvent(List<Identity> e) {
            OnMetricsEvent?.Invoke(this, e);
        }

        protected virtual void IdentityEvent(IdentityEvent e) {
            OnIdentityEvent?.Invoke(this, e);
        }

        protected virtual void ServiceEvent(ServiceEvent e) {
            OnServiceEvent?.Invoke(this, e);
        }

        protected virtual void BulkServiceEvent(BulkServiceEvent e) {
            OnBulkServiceEvent?.Invoke(this, e);
        }

        protected virtual void LogLevelEvent(LogLevelEvent e) {
            OnLogLevelEvent?.Invoke(this, e);
        }

        protected virtual void MfaEvent(MfaEvent e) {
            OnMfaEvent?.Invoke(this, e);
        }

        protected virtual void NotificationEvent(NotificationEvent e) {
            OnNotificationEvent?.Invoke(this, e);
		}

        protected virtual void ControllerEvent(ControllerEvent e) {
            OnControllerEvent?.Invoke(this, e);
        }

        protected override void ClientConnected(object e) {
            base.ClientConnected(e);
        }

        protected override void ClientDisconnected(object e) {
            Reconnect();
            Connected = false;
            base.ClientDisconnected(e);
        }

        // ziti tunnel
        /*
        const string ipcPipe = @"OpenZiti\ziti\ipc";
        const string logPipe = @"OpenZiti\ziti\logs";
        const string eventPipe = @"OpenZiti\ziti\events";
        */

        // ziti edge tunnel
        const string ipcPipe = @"ziti-edge-tunnel.sock";
        const string eventPipe = @"ziti-edge-tunnel-event.sock";

        public DataClient(string id) : base(id) {
        }

        PipeSecurity CreateSystemIOPipeSecurity() {
            PipeSecurity pipeSecurity = new PipeSecurity();

            var id = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            // Allow Everyone read and write access to the pipe. 
            pipeSecurity.SetAccessRule(new PipeAccessRule(id, PipeAccessRights.ReadWrite, AccessControlType.Allow));

            return pipeSecurity;
        }

        async protected override Task ConnectPipesAsync() {
            await semaphoreSlim.WaitAsync();
            try {
                pipeClient = new NamedPipeClientStream(localPipeServer, ipcPipe, PipeDirection.InOut);
                eventClient = new NamedPipeClientStream(localPipeServer, eventPipe, PipeDirection.In);
                await eventClient.ConnectAsync(ServiceConnectTimeout);
                await pipeClient.ConnectAsync(ServiceConnectTimeout);
                ClientConnected(null);
            } catch (Exception ex) {
                semaphoreSlim.Release();
                throw new ServiceException("Could not connect to the service.", 1, ex.Message);
            }
            semaphoreSlim.Release();
        }

        async public Task<ZitiTunnelStatus> GetStatusAsync() {
            try {
                await sendAsync(new ServiceFunction() { Command = "Status" });
                var rtn = await readAsync<ZitiTunnelStatus>(ipcReader);
                return rtn;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
            }
            return null;
        }

        ServiceFunction AddIdentityFunction = new ServiceFunction() { Command = "AddIdentity" };

        async public Task<Identity> AddIdentityAsync(string jwtFileName, bool activate, string jwtContent) {
            IdentityResponse resp = null;
            try {

                EnrollIdentifierFunction enrollIdentifierFunction = new EnrollIdentifierFunction() {
                    Command = "AddIdentity",
                    Data = new EnrollIdentifierPayload() {
                        JwtFileName = jwtFileName,
                        JwtContent = jwtContent
                    }
                };

                await sendAsync(enrollIdentifierFunction);
                resp = await readAsync<IdentityResponse>(ipcReader);
                Logger.Debug(resp.ToString());
            } catch (Exception ex) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                //throw;
                Logger.Error(ex, "Unexpected error");
                CommunicationError(ex);
                throw ex;
            }
            if (resp?.Code != 0) {
                Logger.Warn("failed to enroll. {0} {1}", resp.Message, resp.Error);
                throw new ServiceException("Failed to Enroll", resp.Code, !string.IsNullOrEmpty(resp.Error) ? resp.Error : "The provided token was invalid. This usually is because the token has already been used or it has expired.");
            }
            return resp.Data;
        }


        async public Task RemoveIdentityAsync(string identifier) {
            if (string.IsNullOrEmpty(identifier)) {
                //nothing to do...
                return;
            }

            try {
                IdentifierFunction removeFunction = new IdentifierFunction() {
                    Command = "RemoveIdentity",
                    Data = new IdentifierPayload() { Identifier = identifier }
                };
                Logger.Info("Removing Identity with identifier {0}", identifier);
                await sendAsync(removeFunction);
                var r = await readAsync<SvcResponse>(ipcReader);
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
                CommunicationError(ioe);
            }
            return;
        }

        async public Task SetLogLevelAsync(string level) {
            try {
                await sendAsync(new SetLogLevelFunction(level));
                SvcResponse resp = await readAsync<SvcResponse>(ipcReader);
                return;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
                CommunicationError(ioe);
            }
            return;
        }

        async public Task<Identity> IdentityOnOffAsync(string identifier, bool onOff) {
            try {
                await sendAsync(new IdentityToggleFunction(identifier, onOff));
                IdentityResponse idr = await readAsync<IdentityResponse>(ipcReader);
                return idr.Data;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
                CommunicationError(ioe);
            }
            return null;
        }

        async public Task<SvcResponse> EnableMFA(string identifier) {
            try {
                await sendAsync(new EnableMFAFunction(identifier));
                SvcResponse mfa = await readAsync<SvcResponse>(ipcReader);
                return mfa;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
                CommunicationError(ioe);
            }
            return null;
        }

        async public Task<SvcResponse> VerifyMFA(string identifier, string totp) {
            try {
                await sendAsync(new VerifyMFAFunction(identifier, totp));
                SvcResponse mfa = await readAsync<SvcResponse>(ipcReader);
                return mfa;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
                CommunicationError(ioe);
            }
            return null;
        }

        async public Task<SvcResponse> AuthMFA(string identifier, string totp) {
            try {
                await sendAsync(new AuthMFAFunction(identifier, totp));
                SvcResponse mfa = await readAsync<SvcResponse>(ipcReader);
                return mfa;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
                CommunicationError(ioe);
            }
            return null;
        }

        async public Task<MfaRecoveryCodesResponse> GetMFACodes(string identifier, string totpOrRecoveryCode) {
            try {
                await sendAsync(new GetMFACodesFunction(identifier, totpOrRecoveryCode));
                MfaRecoveryCodesResponse mfa = await readAsync<MfaRecoveryCodesResponse>(ipcReader);
                return mfa;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
                CommunicationError(ioe);
            }
            return null;
        }

        async public Task<MfaRecoveryCodesResponse> GenerateMFACodes(string identifier, string totpOrRecoveryCode) {
            try {
                await sendAsync(new GenerateMFACodesFunction(identifier, totpOrRecoveryCode));
                MfaRecoveryCodesResponse mfa = await readAsync<MfaRecoveryCodesResponse>(ipcReader);
                return mfa;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
                CommunicationError(ioe);
            }
            return null;
        }
        async public Task<SvcResponse> RemoveMFA(string identifier, string totp) {
            try {
                await sendAsync(new RemoveMFAFunction(identifier, totp));
                SvcResponse mfa = await readAsync<SvcResponse>(ipcReader);
                return mfa;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
                CommunicationError(ioe);
            }
            return null;
        }

        protected override void ProcessLine(string line) {
            try {
                string respAsString = line;
                var jsonReaderEvt = new JsonTextReader(new StringReader(respAsString));
                StatusEvent evt = serializer.Deserialize<StatusEvent>(jsonReaderEvt);
                var jsonReader = new JsonTextReader(new StringReader(respAsString));

                if (evt == null) {
                    return;
                }

                switch (evt.Op) {
                    case "metrics":
                        MetricsEvent m = serializer.Deserialize<MetricsEvent>(jsonReader);

                        if (m != null) {
                            MetricsEvent(m.Identities);
                        }
                        break;
                    case "status": //break here to see status on startup
                        //dbg comment Logger.Warn("STATUS EVENT: \n" + respAsString);
                        TunnelStatusEvent tse = serializer.Deserialize<TunnelStatusEvent>(jsonReader);
                        
                        if (tse != null) {
                            TunnelStatusEvent(tse);
                        }
                        break;
                    case "identity":
                        //dbg comment Logger.Warn("IDENTITY EVENT: \n" + respAsString);
                        IdentityEvent id = serializer.Deserialize<IdentityEvent>(jsonReader);

                        if (id != null) {
                            IdentityEvent(id);
                        }
                        break;
                    case "service":
                        //dbg comment Logger.Warn("SERVICE EVENT: \n" + respAsString);
                        ServiceEvent svc = serializer.Deserialize<ServiceEvent>(jsonReader);

                        if (svc != null) {
                            ServiceEvent(svc);
                        }
                        break;
                    case "bulkservice":
                        //dbg comment Logger.Warn("BULKSERVICE EVENT: \n" + respAsString);
                        BulkServiceEvent bsvc = serializer.Deserialize<BulkServiceEvent>(jsonReader);

                        if (bsvc != null) {
                            BulkServiceEvent(bsvc);
                        }
                        break;
                    case "logLevel":
                        LogLevelEvent ll = serializer.Deserialize<LogLevelEvent>(jsonReader);

                        if (ll != null) {
                            LogLevelEvent(ll);
                        }
                        break;
                    case "shutdown":
                        Logger.Debug("shutdown message received");
                        var se = new StatusEvent();
                        se.Op = "clean";
                        ShutdownEvent(se);
                        break;
                    case "mfa":
                        //dbg comment Logger.Warn("MFA EVENT: \n" + respAsString);
                        Logger.Debug("mfa event received");
                        MfaEvent mfa = serializer.Deserialize<MfaEvent>(jsonReader);
                        MfaEvent(mfa);
                        break;
                    case "notification":
                        Logger.Debug("Notification event received");
                        NotificationEvent notificationEvent = serializer.Deserialize<NotificationEvent>(jsonReader);
                        NotificationEvent(notificationEvent);
                        break;
                    case "controller":
                        Logger.Debug("Controller event received");
                        ControllerEvent controllerEvent = serializer.Deserialize<ControllerEvent>(jsonReader);
                        ControllerEvent(controllerEvent);
                        break;
                    default:
                        Logger.Debug("unexpected operation! " + evt.Op);
                        break;
                }
            } catch (Exception e) {
                Logger.Debug(e.Message);
            }
        }

        async public Task zitiDump(string dumpPath) {
            try {
                await sendAsync(new ZitiDumpFunction(dumpPath));
                var rtn = await readAsync<SvcResponse>(ipcReader);
                return; // rtn;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                //throw ioe;
                Logger.Error(ioe, "Could not perform ziti dump. Unexpected error. Is ziti running?");
            }
            return;
        }

        async public Task<SvcResponse> UpdateConfigAsync(string tunIPv4, int tunIPv4Mask, bool addDns, int apiPageSize) {
            SvcResponse resp = null;
            try {

                ConfigUpdateFunction configPayload = new ConfigUpdateFunction(tunIPv4, tunIPv4Mask, addDns, apiPageSize);

                await sendAsync(configPayload);
                resp = await readAsync<SvcResponse>(ipcReader);
                Logger.Debug("config update payload is sent to the ziti tunnel");
            } catch (Exception ex) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                //throw;
                Logger.Error(ex, "Unexpected error");
                CommunicationError(ex);
                throw ex;
            }
            if (resp?.Code != 0) {
                Logger.Warn("failed to update the config. {0} {1}", resp.Message, resp.Error);
                throw new ServiceException("Failed to update the config", resp.Code, "Un expected error.");
            }
            return resp;
        }

        async public Task<SvcResponse> NotificationFrequencyPayloadAsync(int frequency) {
            SvcResponse resp = null;
            try {

                NotificationFrequencyFunction frequencyPayload = new NotificationFrequencyFunction(frequency);

                await sendAsync(frequencyPayload);
                resp = await readAsync<SvcResponse>(ipcReader);
                Logger.Debug("frequency update payload is sent to the ziti tunnel");
            } catch (Exception ex) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                //throw;
                Logger.Error(ex, "Unexpected error");
                CommunicationError(ex);
                throw ex;
            }
            if (resp?.Code != 0) {
                Logger.Warn("failed to update the frequency. {0} {1}", resp.Message, resp.Error);
                throw new ServiceException("Failed to update the frequency", resp.Code, "Un expected error.");
            }
            return resp;
        }


        async public Task<ZitiTunnelStatus> debugAsync() {
            try {
                await sendAsync(new ServiceFunction() { Command = "Debug" });
                var rtn = await readAsync<ZitiTunnelStatus>(ipcReader);
                return rtn;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
            }
            return null;
        }
    }
}
