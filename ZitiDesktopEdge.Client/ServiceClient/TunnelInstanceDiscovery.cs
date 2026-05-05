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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;
using NLog;

namespace ZitiDesktopEdge.ServiceClient {
    /// <summary>
    /// Discovers all ziti-edge-tunnel.exe instances running on the local machine by
    /// enumerating named pipes of the form:
    ///   \\.\pipe\ziti-edge-tunnel.sock              (default instance)
    ///   \\.\pipe\ziti-edge-tunnel.sock.&lt;discriminator&gt;  (-P &lt;discriminator&gt; instance)
    ///
    /// For each matching pipe the discovery performs a best-effort "Status" probe to
    /// pull TunName/IP/DNS. Instances are returned even if the probe fails so the UI
    /// can present "something is listening there" to the user.
    /// </summary>
    public class TunnelInstanceDiscovery {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private const string DefaultPipeName = "ziti-edge-tunnel.sock";
        private const string DefaultEventPipeName = "ziti-edge-tunnel-event.sock";
        private static readonly Regex PipeNameRegex = new Regex(
            @"^ziti-edge-tunnel\.sock(?:\.(.+))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public class TunnelInstance {
            public string Discriminator { get; set; }
            public string DisplayLabel {
                get { return string.IsNullOrEmpty(Discriminator) ? "default" : Discriminator; }
            }
            public string PipeName { get; set; }
            public string EventPipeName { get; set; }
            public string TunName { get; set; }
            public string Ip { get; set; }
            public string Dns { get; set; }

            /// <summary>
            /// True when this instance was discovered by enumerating live named
            /// pipes. False for synthetic entries (e.g. the "default" row the
            /// picker always shows even if the default pipe isn't active).
            /// </summary>
            public bool IsOnline { get; set; } = true;
        }

        /// <summary>
        /// Construct a synthetic "default" TunnelInstance marked as offline.
        /// Used by the picker so the default row is always visible even when
        /// the default ziti-edge-tunnel isn't running — letting the user
        /// come back to it after starting the service.
        /// </summary>
        public static TunnelInstance OfflineDefault() {
            return new TunnelInstance {
                Discriminator = null,
                PipeName = "ziti-edge-tunnel.sock",
                EventPipeName = "ziti-edge-tunnel-event.sock",
                IsOnline = false,
            };
        }

