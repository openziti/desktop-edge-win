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
using ZitiDesktopEdge.Server;

/// <summary>
/// The implementation will abstract away the setup of the communication to
/// the monitor service. This implementation will communicate to the service over a
/// a NamedPipe.
/// 
/// All communication is effectively serial - one or more messages sent and 
/// one or more messages returned.
/// 
/// </summary>
namespace ZitiDesktopEdge.ServiceClient {
    public class MonitorClient : AbstractClient {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public const int EXPECTED_API_VERSION = 1;

        public event EventHandler<TunnelStatusEvent> OnTunnelStatusEvent;

        protected virtual void TunnelStatusEvent(TunnelStatusEvent e) {
            OnTunnelStatusEvent?.Invoke(this, e);
        }

        const PipeDirection inOut = PipeDirection.InOut;

        public MonitorClient() {
        }

        protected override void ConnectPipes() {
            lock (namedPipeSyncLock) {
                pipeClient = new NamedPipeClientStream(localPipeServer, IPCServer.PipeName, inOut);
                eventClient = new NamedPipeClientStream(localPipeServer, IPCServer.EventPipeName, PipeDirection.In);

                try {
                    eventClient.Connect(ServiceConnectTimeout);
                    pipeClient.Connect(ServiceConnectTimeout);
                    ClientConnected(null);
                } catch (Exception ex) {
                    throw new ServiceException("Could not connect to the service.", 1, ex.Message);
                }
            }
        }

        protected override void ProcessLine(string line) {
            var jsonReader = new JsonTextReader(new StringReader(line));
            StatusEvent evt = serializer.Deserialize<StatusEvent>(jsonReader);
            switch (evt.Op) {
                case "status":
                    TunnelStatusEvent se = serializer.Deserialize<TunnelStatusEvent>(jsonReader);

                    if (se != null) {
                        TunnelStatusEvent(se);
                    }
                    break;
                case "shutdown":

                    break;
                default:
                    Logger.Debug("unexpected operation! " + evt.Op);
                    break;
            }
        }

        async public Task SendServiceFunctionAsync(ServiceFunction f) {
            await sendAsync(f);
            
            var resp = await readMessageAsync(ipcReader);
            Logger.Info("RESPONSE: {0}", resp);
        }

        async public Task StopServicAsync() {
            await SendServiceFunctionAsync(new ServiceFunction() { Function = "stop" });
        }
        async public Task StartServicAsync() {
            await SendServiceFunctionAsync(new ServiceFunction() { Function = "start" });
        }
    }
}