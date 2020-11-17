using System;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;
using Newtonsoft.Json;

namespace ZitiUpdateService.IPC {
    public class IPCServer {
        /*
            InteractivelyLoggedInUser = "(A;;GRGW;;;IU)" //generic read/write. We will want to tune this to a specific group but that is not working with Windows 10 home at the moment
            System                    = "(A;;FA;;;SY)"
            BuiltinAdmins             = "(A;;FA;;;BA)"
            LocalService              = "(A;;FA;;;LS)"
        */
        private string pipeName;
        public IPCServer(string pipeName) {
            this.pipeName = pipeName;
        }

        async public Task acceptAsync() {
            int idx = 0;
            while (true) {
                var namedPipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte);
                await namedPipeServer.WaitForConnectionAsync();
                var t = Task.Run(async ()=>{
                    await handleClientAsync(namedPipeServer, idx);
                    idx--;
                });
                idx++;
            }
        }

        async public Task handleClientAsync(NamedPipeServerStream ss, int clientIndex) {
            using (ss) {
                try { 
                    var streamReader = new StreamReader(ss);
                    var line = await streamReader.ReadLineAsync();

                    var writer = new StreamWriter(ss);
                    for (int i = 0; i < 10; i++) {
                        writer.Write($"Hello from c# : {clientIndex}:{i} - Thread:{System.Threading.Thread.CurrentThread.ManagedThreadId}" );
                        writer.Write("\n");
                        writer.Flush();
                    }
                    ss.WaitForPipeDrain();

                    Console.WriteLine($"read from pipe client: {streamReader.ReadLine()}");
                    ss.Dispose();
                } catch(Exception e) {
                    Console.WriteLine("meh - error: " + e.Message);
                }
            }
        }

        public static object DeserializeFromStream(Stream stream) {
            var serializer = new JsonSerializer();

            using (var sr = new StreamReader(stream))
            using (var jsonTextReader = new JsonTextReader(sr)) {
                
                return serializer.Deserialize(jsonTextReader);
            }
        }
    }
}
