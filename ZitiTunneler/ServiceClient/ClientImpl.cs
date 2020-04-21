using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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
        const string namedPipe = @"NetFoundry\tunneler\ipc";
        const string localPipeServer = ".";
        const PipeDirection inOut = PipeDirection.InOut;

        NamedPipeClientStream pipeClient = null;
        StreamWriter writer = null;
        StreamReader reader = null;

        public Client()
        {
            //establish the named pipe to the service
            setupPipe();
        }

        private void setupPipe()
        {
            lock (namedPipeSyncLock)
            {
                if(pipeClient != null)
                {
                    pipeClient.Dispose();
                }
                pipeClient = new NamedPipeClientStream(localPipeServer, namedPipe, inOut);
                writer = new StreamWriter(pipeClient);
                reader = new StreamReader(pipeClient);
                pipeClient.Connect();
            }
        }

        public ZitiTunnelStatus GetStatus()
        {
            send(new ServiceFunction() { Function = "Status" });
            var rtn = read<ZitiTunnelStatus>();
            return rtn;
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


                //serializer.Serialize(Console.Out, newId);
                send(AddIdentityFunction);
                send(newId);
                var resp = read<NewIdentityResponse>();
                //NewIdentityResponse nidresp = (NewIdentityResponse)serializer.Deserialize(sr, typeof(NewIdentityResponse));
                Debug.WriteLine(resp.ToString());

                return resp.Payload;
            }
            catch(IOException ioe)
            {
                //almost certainly a problem with the pipe - recreate the pipe...
                setupPipe();
                throw ioe;
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
            send(new BooleanFunction("TunnelState", onOff));
            read<SvcResponse>();
        }

        public void IdentityOnOff(Identity id)
        {
            string msg = @"{""Function"":""IdentityOnOff"", ""Payload"": { ""fingerprint"" : ""FINGERPRINTHERE"", ""onOff"" : true } }";
            //Console.WriteLine(msg);
            /*
            sw.Write(msg);
            sw.Flush();
            SvcResponse r = (SvcResponse)serializer.Deserialize(sr, typeof(SvcResponse));
            Console.WriteLine(r.ToString());
            */
        }

        /*
        void MainThing(string[] args)
        {
            using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", @"NetFoundry\tunneler\ipc", PipeDirection.InOut))
            {
                // Connect to the pipe or wait until the pipe is available.
                Console.Write("Attempting to connect to pipe...");
                pipeClient.Connect();

                Console.WriteLine("Connected to pipe.");

                GetStatus(sw, sr);

                var enrolledId = NewId(sw, sr);
                if (enrolledId != null)
                {
                    Console.WriteLine(enrolledId.Name);
                }
                Console.WriteLine("");
                Console.WriteLine("");
                Console.WriteLine("Get status 1:");
                GetStatus(sw, sr);

                RemoveIdentity(enrolledId.FingerPrint, sw, sr);

                Console.WriteLine("");
                Console.WriteLine("");
                Console.WriteLine("Get status 2:");
                GetStatus(sw, sr);

                TunnelState(true, sw, sr);
                Console.WriteLine("Get status 3:");
                GetStatus(sw, sr);


















                string resp = "not null for sure";
                if (resp != null)
                {
                    return;
                }

                string msg = @"{""Function"":""Bob"", ""Payload"": { ""arbitrary"": ""some other this is just a value"" } }";
                //Console.WriteLine(msg);
                //Console.Write('\n');
                sw.Write(msg);
                //sw.Write('\n');
                sw.Flush();

                SvcResponse r = (SvcResponse)serializer.Deserialize(sr, typeof(SvcResponse));
                Console.WriteLine(r.ToString());






                msg = @"{""Function"":""GetLogData"", ""Payload"": { ""fingerprint"" : ""put a fingerprint here to remove"" } }";
                //Console.WriteLine(msg);
                sw.Write(msg);
                sw.Flush();

                r = (SvcResponse)serializer.Deserialize(sr, typeof(SvcResponse));
                Console.WriteLine(r.ToString());



                msg = @"{""Function"":""ListIdentities"", ""Payload"": { ""fingerprint"" : ""put a fingerprint here to remove"" } }";
                //Console.WriteLine(msg);
                sw.Write(msg);
                sw.Flush();
                r = (SvcResponse)serializer.Deserialize(sr, typeof(SvcResponse));
                Console.WriteLine(r.ToString());


                msg = @"{""Function"":""OnOff"", ""Payload"": { ""onOff"" : false } }";
                //Console.WriteLine(msg);
                sw.Write(msg);
                sw.Flush();
                r = (SvcResponse)serializer.Deserialize(sr, typeof(SvcResponse));
                Console.WriteLine(r.ToString());


                msg = @"{""Function"":""OnOff"", ""Payload"": { ""onOff"" : true } }";
                //Console.WriteLine(msg);
                sw.Write(msg);
                sw.Flush();
                r = (SvcResponse)serializer.Deserialize(sr, typeof(SvcResponse));
                Console.WriteLine(r.ToString());


                msg = @"{""Function"":""IdentityOnOff"", ""Payload"": { ""fingerprint"" : ""FINGERPRINTHERE"", ""onOff"" : true } }";
                //Console.WriteLine(msg);
                sw.Write(msg);
                sw.Flush();
                r = (SvcResponse)serializer.Deserialize(sr, typeof(SvcResponse));
                Console.WriteLine(r.ToString());


                msg = @"{""Function"":""IdentityOnOff"", ""Payload"": { ""fingerprint"" : ""FINGERPRINTHERE"", ""onOff"" : false } }";
                //Console.WriteLine(msg);
                sw.Write(msg);
                sw.Flush();
                r = (SvcResponse)serializer.Deserialize(sr, typeof(SvcResponse));
                Console.WriteLine(r.ToString());


                msg = @"{""Function"":""Unknown"", ""Payload"": { ""fingerprint"" : ""put a fingerprint here to remove"" } }";
                //Console.WriteLine(msg);
                sw.Write(msg);
                sw.Flush();
                r = (SvcResponse)serializer.Deserialize(sr, typeof(SvcResponse));
                Console.WriteLine(r.ToString());
                Identity rid = (Identity)serializer.Deserialize(sr, typeof(Identity));
                Console.WriteLine(rid.Name);


                pipeClient.WaitForPipeDrain();
                sw.Close();
                sr.Close();
            }
            Console.Write("Press Enter to continue...");
            //Console.ReadLine();
        }

        */
        private void send(object objToSend)
        {
            string json = JsonConvert.SerializeObject(objToSend, Formatting.Indented);

            Debug.WriteLine(json);
            serializer.Serialize(writer, objToSend);
            writer.Flush();
        }

        private T read<T>() where T : SvcResponse
        {
            T resp = (T)serializer.Deserialize(reader, typeof(T));
            if(resp.Code != 0)
            {
                throw new ServiceException(resp.Message, resp.Code, resp.Error);
            }
            return resp;
        }
    }
}