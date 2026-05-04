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
using System.Diagnostics;
using System.Reflection;
using ZitiDesktopEdge.Utility;

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

        public event EventHandler<MonitorServiceStatusEvent> OnServiceStatusEvent;
        public event EventHandler<InstallationNotificationEvent> OnNotificationEvent;
        public event EventHandler<MonitorServiceStatusEvent> OnCaptureFeedbackProgressEvent;

        protected virtual void ServiceStatusEvent(MonitorServiceStatusEvent e) {
            OnServiceStatusEvent?.Invoke(this, e);
        }

        protected virtual void InstallationNotificationEvent(InstallationNotificationEvent e) {
            OnNotificationEvent?.Invoke(this, e);
        }

        protected virtual void CaptureFeedbackProgressEvent(MonitorServiceStatusEvent e) {
            OnCaptureFeedbackProgressEvent?.Invoke(this, e);
        }

        public MonitorClient(string id) : base(id) {
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
                throw new MonitorServiceException("Could not connect to the monitor service.", ex);
            }
            semaphoreSlim.Release();
        }

        protected override void ProcessLine(string line) {
            var evt = serializer.Deserialize<MonitorServiceStatusEvent>(new JsonTextReader(new StringReader(line)));

            switch (evt.Type) {
                case "Notification":
                    var instEvt = serializer.Deserialize<InstallationNotificationEvent>(new JsonTextReader(new StringReader(line)));
                    InstallationNotificationEvent(instEvt);
                    break;
                case "CaptureFeedbackProgress":
                    CaptureFeedbackProgressEvent(evt);
                    break;
                default:
                    ServiceStatusEvent(evt);
                    break;
            }
        }

        async private Task sendMonitorClientAsync(object objtoSend) {
            try {
                await sendAsync("monitor", objtoSend);
            } catch (Exception ex) {
                throw new MonitorServiceException("Could not connect to the monitor service.", ex);
            }
        }

        async protected Task<T> readMonitorClientAsync<T>(StreamReader reader) where T : SvcResponse {
            return await readAsync<T>("monitor", reader, DefaultReadTimeout);
        }

        async protected Task<T> readMonitorClientAsync<T>(StreamReader reader, TimeSpan timeout) where T : SvcResponse {
            return await readAsync<T>("monitor", reader, timeout);
        }

        async public Task<MonitorServiceStatusEvent> StopServiceAsync() {
            ActionEvent action = new ActionEvent() { Op = "Stop", Action = "Normal" };
            await sendMonitorClientAsync(action);
            return await readMonitorClientAsync<MonitorServiceStatusEvent>(ipcReader);
        }

        async public Task<MonitorServiceStatusEvent> StartServiceAsync(TimeSpan timeout) {
            ActionEvent action = new ActionEvent() { Op = "Start", Action = "Normal" };
            await sendMonitorClientAsync(action);
            return await readMonitorClientAsync<MonitorServiceStatusEvent>(ipcReader, timeout);
        }

        async public Task<MonitorServiceStatusEvent> ForceTerminateAsync() {
            ActionEvent action = new ActionEvent() { Op = "Stop", Action = "Force" };
            try {
                await sendMonitorClientAsync(action);
                return await readMonitorClientAsync<MonitorServiceStatusEvent>(ipcReader);
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error");
            }
            return null;
        }

        async public Task<MonitorServiceStatusEvent> StatusAsync() {
            ActionEvent action = new ActionEvent() { Op = "Status", Action = "" };
            try {
                await sendMonitorClientAsync(action);
                return await readMonitorClientAsync<MonitorServiceStatusEvent>(ipcReader);
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error");
            }
            return null;
        }

        async public Task<MonitorServiceStatusEvent> CaptureLogsAsync() {
            ActionEvent action = new ActionEvent() { Op = "CaptureLogs", Action = "Normal" };
            await sendMonitorClientAsync(action);

            DateTime lastProgressTime = DateTime.UtcNow;
            EventHandler<MonitorServiceStatusEvent> heartbeat = (s, e) => lastProgressTime = DateTime.UtcNow;
            OnCaptureFeedbackProgressEvent += heartbeat;

            try {
                Task<MonitorServiceStatusEvent> readTask = readMonitorClientAsync<MonitorServiceStatusEvent>(ipcReader, Timeout.InfiniteTimeSpan);
                TimeSpan stallThreshold = TimeSpan.FromSeconds(60);

                while (true) {
                    Task completed = await Task.WhenAny(readTask, Task.Delay(5000));
                    if (completed == readTask) {
                        return await readTask;
                    }
                    if (DateTime.UtcNow - lastProgressTime > stallThreshold) {
                        string error = $"Feedback collection stalled (no progress for {stallThreshold.TotalSeconds}s).";
                        Logger.Error(error);
                        throw new MonitorServiceException(error);
                    }
                }
            } finally {
                OnCaptureFeedbackProgressEvent -= heartbeat;
            }
        }

        async public Task<SvcResponse> SetLogLevelAsync(string level) {
            if ("verbose".Equals(level?.ToLower())) {
                //only the data client understands verbose - so use trace...
                level = "TRACE";
            }
            ActionEvent action = new ActionEvent() { Op = "SetLogLevel", Action = level };
            await sendMonitorClientAsync(action);
            return await readMonitorClientAsync<SvcResponse>(ipcReader);
        }

        async public Task<StatusCheck> DoUpdateCheck() {
            ActionEvent action = new ActionEvent() { Op = "DoUpdateCheck", Action = "" };
            await sendMonitorClientAsync(action);
            return await readMonitorClientAsync<StatusCheck>(ipcReader);
        }

        async public Task<SvcResponse> TriggerUpdate() {
            UpgradeSentinel.StartUpgradeSentinel(true);
            ActionEvent action = new ActionEvent() { Op = "TriggerUpdate", Action = "" };
            await sendMonitorClientAsync(action);
            return await readMonitorClientAsync<SvcResponse>(ipcReader);
        }

        async public Task<SvcResponse> SetAutomaticUpgradeDisabledAsync(bool disabled) {
            ActionEvent action = new ActionEvent() { Op = "SetAutomaticUpgradeDisabled", Action = (disabled ? "true" : "false") };
            await sendMonitorClientAsync(action);
            return await readMonitorClientAsync<SvcResponse>(ipcReader);
        }

        async public Task<SvcResponse> SetAutomaticUpgradeURLAsync(string url) {
            ActionEvent action = new ActionEvent() { Op = "SetAutomaticUpgradeURL", Action = (url) };
            await sendMonitorClientAsync(action);
            return await readMonitorClientAsync<SvcResponse>(ipcReader);
        }
    }
}