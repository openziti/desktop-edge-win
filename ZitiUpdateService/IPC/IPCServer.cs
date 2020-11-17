using System;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;
using NLog;
using System.Text.Json;

namespace ZitiUpdateService.IPC {
    public class IPCServer {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

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
                    //var streamReader = new StreamReader(ss);
                    //var line = await streamReader.ReadLineAsync();

                    /*
                    var writer = new StreamWriter(ss);
                    for (int i = 0; i < 10; i++) {
                        writer.Write($"Hello from c# : {clientIndex}:{i} - Thread:{System.Threading.Thread.CurrentThread.ManagedThreadId}" );
                        writer.Write("\n");
                        writer.Flush();
                    }
                    */
                    JsonDocument d;
                    while((d = await DeserializeFromStream(ss)) != null) {
                        Logger.Info("json object received: " + d.ToString());
                    }

                    ss.WaitForPipeDrain();

                    //Logger.Debug($"read from pipe client: {streamReader.ReadLine()}");
                } catch(Exception e) {
                    Logger.Error(e, "Unexpected erorr when reading from or writing to a client pipe.");
                }
            }
        }

        public async static Task<JsonDocument> DeserializeFromStream(Stream stream) {
            JsonDocument jd = await JsonSerializer.DeserializeAsync<JsonDocument>(stream);
            return jd;
        }
    }
}
