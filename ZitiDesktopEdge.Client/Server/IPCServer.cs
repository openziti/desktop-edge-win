using System;
using System.Threading.Tasks;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;

using Newtonsoft.Json;
using NLog;

using ZitiDesktopEdge.DataStructures;

namespace ZitiDesktopEdge.Server {
    public class IPCServer {
        public static string PipeName = @"OpenZiti\tunneler\monitoripc";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static int BUFFER_SIZE = 16 * 1024;

        private JsonSerializer serializer = new JsonSerializer() { Formatting = Formatting.None };

        /*
            InteractivelyLoggedInUser = "(A;;GRGW;;;IU)" //generic read/write. We will want to tune this to a specific group but that is not working with Windows 10 home at the moment
            System                    = "(A;;FA;;;SY)"
            BuiltinAdmins             = "(A;;FA;;;BA)"
            LocalService              = "(A;;FA;;;LS)"
        */
        private string pipeName;
        public IPCServer() {
            this.pipeName = IPCServer.PipeName;
        }

        async public Task acceptAsync() {
            int idx = 0;

            PipeSecurity pipeSecurity = new PipeSecurity();
            var id = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            // Allow AuthenticatedUserSid read and write access to the pipe. 
            pipeSecurity.SetAccessRule(new PipeAccessRule(id, PipeAccessRights.ReadWrite, AccessControlType.Allow));

            while (true) {
                var namedPipeServer = new NamedPipeServerStream(
                    pipeName, 
                    PipeDirection.InOut, 
                    NamedPipeServerStream.MaxAllowedServerInstances, 
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    BUFFER_SIZE,
                    BUFFER_SIZE,
                    pipeSecurity );

                await namedPipeServer.WaitForConnectionAsync();
                _ = Task.Run(async ()=>{
                    await handleClientAsync(namedPipeServer);
                    idx--;
                    Logger.Info("Total clients now at: {0}", idx);
                });
            }
        }

        async public Task handleClientAsync(NamedPipeServerStream ss) {
            using (ss) {
                try {
                    StreamReader reader = new StreamReader(ss);
                    StreamWriter writer = new StreamWriter(ss);

                    string line = await reader.ReadLineAsync();

                    while (line != null) {
                        await processMessage(line, writer);
                        line = await reader.ReadLineAsync();
                    }

                    Logger.Info("reading from pipe is complete");
                } catch(Exception e) {
                    Logger.Error(e, "Unexpected erorr when reading from or writing to a client pipe.");
                }
            }
        }

        async public Task processMessage(string msg, StreamWriter writer) {
            Logger.Debug("message received: {0}", msg);
            try {
                ServiceFunction func = serializer.Deserialize<ServiceFunction>(new JsonTextReader(new StringReader(msg)));
                Logger.Info("function: {0}", func.Function);
                switch (func.Function) {
                    case "stop":
                        ServiceActions.StopService();
                        break;
                    case "start":
                        ServiceActions.StartService();
                        break;
                    case "status":
                        msg = ServiceActions.ServiceStatus();
                        break;
                    default:
                        Logger.Error("UNKNOWN ACTION received: {0}", func.Function);
                        break;
                }
            } catch (Exception e) {
                Logger.Error(e, "Unexpected erorr in processMessage!");
            }
            await writer.WriteLineAsync(msg);
            await writer.FlushAsync();
        }
    }
}
