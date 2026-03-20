/*
	Copyright NetFoundry Inc.

	Licensed under the Apache License, Version 2.0 (the "License");
	you may not use this file except in compliance with the License.
	You may obtain a copy of the License at

	https://www.apache.org/licenses/LICENSE-2.0

	Unless required by applicable law or agreed to in writing, software
	distributed under the License is distributed on an "AS IS" BASIS,
	WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
	See the License for the specific language governing permissions and
	limitations under the License.
*/

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

        public delegate Task OnClientAsync(StreamWriter writer);

        public delegate string CaptureLogsDelegate();
        public CaptureLogsDelegate CaptureLogs { get; set; }

        public delegate void SetLogLevelDelegate(string level);
        public SetLogLevelDelegate SetLogLevel { get; set; }

        public delegate void SetReleaseStreamDelegate(string stream);
        public SetReleaseStreamDelegate SetReleaseStream { get; set; }

        public delegate StatusCheck DoUpdateCheckDelegate();
        public DoUpdateCheckDelegate DoUpdateCheck { get; set; }

        public delegate SvcResponse TriggerUpdateDelegate();
        public TriggerUpdateDelegate TriggerUpdate { get; set; }

        public delegate SvcResponse SetAutomaticUpdateDisabledDelegate(bool disabled);

        public delegate SvcResponse SetAutomaticUpdateURLDelegate(string url);

        public SetAutomaticUpdateDisabledDelegate SetAutomaticUpdateDisabled { get; set; }
        public SetAutomaticUpdateURLDelegate SetAutomaticUpdateURL { get; set; }

        public IPCServer() {
            ipcPipeName = PipeName;
            eventPipeName = EventPipeName;
        }

        async public Task startIpcServerAsync(OnClientAsync onClient) {
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
                            await handleIpcClientAsync(ipcPipeServer, onClient);
                        } catch (Exception icpe) {
                            Logger.Error(icpe, "Unexpected error in handleIpcClientAsync");
                        }
                        idx--;
                        Logger.Debug("Total ipc clients now at: {0}", idx);
                    });
                } catch (Exception pe) {
                    Logger.Error(pe, "Unexpected error when connecting a client pipe.");
                }
            }
        }

        async public Task startEventsServerAsync(OnClientAsync onClient) {
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
                            await handleEventClientAsync(eventPipeServer, onClient);
                        } catch (Exception icpe) {
                            if (icpe.Message.StartsWith("Service ziti was not found on computer")) {
                                //ignore this for now...
                            } else {
                                Logger.Error(icpe, "Unexpected error in handleEventClientAsync");
                            }
                        }
                        idx--;
                        Logger.Debug("Total event clients now at: {0}", idx);
                    });
                } catch (Exception pe) {
                    Logger.Error(pe, "Unexpected error when connecting a client pipe.");
                }
            }
        }

        async public Task handleIpcClientAsync(NamedPipeServerStream ss, OnClientAsync onClient) {
            using (ss) {
                try {
                    StreamReader reader = new StreamReader(ss);
                    StreamWriter writer = new StreamWriter(ss);

                    string line = await reader.ReadLineAsync();

                    while (line != null) {
                        await processMessageAsync(line, writer);
                        line = await reader.ReadLineAsync();
                    }

                    Logger.Debug("handleIpcClientAsync is complete");
                } catch (Exception e) {
                    Logger.Error(e, "Unexpected error when reading from or writing to a client pipe.");
                }
            }
        }

        async public Task handleEventClientAsync(NamedPipeServerStream ss, OnClientAsync onClient) {
            try {
                using (ss) {
                    StreamWriter writer = new StreamWriter(ss);

                    EventHandler eh = async (object sender, EventArgs e) => {
                        try {
                            await writer.WriteLineAsync(sender.ToString());
                            await writer.FlushAsync();
                        } catch (Exception ex) {
                            Logger.Error("problem with event handler in handleEventClientAsync: {}", ex.Message);
                        }
                    };

                    await onClient(writer);

                    EventRegistry.MyEvent += eh;
                    StreamReader reader = new StreamReader(ss);

                    string line = await reader.ReadLineAsync();
                    while (line != null) {
                        await processMessageAsync(line, writer);
                        line = await reader.ReadLineAsync();
                    }

                    Logger.Debug("handleEventClientAsync is complete");
                    EventRegistry.MyEvent -= eh;
                }
            } catch (Exception e) {
                Logger.Error(e, "Unexpected error when reading from or writing to a client pipe.");
            }
        }

        async public Task processMessageAsync(string json, StreamWriter writer) {
            Logger.Debug("message received: {0}", json);
            var r = new SvcResponse();
            var rr = new MonitorServiceStatusEvent();
            try {
                ActionEvent ae = serializer.Deserialize<ActionEvent>(new JsonTextReader(new StringReader(json)));
                Logger.Debug("Op: {0}", ae.Op);
                switch (ae.Op.ToLower()) {
                    case "stop":
                        if (ae.Action == "Force") {
                            // attempt to forcefully find the process and terminate it...
                            Logger.Warn("User has requested a FORCEFUL termination of the service. It must be stuck. Current status: {0}", ServiceActions.ServiceStatus());
                            var procs = System.Diagnostics.Process.GetProcessesByName("ziti-edge-tunnel");
                            if (procs == null || procs.Length == 0) {
                                Logger.Error("Process not found! Cannot terminate!");
                                rr.Code = -20;
                                rr.Error = "Process not found! Cannot terminate!";
                                rr.Message = "Could not terminate the service forcefully";
                                break;
                            }

                            foreach (var p in procs) {
                                Logger.Warn("Forcefully terminating process: {0}", p.Id);
                                p.Kill();
                            }
                            rr.Message = "Service has been terminated";
                            rr.Status = ServiceActions.ServiceStatus();
                            r = rr;
                        } else {
                            r.Message = ServiceActions.StopService();
                        }
                        break;
                    case "start":
                        r.Message = ServiceActions.StartService();
                        break;
                    case "status":
                        rr.Status = ServiceActions.ServiceStatus();
                        r = rr;
                        break;
                    case "capturelogs":
                        try {
                            string results = CaptureLogs();
                            r.Message = results;
                        } catch (Exception ex) {
                            string err = string.Format("UNKNOWN ERROR : {0}", ex.Message);
                            Logger.Error(ex, err);
                            r.Code = -5;
                            r.Message = "FAILURE";
                            r.Error = err;
                        }
                        break;
                    case "setreleasestream":
                        SetReleaseStream(ae.Action);
                        break;
                    case "setloglevel":
                        SetLogLevel(ae.Action);
                        break;
                    case "doupdatecheck":
                        r = DoUpdateCheck();
                        break;
                    case "triggerupdate":
                        r = TriggerUpdate();
                        break;
                    case "setautomaticupgradedisabled":
                        r = SetAutomaticUpdateDisabled(bool.TrueString.ToLower() == ("" + ae.Action).ToLower().Trim());
                        break;
                    case "setautomaticupgradeurl":
                        r = SetAutomaticUpdateURL(ae.Action);
                        break;
                    default:
                        r.Message = "FAILURE";
                        r.Code = -3;
                        r.Error = string.Format("UNKNOWN ACTION received: {0}", ae.Op);
                        Logger.Error(r.Message);
                        break;
                }
            } catch (Exception e) {
                Logger.Error(e, "Unexpected error in processMessage!");
                r.Message = "FAILURE: " + e.Message;
                r.Code = -2;
                r.Error = e.Message + ":" + e?.InnerException?.Message;
            }
            Logger.Debug("Returning status: {0}", r.Message);
            await writer.WriteLineAsync(JsonConvert.SerializeObject(r));
            await writer.FlushAsync();
        }
    }

    public enum ErrorCodes {
        NO_ERROR = 0,
        COULD_NOT_SET_URL,
        URL_INVALID,
    }
}
