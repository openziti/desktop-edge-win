using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;

using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Remoting.Contexts;
using System.Windows.Interop;
using System.Windows.Documents;

/// <summary>
/// The implementation will abstract away the setup of the communication to
/// the service. This implementation will communicate to the service over a
/// a NamedPipe.
/// 
/// All communication is effectively serial - one or more messages sent and 
/// one or more messages returned.
/// 
/// </summary>
namespace ZitiDesktopEdge.ServiceClient
{
    public enum LogLevelEnum
    {
        FATAL = 0,
        ERROR = 1,
        WARN = 2,
        INFO = 3,
        DEBUG = 4,
        TRACE = 5,
        VERBOSE = 6,
    }

    internal class Client
    {

        public const int EXPECTED_API_VERSION = 1;

        public event EventHandler<TunnelStatusEvent> OnTunnelStatusEvent;
        public event EventHandler<List<Identity>> OnMetricsEvent;
        public event EventHandler<IdentityEvent> OnIdentityEvent;
        public event EventHandler<ServiceEvent> OnServiceEvent;
        public event EventHandler<object> OnClientConnected;
        public event EventHandler<object> OnClientDisconnected;

        protected virtual void TunnelStatusEvent(TunnelStatusEvent e)
        {
            EventHandler<TunnelStatusEvent> handler = OnTunnelStatusEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void MetricsEvent(List<Identity> e)
        {
            EventHandler<List<Identity>> handler = OnMetricsEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void IdentityEvent(IdentityEvent e)
        {
            EventHandler<IdentityEvent> handler = OnIdentityEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void ServiceEvent(ServiceEvent e)
        {
            EventHandler<ServiceEvent> handler = OnServiceEvent;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void ClientConnected(object e)
        {
            Connected = true;
            this.Reconnecting = false;
            EventHandler<object> handler = OnClientConnected;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void ClientDisconnected(object e)
        {
            Reconnect();
            Connected = false;
            EventHandler<object> handler = OnClientDisconnected;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        JsonSerializer serializer = new JsonSerializer();

        private object namedPipeSyncLock = new object();
        const string ipcPipe = @"NetFoundry\tunneler\ipc";
        const string logPipe = @"NetFoundry\tunneler\logs";
        const string eventPipe = @"NetFoundry\tunneler\events";
        const string localPipeServer = ".";
        const PipeDirection inOut = PipeDirection.InOut;
        const int ServiceConnectTimeout = 500;

        NamedPipeClientStream pipeClient = null;
        StreamWriter ipcWriter = null;
        StreamReader ipcReader = null;

        NamedPipeClientStream eventClient = null;
        bool _extendedDebug = false; //set this to true if you need to diagnose issues with the service comms

        public Client()
        {
            try
            {
                string extDebugEnv = Environment.GetEnvironmentVariable("ZITI_EXTENDED_DEBUG");
                if (extDebugEnv != null)
                {
                    if (bool.Parse(extDebugEnv))
                    {
                        _extendedDebug = true;
                    }
                }
            }
            catch(Exception ex)
            {
                Debug.WriteLine("EXCEPTION IN CLIENT CONNECT: " + ex.Message);
                //if this happens - enter retry mode...
                Reconnect();
            }
        }

        public bool Reconnecting { get; set; }
        public bool Connected { get; set; }

        public void Connect()
        {
            //establish the named pipe to the service
            setupPipe();
        }

        public void Reconnect()
        {
            if (Reconnecting)
            {
                Debug.WriteLine("Already in reconnect mode.");
                return;
            }
            else
            {
                Reconnecting = true;
            }

            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        Thread.Sleep(2500); //wait 500ms and try to reconnect...
                        Debug.WriteLine("Attempting to connect to service...");
                        pipeClient?.Close();
                        eventClient?.Close();
                        setupPipe();

                        if (Connected)
                        {
                            Debug.WriteLine("Connected to the service - exiting reconect loop");
                            this.Connected = true;
                            Reconnecting = false;
                            return;
                        }
                        else
                        {
                            //ClientDisconnected(null);
                        }
                    }
                    catch
                    {
                        //fire the event and just try it all over....
                        //ClientDisconnected(null);
                    }
                    Debug.WriteLine("Reconnect failed. Trying again...");
                }
            });
        }

        PipeSecurity CreateSystemIOPipeSecurity()
        {
            PipeSecurity pipeSecurity = new PipeSecurity();

            var id = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            // Allow Everyone read and write access to the pipe. 
            pipeSecurity.SetAccessRule(new PipeAccessRule(id, PipeAccessRights.ReadWrite, AccessControlType.Allow));

            return pipeSecurity;
        }

        private void setupPipe()
        {
            lock (namedPipeSyncLock)
            {
                pipeClient = new NamedPipeClientStream(localPipeServer, ipcPipe, inOut);
                eventClient = new NamedPipeClientStream(localPipeServer, eventPipe, PipeDirection.In);

                try
                {
                    eventClient.Connect(ServiceConnectTimeout);
                    pipeClient.Connect(ServiceConnectTimeout);
                    ipcWriter = new StreamWriter(pipeClient);
                    ipcReader = new StreamReader(pipeClient);
                    ClientConnected(null);
                }
                catch(Exception ex)
                {
                    throw new ServiceException("Could not connect to the service.", 1, ex.Message);
                }

                Task.Run(() => { //hack for now until it's async...
                    try
                    {
                        StreamReader eventReader = new StreamReader(eventClient);
                        while (true)
                        {
                            if (eventReader.EndOfStream)
                            {
                                break;
                            }

                            processEvent(eventReader);
                        }
                    }
                    catch(Exception ex)
                    {
                        Debug.WriteLine("unepxected error: " + ex.ToString());
                    }
                    
                    // since this thread is always sitting waiting to read
                    // it should be the only one triggering this event
                    ClientDisconnected(null); 
                });
            }
        }

        public ZitiTunnelStatus GetStatus()
        {
            try
            {
                send(new ServiceFunction() { Function = "Status" });
                var rtn = read<ZitiTunnelStatus>(ipcReader);
                return rtn;
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        ServiceFunction AddIdentityFunction = new ServiceFunction() { Function = "AddIdentity" };

        public Identity AddIdentity(string identityName, bool activate, string jwt)
        {
            try
            {
                Identity id = new Identity
                {
                    Active = activate,
                    Name = identityName
                };

                NewIdentity newId = new NewIdentity()
                {
                    Id = id,
                    Flags = new EnrollmentFlags()
                    {
                        JwtString = jwt
                    }
                };

                send(AddIdentityFunction);
                send(newId);
                var resp = read<IdentityResponse>(ipcReader);
                Debug.WriteLine(resp.ToString());
                if(resp.Code != 0)
                {
                    throw new ServiceException(resp.Message, resp.Code, resp.Error);
                }
                return resp.Payload;
            }
            catch (IOException)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw;
            }
        }


        public void RemoveIdentity(string fingerPrint)
        {
            if (string.IsNullOrEmpty(fingerPrint))
            {
                //nothing to do...
                return;
            }

            try
            {
                FingerprintFunction removeFunction = new FingerprintFunction()
                {
                    Function = "RemoveIdentity",
                    Payload = new FingerprintPayload() { Fingerprint = fingerPrint }
                };
                send(removeFunction);
                var r = read<SvcResponse>(ipcReader);
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        private void checkConnected()
        {
            if (this.Reconnecting)
            {
                throw new ServiceException("Client is not connected", 2, "Cannot use the client at this time, it is reconnecting");
            }
            if (!this.Connected)
            {
                throw new ServiceException("Client is not connected", 2, "Cannot use the client at this time, it is not connected");
            }
        }

        public void SetTunnelState(bool onOff)
        {
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

        public string GetLogs()
        {
            try
            {
                NamedPipeClientStream logClient = new NamedPipeClientStream(localPipeServer, logPipe, PipeDirection.In);
                StreamReader logReader = new StreamReader(logClient);
                logClient.Connect(ServiceConnectTimeout);

                string content = logReader.ReadToEnd();

                //ugly hack to turn ansi escaping to not... _bleck_
                //todo: fix this :point_up:
                content = new System.Text.RegularExpressions.Regex(@"\x1B\[[^@-~]*[@-~]").Replace(content, "");
                return content;
            }
            catch
            {
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

        public void SetLogLevel(LogLevelEnum level)
        {
            try
            {
                send(new SetLogLevelFunction(Enum.GetName(level.GetType(), level)));
                SvcResponse resp = read<SvcResponse>(ipcReader);
                return;
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        public Identity IdentityOnOff(string fingerprint, bool onOff)
        {
            try
            {
                send(new IdentityToggleFunction(fingerprint, onOff));
                IdentityResponse idr = read<IdentityResponse>(ipcReader);
                return idr.Payload;
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }

        private void send(object objToSend)
        {
            bool retried = false;
            while (true)
            {
                try
                {
                    string toSend = JsonConvert.SerializeObject(objToSend, Formatting.None);

                    if (toSend?.Trim() != null)
                    {
                        debugServiceCommunication("===============  sending message =============== ");
                        debugServiceCommunication(toSend);
                        ipcWriter.Write(toSend);
                        ipcWriter.Write('\n');
                        debugServiceCommunication("=============== flushing message =============== ");
                        ipcWriter.Flush();
                        debugServiceCommunication("===============     sent message =============== ");
                        debugServiceCommunication("");
                        debugServiceCommunication("");
                    }
                    else
                    {
                        Debug.WriteLine("NOT sending empty object??? " + objToSend?.ToString());
                    }
                    break;
                }
                catch (IOException ioe)
                {
                    //almost certainly a problem with the pipe - recreate the pipe... try one more time.
                    //setupPipe();
                    if (retried)
                    {
                        //we tried - throw the error...
                        throw ioe;
                    }
                    else
                    {
                        retried = true; //fall back through to the while and try again
                    }
                }
                catch (Exception ex)
                {
                    //if this fails it's usually because the writer is null/invalid. throwing IOException
                    //will trigger the pipe to rebuild
                    throw new IOException("Unexpected error when sending data to service. " + ex.Message);
                }
            }
        }

        private T read<T>(StreamReader reader) where T : SvcResponse
        {
            string respAsString = readMessage(reader);
            T resp = (T)serializer.Deserialize(new StringReader(respAsString), typeof(T));
            return resp;
        }

        private void processEvent(StreamReader reader)
        {
            try
            {
                string respAsString = readMessage(reader);
                debugServiceCommunication("----------------------------------------------------------------------");
                debugServiceCommunication(respAsString);
                debugServiceCommunication("----------------------------------------------------------------------");
                StatusEvent evt = (StatusEvent)serializer.Deserialize(new StringReader(respAsString), typeof(StatusEvent));

                switch (evt.Op)
                {
                    case "metrics":
                        MetricsEvent m = (MetricsEvent)serializer.Deserialize(new StringReader(respAsString), typeof(MetricsEvent));

                        if (m != null)
                        {
                            this.MetricsEvent(m.Identities);
                        }
                        break;
                    case "status":
                        TunnelStatusEvent se = (TunnelStatusEvent)serializer.Deserialize(new StringReader(respAsString), typeof(TunnelStatusEvent));

                        if (se != null)
                        {
                            this.TunnelStatusEvent(se);
                        }
                        break;
                    case "identity":
                        IdentityEvent id = (IdentityEvent)serializer.Deserialize(new StringReader(respAsString), typeof(IdentityEvent));

                        if (id != null)
                        {
                            this.IdentityEvent(id);
                        }
                        break;
                    case "service":
                        ServiceEvent svc = (ServiceEvent)serializer.Deserialize(new StringReader(respAsString), typeof(ServiceEvent));

                        if (svc != null)
                        {
                            this.ServiceEvent(svc);
                        }
                        break;
                    default:
                        Debug.WriteLine("unexpected operation! " + evt.Op);
                        break;
                }
            } catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
        }

        public string readMessage(StreamReader reader)
        {
            try
            {
                if (reader.EndOfStream)
                {
                    throw new ServiceException("the pipe has closed", 0, "end of stream reached");
                }
                int emptyCount = 1; //just a stop gap in case something crazy happens in the communication

                debugServiceCommunication( "===============  reading message =============== " + emptyCount);
                string respAsString = reader.ReadLine();
                debugServiceCommunication(respAsString);
                debugServiceCommunication("===============     read message =============== " + emptyCount);
                while (string.IsNullOrEmpty(respAsString?.Trim()))
                {
                    if (reader.EndOfStream)
                    {
                        throw new Exception("the pipe has closed");
                    }
                    debugServiceCommunication("Received empty payload - continuing to read until a payload is received");
                    //now how'd that happen...
                    debugServiceCommunication("===============  reading message =============== " + emptyCount);
                    respAsString = reader.ReadLine();
                    debugServiceCommunication(respAsString);
                    debugServiceCommunication("===============     read message =============== " + emptyCount);
                    emptyCount++;
                    if (emptyCount > 5)
                    {
                        Debug.WriteLine("are we there yet? " + reader.EndOfStream);
                        //that's just too many...
                        //setupPipe();
                        return null;
                    }
                }
                debugServiceCommunication("");
                debugServiceCommunication("");
                return respAsString;
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe
                Debug.WriteLine("io error in read: " + ioe.Message);
                ClientDisconnected(null);
                throw ioe;
            }
            catch (Exception ee)
            {
                //almost certainly a problem with the pipe
                Debug.WriteLine("unexpected error in read: " + ee.Message);
                ClientDisconnected(null);
                throw ee;
            }
        }

        private void debugServiceCommunication(string msg)
        {
            if (_extendedDebug)
            {
                Debug.WriteLine(msg);
            }
        }
        public ZitiTunnelStatus debug()
        {
            try
            {
                send(new ServiceFunction() { Function = "Debug" });
                var rtn = read<ZitiTunnelStatus>(ipcReader);
                return rtn;
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                //setupPipe();
                throw ioe;
            }
        }
    }
}