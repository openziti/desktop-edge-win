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
        public event EventHandler<AuthenticationEvent> OnAuthenticationEvent;

        protected override void ShutdownEvent(StatusEvent e) {
            Logger.Debug("Clean shutdown detected from ziti");
            ExpectedShutdown = true;
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
                throw new ServiceException("Could not connect to the data service.", new SvcResponse() { Code = 1 }, ex.Message);
            }
            semaphoreSlim.Release();
        }

        async private Task sendDataClientAsync(object objtoSend) {
            await sendAsync("data", objtoSend);
        }

        async protected Task<T> readDataClientAsync<T>(StreamReader reader) where T : SvcResponse {
            return await readAsync<T>("data", reader, DefaultReadTimeout);
        }

        async public Task<ZitiTunnelStatus> GetStatusAsync() {
            await sendDataClientAsync(new ServiceFunction() { Command = "Status" });
            var rtn = await readDataClientAsync<ZitiTunnelStatus>(ipcReader);
            return rtn;
        }

        async public Task<Identity> AddIdentityAsync(EnrollIdentifierPayload payload) {
            IdentityResponse resp = null;
            try {
                EnrollIdentifierFunction enrollIdentifierFunction = new EnrollIdentifierFunction() {
                    Command = "AddIdentity",
                    Data = payload
                };

                await sendDataClientAsync(enrollIdentifierFunction);
                resp = await readDataClientAsync<IdentityResponse>(ipcReader);
            } catch (Exception ex) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                //throw;
                Logger.Error(ex, "Unexpected error");
                CommunicationError(ex);
                throw ex;
            }
            if (resp?.Error == "ZITI_KEY_GENERATION_FAILED") {
                throw new ServiceException("Failed to Enroll", resp, "keygen failed");
            }
            if (resp?.Code != 0) {
                Logger.Warn("failed to enroll. {0} {1}", resp.Message, resp.Error);
                throw new ServiceException("Failed to Enroll", resp, !string.IsNullOrEmpty(resp.Error) ? resp.Error : "The provided token was invalid. This usually is because the token has already been used or it has expired.");
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
                await sendDataClientAsync(removeFunction);
                var r = await readDataClientAsync<SvcResponse>(ipcReader);
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
                await sendDataClientAsync(new SetLogLevelFunction(level));
                SvcResponse resp = await readDataClientAsync<SvcResponse>(ipcReader);
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
                await sendDataClientAsync(new IdentityToggleFunction(identifier, onOff));
                IdentityResponse idr = await readDataClientAsync<IdentityResponse>(ipcReader);
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
                await sendDataClientAsync(new EnableMFAFunction(identifier));
                SvcResponse mfa = await readDataClientAsync<SvcResponse>(ipcReader);
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
                await sendDataClientAsync(new VerifyMFAFunction(identifier, totp));
                SvcResponse mfa = await readDataClientAsync<SvcResponse>(ipcReader);
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
                await sendDataClientAsync(new AuthMFAFunction(identifier, totp));
                SvcResponse mfa = await readDataClientAsync<SvcResponse>(ipcReader);
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
                await sendDataClientAsync(new GetMFACodesFunction(identifier, totpOrRecoveryCode));
                MfaRecoveryCodesResponse mfa = await readDataClientAsync<MfaRecoveryCodesResponse>(ipcReader);
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
                await sendDataClientAsync(new GenerateMFACodesFunction(identifier, totpOrRecoveryCode));
                MfaRecoveryCodesResponse mfa = await readDataClientAsync<MfaRecoveryCodesResponse>(ipcReader);
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
                await sendDataClientAsync(new RemoveMFAFunction(identifier, totp));
                SvcResponse mfa = await readDataClientAsync<SvcResponse>(ipcReader);
                return mfa;
            } catch (Exception ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //throw ioe;
                Logger.Error(ioe, "Unexpected error");
                CommunicationError(ioe);
            }
            return null;
        }

        async public Task<ExternalAuthLoginResponse> ExternalAuthLogin(string identifier, string provider) {
            try {
                await sendDataClientAsync(new ExternalAuthLogin(identifier, provider));
                ExternalAuthLoginResponse extAuthResp = await readDataClientAsync<ExternalAuthLoginResponse>(ipcReader);
                return extAuthResp;
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
                    case "authentication":
                        Logger.Debug("Authentication event received");
                        AuthenticationEvent authEvent = serializer.Deserialize<AuthenticationEvent>(jsonReader);
                        OnAuthenticationEvent?.Invoke(this, authEvent);
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
                await sendDataClientAsync(new ZitiDumpFunction(dumpPath));
                var rtn = await readDataClientAsync<SvcResponse>(ipcReader);
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

                await sendDataClientAsync(configPayload);
                resp = await readDataClientAsync<SvcResponse>(ipcReader);
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
                throw new ServiceException("Failed to update the config", resp, "Un expected error.");
            }
            return resp;
        }

        async public Task<SvcResponse> NotificationFrequencyPayloadAsync(int frequency) {
            SvcResponse resp = null;
            try {

                NotificationFrequencyFunction frequencyPayload = new NotificationFrequencyFunction(frequency);

                await sendDataClientAsync(frequencyPayload);
                resp = await readDataClientAsync<SvcResponse>(ipcReader);
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
                throw new ServiceException("Failed to update the frequency", resp, "Un expected error.");
            }
            return resp;
        }


        async public Task<ZitiTunnelStatus> debugAsync() {
            try {
                await sendDataClientAsync(new ServiceFunction() { Command = "Debug" });
                var rtn = await readDataClientAsync<ZitiTunnelStatus>(ipcReader);
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
