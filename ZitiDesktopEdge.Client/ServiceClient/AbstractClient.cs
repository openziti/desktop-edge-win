//#define DEBUG_METRICS_MESSAGES
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
using Newtonsoft.Json.Serialization;
using NLog;


using ZitiDesktopEdge.DataStructures;
using System.Reflection;

namespace ZitiDesktopEdge.ServiceClient {
    public abstract class AbstractClient {
        public event EventHandler<object> OnClientConnected = null;
        public event EventHandler<object> OnClientDisconnected;
        public event EventHandler<StatusEvent> OnShutdownEvent;
        public event EventHandler<object> OnReconnectFailure;
        public virtual event EventHandler<Exception> OnCommunicationError;

        protected NamedPipeClientStream pipeClient = null;
        protected NamedPipeClientStream eventClient = null;
        protected StreamWriter ipcWriter = null;
        protected StreamReader ipcReader = null;
        protected abstract Task ConnectPipesAsync();
        protected abstract void ProcessLine(string line);
        protected abstract Logger Logger { get; }
        protected string Id { get; set; }

        protected const string localPipeServer = ".";
        protected const int ServiceConnectTimeout = 500;

        //protected object namedPipeSyncLock = new object();
        protected static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);

        protected JsonSerializer serializer = new JsonSerializer() { Formatting = Formatting.None };

        protected virtual void ClientConnected(object e) {
            Connected = true;
            Reconnecting = false;
            ExpectedShutdown = false;
            Logger.Debug("Client connected successfully. Setting UnexpectedShutdown set to false.");

            ipcWriter = new StreamWriter(pipeClient);
            ipcReader = new StreamReader(pipeClient);
            Task.Run(async () => { //hack for now until it's async...
                try {
                    using (StreamReader eventReader = new StreamReader(eventClient)) {
                        while (true) {
                            if (eventReader.EndOfStream) {
                                break;
                            }
                            string respAsString = null;
                            try {
                                respAsString = await readMessageAsync("event", eventReader);
                                try {
                                    ProcessLine(respAsString);
                                } catch (Exception ex) {
                                    Logger.Warn(ex, "ERROR caught in ProcessLine: {0}", respAsString);
                                }
                            } catch (Exception ex) {
                                Logger.Warn(ex, "ERROR caught in readMessageAsync: {0}", respAsString);
                            }
                        }
                    }
                } catch (Exception ex) {
                    Logger.Debug("unepxected error: " + ex.ToString());
                }

                // since this thread is always sitting waiting to read
                // it should be the only one triggering this event
                ClientDisconnected(null);
            });
            OnClientConnected?.Invoke(this, e);
        }

        protected virtual void ClientDisconnected(object e) {
            Reconnect();
            Connected = false;
            OnClientDisconnected?.Invoke(this, e);
        }

        protected virtual void ShutdownEvent(StatusEvent e) {
            ExpectedShutdown = true;
            OnShutdownEvent?.Invoke(this, e);
        }

        protected virtual void ReconnectFailureEvent(object e) {
            OnReconnectFailure?.Invoke(this, e);
        }

        protected virtual void CommunicationError(Exception e) {
            OnCommunicationError?.Invoke(this, e);
        }

        async protected Task sendAsync(string channel, object objToSend) {
            bool retried = false;
            while (true) {
                try {
                    var jsonResolver = new ShouldSerializeContractResolver();

                    var serializerSettings = new JsonSerializerSettings();
                    serializerSettings.ContractResolver = jsonResolver;
                    string toSend = JsonConvert.SerializeObject(objToSend, serializerSettings);

                    if (toSend?.Trim() != null) {
                        debugServiceCommunication(Id, "send", channel, toSend);
                        if (ipcWriter != null) {
                            await ipcWriter.WriteAsync(toSend);
                            await ipcWriter.WriteAsync('\n');
                            await ipcWriter.FlushAsync();
                        } else {
                            throw new IPCException("ipcWriter is null. the target appears to be offline?");
                        }
                    } else {
                        Logger.Debug("NOT sending empty object??? " + objToSend?.ToString());
                    }
                    break;
                } catch (IOException ioe) {
                    //almost certainly a problem with the pipe - recreate the pipe... try one more time.
                    await ConnectPipesAsync();
                    if (retried) {
                        //we tried - throw the error...
                        throw ioe;
                    } else {
                        retried = true; //fall back through to the while and try again
                    }
                } catch (MonitorServiceException) {
                    throw;
                } catch (Exception ex) {
                    //if this fails it's usually because the writer is null/invalid. throwing IOException
                    //will trigger the pipe to rebuild
                    throw new IOException("Unexpected error when sending data to service. " + ex.Message);
                }
            }
        }

        public bool Reconnecting { get; set; }
        public bool Connected { get; set; }
        public bool ExpectedShutdown { get; set; }

        public AbstractClient(string id) {
            this.Id = id;
        }

        async public Task ConnectAsync() {
            //establish the named pipe to the service
            await ConnectPipesAsync();
        }

