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
    public class MonitorClient {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public const int EXPECTED_API_VERSION = 1;

        public event EventHandler<TunnelStatusEvent> OnTunnelStatusEvent;
        public event EventHandler<List<Identity>> OnMetricsEvent;
        public event EventHandler<IdentityEvent> OnIdentityEvent;
        public event EventHandler<ServiceEvent> OnServiceEvent;
        public event EventHandler<object> OnClientConnected;
        public event EventHandler<object> OnClientDisconnected;
        public event EventHandler<StatusEvent> OnShutdownEvent;

        private JsonSerializer serializer = new JsonSerializer() { Formatting = Formatting.None };

        public bool CleanShutdown { get; set; }

        protected virtual void ShutdownEvent(StatusEvent e) {
            Logger.Debug("Clean shutdown detected from ziti");
            CleanShutdown = true;
            OnShutdownEvent?.Invoke(this, e);
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

        protected virtual void ClientConnected(object e) {
            Connected = true;
            Reconnecting = false;
            CleanShutdown = false;
            Logger.Debug("CleanShutdown set to false");
            OnClientConnected?.Invoke(this, e);
        }

        protected virtual void ClientDisconnected(object e) {
            Reconnect();
            Connected = false;
            OnClientDisconnected?.Invoke(this, e);
        }

        private object namedPipeSyncLock = new object();
        const string ipcPipe = @"OpenZiti\ziti-monitor\ipc";
        const string logPipe = @"OpenZiti\ziti-monitor\logs";
        const string eventPipe = @"OpenZiti\ziti-monitor\events";
        const string localPipeServer = ".";
        const PipeDirection inOut = PipeDirection.InOut;
        const int ServiceConnectTimeout = 500;

        NamedPipeClientStream pipeClient = null;
        StreamWriter writer = null;
        StreamReader ipcReader = null;

        NamedPipeClientStream eventClient = null;
#if DEBUG
        bool _extendedDebug = false; //set ZITI_EXTENDED_DEBUG env var to true if you want to diagnose issues with the service comms
#else
        bool _extendedDebug = false;
#endif

        public MonitorClient() {
            try {
                string extDebugEnv = Environment.GetEnvironmentVariable("ZITI_EXTENDED_DEBUG");
                if (extDebugEnv != null) {
                    if (bool.Parse(extDebugEnv)) {
                        _extendedDebug = true;
                    }
                }
            } catch (Exception ex) {
                Logger.Debug("EXCEPTION IN CLIENT CONNECT: " + ex.Message);
                //if this happens - enter retry mode...
                Reconnect();
            }
        }

        public bool Reconnecting { get; set; }
        public bool Connected { get; set; }

        public void Connect() {
            //establish the named pipe to the service
            setupPipe();
        }

        public void WaitForConnection() {
            while (Reconnecting || !Connected) {
                Task.Delay(100).Wait();
            }
        }

        public void Reconnect() {
            if (Reconnecting) {
                Logger.Debug("Already in reconnect mode.");
                return;
            } else {
                Reconnecting = true;
            }

            Task.Run(() => {
                Logger.Info("service is down. attempting to connect to service...");

                DateTime reconnectStart = DateTime.Now;
                DateTime logAgainAfter = reconnectStart + TimeSpan.FromSeconds(1);

                while (true) {
                    try {
                        Thread.Sleep(2500);
                        pipeClient?.Close();
                        eventClient?.Close();
                        setupPipe();

                        if (Connected) {
                            Logger.Debug("Connected to the service - exiting reconect loop");
                            Connected = true;
                            Reconnecting = false;
                            return;
                        } else {
                            //ClientDisconnected(null);
                        }
                    } catch {
                        var now = DateTime.Now;
                        if (now > logAgainAfter) {
                            Logger.Debug("Reconnect failed. Trying again...");
                            var duration = now - reconnectStart;
                            if (duration > TimeSpan.FromHours(1)) {
                                Logger.Info("reconnect has not completed and has been running for {0} hours", duration.TotalHours);
                                logAgainAfter += TimeSpan.FromHours(1);
                            } else if (duration > TimeSpan.FromMinutes(1)) {
                                Logger.Info("reconnect has not completed and has been running for {0} minutes", duration.TotalMinutes);
                                logAgainAfter += TimeSpan.FromMinutes(1);
                            } else {
                                logAgainAfter += TimeSpan.FromSeconds(1);
                            }
                        }
                    }
                }
            });
        }

        private void setupPipe() {
            lock (namedPipeSyncLock) {
                pipeClient = new NamedPipeClientStream(localPipeServer, ipcPipe, inOut);
                eventClient = new NamedPipeClientStream(localPipeServer, eventPipe, PipeDirection.In);

                try {
                    eventClient.Connect(ServiceConnectTimeout);
                    pipeClient.Connect(ServiceConnectTimeout);
                    writer = new StreamWriter(pipeClient);
                    ipcReader = new StreamReader(pipeClient);
                    ClientConnected(null);
                } catch (Exception ex) {
                    throw new ServiceException("Could not connect to the service.", 1, ex.Message);
                }

                Task.Run(() => { //hack for now until it's async...
                    try {
                        StreamReader eventReader = new StreamReader(eventClient);
                        while (true) {
                            if (eventReader.EndOfStream) {
                                break;
                            }

                            processEvent(eventReader);
                        }
                    } catch (Exception ex) {
                        Logger.Debug("unepxected error: " + ex.ToString());
                    }

                    // since this thread is always sitting waiting to read
                    // it should be the only one triggering this event
                    ClientDisconnected(null);
                });
            }
        }

        public ZitiTunnelStatus GetStatus() {
            try {
                send(new ServiceFunction() { Function = "Status" });
                var rtn = read<ZitiTunnelStatus>(ipcReader);
                return rtn;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        ServiceFunction AddIdentityFunction = new ServiceFunction() { Function = "AddIdentity" };

        public Identity AddIdentity(string identityName, bool activate, string jwt) {
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

                send(AddIdentityFunction);
                send(newId);
                var resp = read<IdentityResponse>(ipcReader);
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


        public void RemoveIdentity(string fingerPrint) {
            if (string.IsNullOrEmpty(fingerPrint)) {
                //nothing to do...
                return;
            }

            try {
                FingerprintFunction removeFunction = new FingerprintFunction() {
                    Function = "RemoveIdentity",
                    Payload = new FingerprintPayload() { Fingerprint = fingerPrint }
                };
                send(removeFunction);
                var r = read<SvcResponse>(ipcReader);
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

        public void SetLogLevel(string level) {
            try {
                send(new SetLogLevelFunction(level));
                SvcResponse resp = read<SvcResponse>(ipcReader);
                return;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        public void SetLogLevel(LogLevelEnum level) {
            try {
                send(new SetLogLevelFunction(Enum.GetName(level.GetType(), level)));
                SvcResponse resp = read<SvcResponse>(ipcReader);
                return;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        public Identity IdentityOnOff(string fingerprint, bool onOff) {
            try {
                send(new IdentityToggleFunction(fingerprint, onOff));
                IdentityResponse idr = read<IdentityResponse>(ipcReader);
                return idr.Payload;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        private void send(object objToSend) {
            bool retried = false;
            while (true) {
                try {
                    string toSend = JsonConvert.SerializeObject(objToSend, Formatting.None);

                    if (toSend?.Trim() != null) {
                        debugServiceCommunication("===============  sending message =============== ");
                        debugServiceCommunication(toSend);
                        writer.Write(toSend);
                        writer.Write('\n');
                        debugServiceCommunication("=============== flushing message =============== ");
                        writer.Flush();
                        debugServiceCommunication("===============     sent message =============== ");
                        debugServiceCommunication("");
                        debugServiceCommunication("");
                    } else {
                        Logger.Debug("NOT sending empty object??? " + objToSend?.ToString());
                    }
                    break;
                } catch (IOException ioe) {
                    //almost certainly a problem with the pipe - recreate the pipe... try one more time.
                    //setupPipe();
                    if (retried) {
                        //we tried - throw the error...
                        throw ioe;
                    } else {
                        retried = true; //fall back through to the while and try again
                    }
                } catch (Exception ex) {
                    //if this fails it's usually because the writer is null/invalid. throwing IOException
                    //will trigger the pipe to rebuild
                    throw new IOException("Unexpected error when sending data to service. " + ex.Message);
                }
            }
        }

        private T read<T>(StreamReader reader) where T : SvcResponse {
            string respAsString = readMessage(reader);
            //            T resp = JsonSerializer.Deserialize<T>(new JsonStr(respAsString));
            T resp = (T)serializer.Deserialize(new StringReader(respAsString), typeof(T));
            return resp;
        }

        private void processEvent(StreamReader reader) {
            try {
                string respAsString = readMessage(reader);
                var jsonReader = new JsonTextReader(new StringReader(respAsString));
                StatusEvent evt = serializer.Deserialize<StatusEvent>(jsonReader);

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

                        break;
                    default:
                        Logger.Debug("unexpected operation! " + evt.Op);
                        break;
                }
            } catch (Exception e) {
                Logger.Debug(e.Message);
            }
        }

        public string readMessage(StreamReader reader) {
            try {
                if (reader.EndOfStream) {
                    throw new ServiceException("the pipe has closed", 0, "end of stream reached");
                }
                int emptyCount = 1; //just a stop gap in case something crazy happens in the communication

                debugServiceCommunication("===============  reading message =============== " + emptyCount);
                string respAsString = reader.ReadLine();
                debugServiceCommunication(respAsString);
                debugServiceCommunication("===============     read message =============== " + emptyCount);
                while (string.IsNullOrEmpty(respAsString?.Trim())) {
                    if (reader.EndOfStream) {
                        throw new Exception("the pipe has closed");
                    }
                    debugServiceCommunication("Received empty payload - continuing to read until a payload is received");
                    //now how'd that happen...
                    debugServiceCommunication("===============  reading message =============== " + emptyCount);
                    respAsString = reader.ReadLine();
                    debugServiceCommunication(respAsString);
                    debugServiceCommunication("===============     read message =============== " + emptyCount);
                    emptyCount++;
                    if (emptyCount > 5) {
                        Logger.Debug("are we there yet? " + reader.EndOfStream);
                        //that's just too many...
                        //setupPipe();
                        return null;
                    }
                }
                debugServiceCommunication("");
                debugServiceCommunication("");
                return respAsString;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe
                Logger.Debug("io error in read: " + ioe.Message);
                ClientDisconnected(null);
                throw ioe;
            } catch (Exception ee) {
                //almost certainly a problem with the pipe
                Logger.Debug("unexpected error in read: " + ee.Message);
                ClientDisconnected(null);
                throw ee;
            }
        }

        private void debugServiceCommunication(string msg) {
            if (_extendedDebug) {
                Logger.Debug(msg);
            }
        }
        public ZitiTunnelStatus debug() {
            try {
                send(new ServiceFunction() { Function = "Debug" });
                var rtn = read<ZitiTunnelStatus>(ipcReader);
                return rtn;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }
    }
}