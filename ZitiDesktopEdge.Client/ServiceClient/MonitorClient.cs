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
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        protected override Logger Logger { get { return _logger; } }

        public const int EXPECTED_API_VERSION = 1;

        public event EventHandler<MonitorStatusEvent> OnMonitorStatusEvent;

        protected virtual void MonitorStatusEvent(MonitorStatusEvent e) {
            OnMonitorStatusEvent?.Invoke(this, e);
        }

        public MonitorClient() {
        }

        async protected override Task ConnectPipesAsync() {
            await semaphoreSlim.WaitAsync();
            try {
                pipeClient = new NamedPipeClientStream(localPipeServer, IPCServer.PipeName, PipeDirection.InOut);
                eventClient = new NamedPipeClientStream(localPipeServer, IPCServer.EventPipeName, PipeDirection.In);
                await eventClient.ConnectAsync(ServiceConnectTimeout);
                await pipeClient.ConnectAsync(ServiceConnectTimeout);
                ClientConnected(null);
            } catch (Exception ex) {
                semaphoreSlim.Release();
                throw new ServiceException("Could not connect to the service.", 1, ex.Message);
            }
            semaphoreSlim.Release();
        }

        protected override void ProcessLine(string line) {
            var jsonReader = new JsonTextReader(new StringReader(line));
            MonitorStatusEvent evt = serializer.Deserialize<MonitorStatusEvent>(jsonReader);
            MonitorStatusEvent(evt);
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