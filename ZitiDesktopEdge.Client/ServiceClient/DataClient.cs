using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using NLog;

using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.Server;

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

        protected override void ClientConnected(object e) {
            base.ClientConnected(e);
        }

        protected override void ClientDisconnected(object e) {
            Reconnect();
            Connected = false;
            base.ClientDisconnected(e);
        }

        const string ipcPipe = @"OpenZiti\ziti\ipc";
        const string logPipe = @"OpenZiti\ziti\logs";
        const string eventPipe = @"OpenZiti\ziti\events";

        public DataClient() : base() {
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
                await sendAsync(new ServiceFunction() { Function = "Status" });
                var rtn = await readAsync<ZitiTunnelStatus>(ipcReader, "GetStatusAsync");
                return rtn;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        ServiceFunction AddIdentityFunction = new ServiceFunction() { Function = "AddIdentity" };

        async public Task<Identity> AddIdentityAsync(string identityName, bool activate, string jwt) {
            try {
                Identity id = new Identity {
                    Active = activate,
                    Name = identityName
                };

                NewIdentity newId = new NewIdentity() {
                    Id = id,
                    Flags = new EnrollmentFlags() {
                        JwtString = jwt
                    }
                };

                await sendAsync(AddIdentityFunction);
                await sendAsync(newId);
                var resp = await readAsync<IdentityResponse>(ipcReader, "AddIdentityAsync");
                Logger.Debug(resp.ToString());
                if (resp.Code != 0) {
                    throw new ServiceException(resp.Message, resp.Code, resp.Error);
                }
                return resp.Payload;
            } catch (IOException) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw;
            }
        }


        async public Task RemoveIdentityAsync(string fingerPrint) {
            if (string.IsNullOrEmpty(fingerPrint)) {
                //nothing to do...
                return;
            }

            try {
                FingerprintFunction removeFunction = new FingerprintFunction() {
                    Function = "RemoveIdentity",
                    Payload = new FingerprintPayload() { Fingerprint = fingerPrint }
                };
                await sendAsync(removeFunction);
                var r = await readAsync<SvcResponse>(ipcReader, "RemoveIdentityAsync");
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        private void checkConnected() {
            if (Reconnecting) {
                throw new ServiceException("Client is not connected", 2, "Cannot use the client at this time, it is reconnecting");
            }
            if (!Connected) {
                throw new ServiceException("Client is not connected", 2, "Cannot use the client at this time, it is not connected");
            }
        }

        public void SetTunnelState(bool onOff) {
            /*
            checkConnected();
            try
            {
                send(new BooleanFunction("TunnelState", onOff));
                read<SvcResponse>(ipcReader);
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
            */
        }

        public string GetLogs() {
            try {
                NamedPipeClientStream logClient = new NamedPipeClientStream(localPipeServer, logPipe, PipeDirection.In);
                StreamReader logReader = new StreamReader(logClient);
                logClient.Connect(ServiceConnectTimeout);

                string content = logReader.ReadToEnd();

                //ugly hack to turn ansi escaping to not... _bleck_
                //todo: fix this :point_up:
                content = new System.Text.RegularExpressions.Regex(@"\x1B\[[^@-~]*[@-~]").Replace(content, "");
                return content;
            } catch {
                //almost certainly a problem with the pipe - probably means the service is NOT running
                return "Error fetching logs from service. Is it running?";
            }
        }

        async public Task SetLogLevelAsync(string level) {
            try {
                await sendAsync(new SetLogLevelFunction(level));
                SvcResponse resp = await readAsync<SvcResponse>(ipcReader, "SetLogLevelAsync");
                return;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        async public Task SetLogLevelAsync(LogLevelEnum level) {
            try {
                await sendAsync(new SetLogLevelFunction(Enum.GetName(level.GetType(), level)));
                SvcResponse resp = await readAsync<SvcResponse>(ipcReader, "SetLogLevelAsync");
                return;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        async public Task<Identity> IdentityOnOffAsync(string fingerprint, bool onOff) {
            try {
                await sendAsync(new IdentityToggleFunction(fingerprint, onOff));
                IdentityResponse idr = await readAsync<IdentityResponse>(ipcReader, "IdentityOnOffAsync");
                return idr.Payload;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        protected override void ProcessLine(string line) {
            try {
                string respAsString = line;
                var jsonReaderEvt = new JsonTextReader(new StringReader(respAsString));
                StatusEvent evt = serializer.Deserialize<StatusEvent>(jsonReaderEvt);
                var jsonReader = new JsonTextReader(new StringReader(respAsString));

                switch (evt.Op) {
                    case "metrics":
                        MetricsEvent m = serializer.Deserialize<MetricsEvent>(jsonReader);

                        if (m != null) {
                            MetricsEvent(m.Identities);
                        }
                        break;
                    case "status":
                        TunnelStatusEvent se = serializer.Deserialize<TunnelStatusEvent>(jsonReader);

                        if (se != null) {
                            TunnelStatusEvent(se);
                        }
                        break;
                    case "identity":
                        IdentityEvent id = serializer.Deserialize<IdentityEvent>(jsonReader);

                        if (id != null) {
                            IdentityEvent(id);
                        }
                        break;
                    case "service":
                        ServiceEvent svc = serializer.Deserialize<ServiceEvent>(jsonReader);

                        if (svc != null) {
                            ServiceEvent(svc);
                        }
                        break;
                    case "shutdown":
                        Logger.Debug("Service shutdown has been requested! " + evt.Op);
                        ClientDisconnected("true");
                        break;
                    default:
                        Logger.Debug("unexpected operation! " + evt.Op);
                        break;
                }
            } catch (Exception e) {
                Logger.Debug(e.Message);
            }
        }

        async public Task<ZitiTunnelStatus> debugAsync() {
            try {
                await sendAsync(new ServiceFunction() { Function = "Debug" });
                var rtn = await readAsync<ZitiTunnelStatus>(ipcReader, "debugAsync");
                return rtn;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }
    }
}