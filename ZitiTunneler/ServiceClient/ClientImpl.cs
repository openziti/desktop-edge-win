using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;

using Newtonsoft.Json;


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
        JsonSerializer serializer = new JsonSerializer();

        private object namedPipeSyncLock = new object();
        const string ipcPipe = @"NetFoundry\tunneler\ipc";
        const string logPipe = @"NetFoundry\tunneler\logs";
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
                /*
                PipeSecurity pipeSecurity = CreateSystemIOPipeSecurity();
                var pipeServer = new NamedPipeServerStream(ipcPipe,
                                                       PipeDirection.InOut,
                                                       1,
                                                       PipeTransmissionMode.Message,
                                                       PipeOptions.Asynchronous,
                                                       0x4000,
                                                       0x400,
                                                       pipeSecurity,
                                                       HandleInheritability.Inheritable);

                pipeClient = new NamedPipeClientStream(localPipeServer,
                             ipcPipe,
                             PipeAccessRights.Read | PipeAccessRights.Write,
                             PipeOptions.None,
                             System.Security.Principal.TokenImpersonationLevel.None,
                             System.IO.HandleInheritability.None);
                             */
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
                        ipcReader?.Close();
                        ipcWriter?.Close();
                        pipeClient?.Close();
                    }
                    catch
                    {
                        //intentionally ignored
                    }
                    ipcReader = null;
                    ipcWriter = null;
                    pipeClient = null;
                    throw;
                }
            }
        }

        public ZitiTunnelStatus GetStatus()
        {
            try
            {
                send(new ServiceFunction() { Function = "Status" });
                var rtn = read<ZitiTunnelStatus>();
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
                var resp = read<IdentityResponse>();
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
                var r = read<SvcResponse>();
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
                read<SvcResponse>();
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
                IdentityResponse idr = read<IdentityResponse>();
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
            try
            {
                string toSend = JsonConvert.SerializeObject(objToSend, Formatting.None);
                /*
                StringWriter w = new StringWriter();
                serializer.Serialize(w, objToSend);

                string toSend = w.ToString();
                */
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
            }
            catch(Exception ex)
            {
                //if this fails it's usually because the writer is null/invalid. throwing IOException
                //will trigger the pipe to rebuild
                throw new IOException("Unexpected error when sending data to service. " + ex.Message);
            }
        }

        private T read<T>() where T : SvcResponse
        {
            try
            {
                int emptyCount = 1;

                Debug.WriteLine("===============  reading message =============== " + emptyCount);
                string respAsString = ipcReader.ReadLine();
                Debug.WriteLine(respAsString);
                Debug.WriteLine("===============     read message =============== " + emptyCount);
                while (string.IsNullOrEmpty(respAsString?.Trim()))
                {
                    //T resp = (T)serializer.Deserialize(reader, typeof(T));
                    if (ipcReader.EndOfStream)
                    {
                        throw new Exception("the pipe has closed");
                    }
                    Debug.WriteLine("Received empty payload - continuing to read until a payload is received");
                    //now how'd that happen...
                    Debug.WriteLine("===============  reading message =============== " + emptyCount);
                    respAsString = ipcReader.ReadLine();
                    Debug.WriteLine(respAsString);
                    Debug.WriteLine("===============     read message =============== " + emptyCount);
                    emptyCount++;
                    if (emptyCount > 5)
                    {
                        Debug.WriteLine("are we there yet? " + ipcReader.EndOfStream);
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
        }
    }
}