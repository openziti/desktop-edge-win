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
        public const string PipeName = @"OpenZiti\ziti-monitor\ipc";
        public const string EventPipeName = @"OpenZiti\ziti-monitor\events";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static int BUFFER_SIZE = 16 * 1024;

        private JsonSerializer serializer = new JsonSerializer() { Formatting = Formatting.None };
        private string ipcPipeName;
        private string eventPipeName;

        public IPCServer() {
            this.ipcPipeName = IPCServer.PipeName;
            this.eventPipeName = IPCServer.EventPipeName;
        }

        async public Task startIpcServer() {
            int idx = 0;

            // Allow AuthenticatedUserSid read and write access to the pipe. 
            PipeSecurity pipeSecurity = new PipeSecurity();
            var id = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            pipeSecurity.SetAccessRule(new PipeAccessRule(id, PipeAccessRights.CreateNewInstance | PipeAccessRights.ReadWrite, AccessControlType.Allow));

            while (true) {
                try {
                    var ipcPipeServer = new NamedPipeServerStream(
                        ipcPipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                        BUFFER_SIZE,
                        BUFFER_SIZE,
                        pipeSecurity);

                    await ipcPipeServer.WaitForConnectionAsync();
                    Logger.Debug("Total ipc clients now at: {0}", ++idx);
                    _ = Task.Run(async () => {
                        try {
                            await handleIpcClientAsync(ipcPipeServer);
                        } catch(Exception icpe) {
                            Logger.Error(icpe, "Unexpected erorr in handleIpcClientAsync");
                        }
                        idx--;
                        Logger.Debug("Total ipc clients now at: {0}", idx);
                    });
                } catch (Exception pe) {
                    Logger.Error(pe, "Unexpected erorr when connecting a client pipe.");
                }
            }
        }
        async public Task startEventsServer() {
            int idx = 0;

            // Allow AuthenticatedUserSid read and write access to the pipe. 
            PipeSecurity pipeSecurity = new PipeSecurity();
            var id = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            pipeSecurity.SetAccessRule(new PipeAccessRule(id, PipeAccessRights.CreateNewInstance | PipeAccessRights.ReadWrite, AccessControlType.Allow));

            while (true) {
                try {
                    var eventPipeServer = new NamedPipeServerStream(
                        eventPipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                        BUFFER_SIZE,
                        BUFFER_SIZE,
                        pipeSecurity);

                    await eventPipeServer.WaitForConnectionAsync();
                    Logger.Debug("Total event clients now at: {0}", ++idx);
                    _ = Task.Run(async () => {
                        try {
                            await handleEventClientAsync(eventPipeServer);
                        } catch (Exception icpe) {
                            Logger.Error(icpe, "Unexpected erorr in handleEventClientAsync");
                        }
                        idx--;
                        Logger.Debug("Total event clients now at: {0}", idx);
                    });
                } catch (Exception pe) {
                    Logger.Error(pe, "Unexpected erorr when connecting a client pipe.");
                }
            }
        }

        async public Task handleIpcClientAsync(NamedPipeServerStream ss) {
            using (ss) {
                try {
                    StreamReader reader = new StreamReader(ss);
                    StreamWriter writer = new StreamWriter(ss);

                    string line = await reader.ReadLineAsync();

                    while (line != null) {
                        await processMessage(line, writer);
                        line = await reader.ReadLineAsync();
                    }

                    Logger.Debug("handleIpcClientAsync is complete");
                } catch (Exception e) {
                    Logger.Error(e, "Unexpected erorr when reading from or writing to a client pipe.");
                }
            }
        }

        async public Task handleEventClientAsync(NamedPipeServerStream ss) {
            using (ss) {
                try {
                    StreamReader reader = new StreamReader(ss);
                    StreamWriter writer = new StreamWriter(ss);

                    string line = await reader.ReadLineAsync();

                    while (line != null) {
                        await processMessage(line, writer);
                        line = await reader.ReadLineAsync();
                    }

                    Logger.Debug("handleEventClientAsync is complete");
                } catch (Exception e) {
                    Logger.Error(e, "Unexpected erorr when reading from or writing to a client pipe.");
                }
            }
        }

        async public Task processMessage(string msg, StreamWriter writer) {
            Logger.Debug("message received: {0}", msg);
            SvcResponse r = new SvcResponse();
            try {
                ServiceFunction func = serializer.Deserialize<ServiceFunction>(new JsonTextReader(new StringReader(msg)));
                Logger.Info("function: {0}", func.Function);
                switch (func.Function) {
                    case "stop":
                        r.Message = ServiceActions.StopService();
                        break;
                    case "start":
                        r.Message = ServiceActions.StartService();
                        break;
                    case "status":
                        r.Message = ServiceActions.ServiceStatus();
                        break;
                    default:
                        msg = string.Format("UNKNOWN ACTION received: {0}", func.Function);
                        Logger.Error(msg);
                        r.Code = -3;
                        r.Error = msg;
                        break;
                }
            } catch (Exception e) {
                Logger.Error(e, "Unexpected erorr in processMessage!");
                r.Code = -2;
                r.Error = e.Message + ":" + e?.InnerException?.Message;
            }
            Logger.Info("Returning status: {0}", r.Message);
            await writer.WriteLineAsync(JsonConvert.SerializeObject(r));
            await writer.FlushAsync();
        }
    }
}
