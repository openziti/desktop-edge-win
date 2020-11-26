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

        public event EventHandler<ServiceStatusEvent> OnServiceStatusEvent;

        protected virtual void ServiceStatusEvent(ServiceStatusEvent e) {
            OnServiceStatusEvent?.Invoke(this, e);
        }

        public MonitorClient() : base() {
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
            ServiceStatusEvent evt = serializer.Deserialize<ServiceStatusEvent>(jsonReader);
            ServiceStatusEvent(evt);
        }

        async internal Task<string> SendServiceFunctionAsync(object toSend) {
            await sendAsync(toSend);
            
            var resp = await readMessageAsync(ipcReader, "SendServiceFunctionAsync");
            Logger.Info("RESPONSE: {0}", resp);
            return resp;
        }

        async public Task<ServiceStatusEvent> StopServiceAsync() {
            ActionEvent action = new ActionEvent() { Action = "Normal", Op = "Stop" };
            //string result = await SendServiceFunctionAsync(action);
            await sendAsync(action);
            return await readAsync<ServiceStatusEvent>(ipcReader, "StopServiceAsync");
        }
        async public Task<ServiceStatusEvent> StartServiceAsync() {
            ActionEvent action = new ActionEvent() { Action = "Normal", Op = "Start" };
            //string result = await SendServiceFunctionAsync(action);
            await sendAsync(action);
            return await readAsync<ServiceStatusEvent>(ipcReader, "StartServiceAsync");
        }

        async public Task<ServiceStatusEvent> ForceTerminate() {
            ActionEvent action = new ActionEvent() { Action = "Force", Op = "Stop" };
            await sendAsync(action);
            return await readAsync<ServiceStatusEvent>(ipcReader, "ForceTerminate");
        }

        async public Task<ServiceStatusEvent> Status() {
            ActionEvent action = new ActionEvent() { Action = "", Op = "Status" };
            await sendAsync(action);
            return await readAsync<ServiceStatusEvent>(ipcReader, "Status");
        }
        async public Task<ServiceStatusEvent> CaptureLogsAsync() {
            ActionEvent action = new ActionEvent() { Action = "Normal", Op = "captureLogs" };
            //string result = await SendServiceFunctionAsync(action);
            await sendAsync(action);
            return await readAsync<ServiceStatusEvent>(ipcReader, "CaptureLogsAsync");
        }
    }
}