        /// <summary>
        /// Enumerate all ziti-edge-tunnel instances currently running on the local
        /// machine. Each discovered instance is probed in parallel with a short
        /// timeout so total enumeration time is bounded by probeTimeoutMs rather
        /// than N * probeTimeoutMs.
        /// </summary>
        public static async Task<IReadOnlyList<TunnelInstance>> EnumerateAsync(int probeTimeoutMs = 150, CancellationToken ct = default(CancellationToken)) {
            List<TunnelInstance> instances = new List<TunnelInstance>();

            string[] pipeEntries;
            try {
                // On Windows .NET Framework, Directory.GetFiles against \\.\pipe\
                // returns the list of named pipes. Entries come back as full paths
                // of the form \\.\pipe\<name>, so we extract just the name.
                pipeEntries = Directory.GetFiles(@"\\.\pipe\", "ziti*");
            } catch (Exception ex) {
                Logger.Debug(ex, "Failed to enumerate named pipes at \\\\.\\pipe\\");
                return instances;
            }

            foreach (string entry in pipeEntries) {
                if (string.IsNullOrEmpty(entry)) continue;
                string name = entry;
                int lastSlash = name.LastIndexOf('\\');
                if (lastSlash >= 0 && lastSlash < name.Length - 1) {
                    name = name.Substring(lastSlash + 1);
                }

                Match m = PipeNameRegex.Match(name);
                if (!m.Success) continue;

                string discriminator = m.Groups[1].Success ? m.Groups[1].Value : null;

                // Skip the event pipes - we only want the command pipes. The event
                // pipe name contains "-event" which is not matched by our regex,
                // but this guard is here defensively in case the regex is ever
                // loosened.
                if (!string.IsNullOrEmpty(discriminator) && discriminator.StartsWith("sock", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                TunnelInstance inst = new TunnelInstance {
                    Discriminator = discriminator,
                    PipeName = string.IsNullOrEmpty(discriminator)
                        ? DefaultPipeName
                        : DefaultPipeName + "." + discriminator,
                    EventPipeName = string.IsNullOrEmpty(discriminator)
                        ? DefaultEventPipeName
                        : DefaultEventPipeName + "." + discriminator,
                };
                instances.Add(inst);
            }

            // De-duplicate in case Directory.GetFiles returns an event pipe that
            // somehow satisfied the regex (the event pipe name ends with
            // -event.sock, which does not match ^ziti-edge-tunnel\.sock, so this
            // is belt-and-braces only).
            instances = instances
                .GroupBy(i => i.PipeName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (instances.Count == 0) {
                return instances;
            }

            Task[] probes = instances
                .Select(i => ProbeAsync(i, probeTimeoutMs, ct))
                .ToArray();
            try {
                await Task.WhenAll(probes).ConfigureAwait(false);
            } catch (Exception ex) {
                // Individual probe failures are swallowed inside ProbeAsync; this
                // only trips for truly unexpected exceptions.
                Logger.Debug(ex, "unexpected error while awaiting tunnel instance probes");
            }

            return instances;
        }

        private static async Task ProbeAsync(TunnelInstance inst, int probeTimeoutMs, CancellationToken ct) {
            NamedPipeClientStream pipe = null;
            try {
                pipe = new NamedPipeClientStream(".", inst.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

                // NamedPipeClientStream.ConnectAsync in .NET Framework 4.8 exists,
                // but it does not natively honor a cancellation token on the
                // underlying connect. We wrap with a timeout Task so the probe is
                // bounded. Any failure here just means "probe failed" - the
                // instance still goes into the returned list.
                Task connectTask = pipe.ConnectAsync(probeTimeoutMs);
                Task delayTask = Task.Delay(probeTimeoutMs + 50, ct);
                Task winner = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);
                if (winner != connectTask || !pipe.IsConnected) {
                    Logger.Debug("probe: connect timed out for pipe '{0}'", inst.PipeName);
                    return;
                }
                await connectTask.ConfigureAwait(false); // surface any connect exception

                // Write the Status command.
                byte[] payload = new UTF8Encoding(false).GetBytes("{\"Command\":\"Status\"}\n");
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                    cts.CancelAfter(probeTimeoutMs);
                    await pipe.WriteAsync(payload, 0, payload.Length, cts.Token).ConfigureAwait(false);
                    await pipe.FlushAsync(cts.Token).ConfigureAwait(false);
                }

                // Read a single line back. Leave the stream open so the pipe is
                // closed cleanly by the using below.
                StreamReader reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
                Task<string> readTask = reader.ReadLineAsync();
                Task readTimeout = Task.Delay(probeTimeoutMs, ct);
                Task readWinner = await Task.WhenAny(readTask, readTimeout).ConfigureAwait(false);
                if (readWinner != readTask) {
                    Logger.Debug("probe: read timed out for pipe '{0}'", inst.PipeName);
                    return;
                }

                string line = await readTask.ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) {
                    Logger.Debug("probe: empty response from pipe '{0}'", inst.PipeName);
                    return;
                }

                try {
                    // Status response envelope (ziti-tunnel-sdk-c):
                    //   { Success, Error, Code, Data: { TunName, IpInfo: { Ip, DNS }, ... } }
                    // Fields confirmed from
                    //   lib/ziti-tunnel-cbs/include/ziti/ziti_tunnel_cbs.h:96-100
                    //   programs/ziti-edge-tunnel/include/model/dtos.h:101-117
                    JObject obj = JObject.Parse(line);
                    JObject data = obj.GetValue("Data", StringComparison.OrdinalIgnoreCase) as JObject;
                    if (data == null) {
                        Logger.Debug("probe: status response for pipe '{0}' had no Data object. payload={1}", inst.PipeName, line);
                        return;
                    }

                    inst.TunName = GetStringProp(data, "TunName");

                    JObject ipInfo = data.GetValue("IpInfo", StringComparison.OrdinalIgnoreCase) as JObject;
                    if (ipInfo != null) {
                        inst.Ip = GetStringProp(ipInfo, "Ip");
                        inst.Dns = GetStringProp(ipInfo, "DNS");
                    }
                } catch (Exception ex) {
                    Logger.Debug(ex, "probe: failed to parse JSON response from pipe '{0}'. payload={1}", inst.PipeName, line);
                }
            } catch (OperationCanceledException) {
                Logger.Debug("probe: canceled for pipe '{0}'", inst.PipeName);
            } catch (Exception ex) {
                Logger.Debug(ex, "probe: unexpected error for pipe '{0}'", inst.PipeName);
            } finally {
                try { pipe?.Dispose(); } catch { /* ignore */ }
            }
        }

        private static string GetStringProp(JObject obj, string name) {
            if (obj == null) return null;
            JToken tok = obj.GetValue(name, StringComparison.OrdinalIgnoreCase);
            if (tok == null || tok.Type == JTokenType.Null || tok.Type == JTokenType.Object || tok.Type == JTokenType.Array) {
                return null;
            }
            string s = tok.ToString();
            return string.IsNullOrEmpty(s) ? null : s;
        }
    }
}