        public void Reconnect() {
            if (Reconnecting) {
                Logger.Debug("Already in reconnect mode.");
                return;
            } else {
                Reconnecting = true;
            }

            Task.Run(async () => {
                Logger.Info("service is down. attempting to connect to service...");

                DateTime reconnectStart = DateTime.Now;
                DateTime logAgainAfter = reconnectStart + TimeSpan.FromSeconds(1);

                while (true) {
                    try {
                        await Task.Delay(2500);
                        await ConnectPipesAsync();

                        if (Connected) {
                            Logger.Debug("Connected to the service - exiting reconect loop");
                            Connected = true;
                            Reconnecting = false;
                            return;
                        } else {
                            //ClientDisconnected(null);
                        }
                    } catch (Exception) {
                        try {
                            ReconnectFailureEvent("reconnect failure");
                        } catch (Exception) {
                            // don't care - just catch it and continue... it's a timeout...
                        }
                        var now = DateTime.Now;
                        if (now > logAgainAfter) {
                            Logger.Trace("Reconnect failed. Trying again...");
                            var duration = now - reconnectStart;
                            if (duration > TimeSpan.FromHours(1)) {
                                Logger.Info("reconnect has not completed and has been running for {0} hours", duration.TotalHours);
                                logAgainAfter += TimeSpan.FromHours(1);
                            } else if (duration > TimeSpan.FromMinutes(1)) {
                                Logger.Info("reconnect has not completed and has been running for {0} minutes", duration.TotalMinutes);
                                logAgainAfter += TimeSpan.FromMinutes(1);
                            } else {
                                logAgainAfter += TimeSpan.FromSeconds(1);
                            }
                        }
                    }
                }
            });
        }

        protected void debugServiceCommunication(string source, string direction, string channel, string msg) {
#if DEBUG
#if DEBUG_METRICS_MESSAGES
            // see the top of the file for where you can enable this
            Logger.Warn("{0}-{1}-{2}: {3}", source, direction, channel, msg);
#else
            if (false == msg?.Contains("\"metrics\"")) {
                Logger.Warn("{0}-{1}-{2}: {3}", source, direction, channel, msg);
            }
#endif
#else
            Logger.Trace("{0}-{1}-{2}: {3}", source, direction, channel, msg);
#endif
        }
#if DEBUG
        protected TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(30);
#else
        protected TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(3);
#endif
        async protected Task<T> readAsync<T>(string stream, StreamReader reader, TimeSpan timeout) where T : SvcResponse {
            var cts = new CancellationTokenSource(timeout);
            try {
                // Create a task that will complete when the read operation finishes
                var readTask = readMessageAsync(stream, reader);

                // Create a task that will complete when the timeout occurs
                var timeoutTask = Task.Delay(timeout, cts.Token);

                // Wait for either the read operation or timeout
                var completedTask = await Task.WhenAny(readTask, timeoutTask);

                // If the timeout task is the one that completed, throw a TimeoutException
                if (completedTask == timeoutTask) {
                    throw new TimeoutException("Read operation timed out waiting for a response. If the " + Id + " service is running, this is highly unepxected and should be reported.");
                }

                // Otherwise, await the read operation to get the result
                string respAsString = await readTask;
                T resp = (T)serializer.Deserialize(new StringReader(respAsString), typeof(T));
                return resp;
            } catch (TimeoutException) {
                throw; // just throw it
            } catch (Exception ex) {
                // handle all the other unexpected situations
                throw new IOException("Unexpected error while reading data. " + ex.Message);
            }
        }

        async public Task<string> readMessageAsync(string channel, StreamReader reader) {
            try {
                int emptyCount = 1; //just a stop gap in case something crazy happens in the communication

                string respAsString = await reader.ReadLineAsync();
                debugServiceCommunication(Id, "read", channel, respAsString);
                while (string.IsNullOrEmpty(respAsString?.Trim())) {
                    debugServiceCommunication(Id, "read", channel, "Received empty payload - continuing to read until a payload is received");
                    //now how'd that happen...
                    respAsString = await reader.ReadLineAsync();
                    debugServiceCommunication(Id, "read", channel, respAsString);
                    emptyCount++;
                    if (emptyCount > 5) {
                        Logger.Debug("are we there yet? " + reader.EndOfStream);
                        //that's just too many...
                        return null;
                    }
                }
                return respAsString;
            } catch (IOException ioe) {
                //almost certainly a problem with the pipe
                Logger.Error(ioe, "io error in read: " + ioe.Message);
                ClientDisconnected(null);
                throw ioe;
            } catch (Exception ee) {
                //almost certainly a problem with the pipe
                Logger.Error(ee, "unexpected error in read: " + ee.Message);
                ClientDisconnected(null);
                throw ee;
            }
        }

        async public Task WaitForConnectionAsync() {
            while (Reconnecting || !Connected) {
                await Task.Delay(100);
            }
        }
    }

    public class ShouldSerializeContractResolver : DefaultContractResolver {
        public static readonly ShouldSerializeContractResolver Instance = new ShouldSerializeContractResolver();
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization) {
            JsonProperty property = base.CreateProperty(member, memberSerialization);

            if (property.DeclaringType == typeof(Identity) && property.PropertyName == "MfaLastUpdatedTime") {
                property.ShouldSerialize =
                    instance => {
                        Identity identity = (Identity)instance;
                        return identity != null && identity.MfaLastUpdatedTime != DateTime.MinValue;
                    };
            }

            return property;
        }
    }

    public class MonitorServiceException : Exception {
        public MonitorServiceException() { }
        public MonitorServiceException(string message) : base(message) { }
        public MonitorServiceException(string message, Exception source) : base(message, source) { }
    }

    public class IPCException : Exception {
        public IPCException() { }
        public IPCException(string message) : base(message) { }
        public IPCException(string message, Exception source) : base(message, source) { }

    }
}
