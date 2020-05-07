using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;

using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// The implementation will abstract away the setup of the communication to
/// the service. This implementation will communicate to the service over a
/// a NamedPipe.
/// 
/// All communication is effectively serial - one or more messages sent and 
/// one or more messages returned.
/// 
/// </summary>
namespace ZitiTunneler.ServiceClient
{

    internal class Client
    {
        public event EventHandler<TunnelStatus> OnTunnelStatusUpdate;
        public event EventHandler<Metrics> OnMetricsUpdate;

        protected virtual void ZitiTunnelStatusUpdate(TunnelStatus e)
        {
            EventHandler<TunnelStatus> handler = OnTunnelStatusUpdate;
            if (handler != null)
            {
                handler(this, e);
            }
        }
        protected virtual void MetricsUpdate(Metrics e)
        {
            EventHandler<Metrics> handler = OnMetricsUpdate;
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

        public Client()
        {
            //establish the named pipe to the service
            setupPipe();
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
                if (pipeClient != null)
                {
                    pipeClient.Dispose();
                }
                pipeClient = new NamedPipeClientStream(localPipeServer, ipcPipe, inOut);
                try
                {
                    ipcWriter = new StreamWriter(pipeClient);
                    ipcReader = new StreamReader(pipeClient);
                    pipeClient.Connect(ServiceConnectTimeout);
                }
                catch(Exception ex)
                {
                    //todo: better error
                    Debug.WriteLine("There was a problem connecting to the service. " + ex.Message);
                    try
                    {
                        pipeClient?.Close();
                    }
                    catch (Exception exc)
                    {
                        //intentionally ignored
                        Debug.WriteLine(exc.Message);
                    }
                    ipcReader = null;
                    ipcWriter = null;
                    pipeClient = null;
                }

                Task.Run(() => {
                    Console.WriteLine("THREAD BEGINS");
                    NamedPipeClientStream eventClient = new NamedPipeClientStream(localPipeServer, eventPipe, PipeDirection.In);
                    eventClient.Connect();
                    StreamReader eventReader = new StreamReader(eventClient);
                    while (true)
                    {
                        var r = read<StatusUpdateResponse>("event: ", eventReader);
                        if (eventReader.EndOfStream)
                        {
                            break;
                        }
                        MetricsUpdate(r.Payload.Metrics);
                    }
                    Console.WriteLine("THREAD DONE");
                });
            }
        }

        public ZitiTunnelStatus GetStatus()
        {
            try
            {
                send(new ServiceFunction() { Function = "Status" });
                var rtn = read<ZitiTunnelStatus>("   ipc: ", ipcReader);
                return rtn;
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                setupPipe();
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
                var resp = read<IdentityResponse>("   ipc: ", ipcReader);
                Debug.WriteLine(resp.ToString());

                return resp.Payload;
            }
            catch (IOException)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                setupPipe();
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
                var r = read<SvcResponse>("   ipc: ", ipcReader);
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                setupPipe();
                throw ioe;
            }
        }

        public void SetTunnelState(bool onOff)
        {
            try
            {
                send(new BooleanFunction("TunnelState", onOff));
                read<SvcResponse>("   ipc: ", ipcReader);
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                setupPipe();
                throw ioe;
            }
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

        public Identity IdentityOnOff(string fingerprint, bool onOff)
        {
            try
            {
                send(new IdentityToggleFunction(fingerprint, onOff));
                IdentityResponse idr = read<IdentityResponse>("   ipc: ", ipcReader);
                return idr.Payload;
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                setupPipe();
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
                    if (ipcWriter == null)
                    {
                        setupPipe();
                    }
                    string toSend = JsonConvert.SerializeObject(objToSend, Formatting.None);

                    if (toSend?.Trim() != null)
                    {
                        Debug.WriteLine("===============  sending message =============== ");
                        Debug.WriteLine(toSend);
                        ipcWriter.Write(toSend);
                        ipcWriter.Write('\n');
                        Debug.WriteLine("=============== flushing message =============== ");
                        ipcWriter.Flush();
                        Debug.WriteLine("===============     sent message =============== ");
                        Debug.WriteLine("");
                        Debug.WriteLine("");
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
                    setupPipe();
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

        private T read<T>(string prefix, StreamReader reader) where T : SvcResponse
        {
            try
            {
                int emptyCount = 1;

                Debug.WriteLine(prefix + "===============  reading message =============== " + emptyCount);
                string respAsString = reader.ReadLine();
                Debug.WriteLine(respAsString);
                Debug.WriteLine(prefix + "===============     read message =============== " + emptyCount);
                while (string.IsNullOrEmpty(respAsString?.Trim()))
                {
                    //T resp = (T)serializer.Deserialize(reader, typeof(T));
                    if (reader.EndOfStream)
                    {
                        throw new Exception("the pipe has closed");
                    }
                    Debug.WriteLine("Received empty payload - continuing to read until a payload is received");
                    //now how'd that happen...
                    Debug.WriteLine(prefix + "===============  reading message =============== " + emptyCount);
                    respAsString = reader.ReadLine();
                    Debug.WriteLine(respAsString);
                    Debug.WriteLine(prefix + "===============     read message =============== " + emptyCount);
                    emptyCount++;
                    if (emptyCount > 5)
                    {
                        Debug.WriteLine("are we there yet? " + reader.EndOfStream);
                        //that's just too many...
                        setupPipe();
                        return null;
                    }
                }
                Debug.WriteLine("");
                Debug.WriteLine("");

                T resp = (T)serializer.Deserialize(new StringReader(respAsString), typeof(T));

                if (resp.Code != 0)
                {
                    throw new ServiceException(resp.Message, resp.Code, resp.Error);
                }
                return resp;
            }
            catch (IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                setupPipe();
                throw ioe;
            }
            catch (Exception ee)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                throw ee;
            }
        }
    }
}