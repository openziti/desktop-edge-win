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
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

class FileWatcher {
    private static string processName = Process.GetCurrentProcess().ProcessName;
    private static string tempDir = $"{Environment.GetEnvironmentVariable("TEMP")}";
    private static string fileName = $"{processName}_{DateTime.Now:yyyyMMddHHmmss}.log";
    private static string logFilePath = Path.Combine(tempDir, fileName);

    public static async Task Main(string[] args) {
        Log($"{processName} started");
        try {
            if (Process.GetProcessesByName(processName).Length > 1) {
                Log("Another instance is already running. Exiting...");
                return;
            }
            await RunWithTimeout(task: WaitForStartupChange(), timeout: TimeSpan.FromMinutes(5));
            StartZitiDesktopEdgeUI();
        } catch (Exception e) {
            Log($"{processName} completed exceptionally: ");
            Log(e.ToString());
        } finally {
            Log($"{processName} completed");
        }
    }

    public static DateTime GetCurrentStartTime(StreamWriter writer, StreamReader reader) {
        string statusCommand = "{\"Command\":\"Status\"}";
        writer.WriteLine(statusCommand);
        writer.Flush();
        var statusResponse = reader.ReadLine();
        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Response));
        MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(statusResponse));
        Response response = (Response)serializer.ReadObject(ms);

        DateTime startTime = DateTime.Parse(response.Data.StartTime).ToLocalTime();
        Log($"StartTime: {startTime}");
        return startTime;
        throw new Exception("could not obtain current time from data service");
    }

    public static async Task WaitForStartupChange() {
        DateTime startTime = DateTime.Now;
        try {
            using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", "ziti-edge-tunnel.sock", PipeDirection.InOut)) {
                pipeClient.Connect();
                StreamWriter writer = new StreamWriter(pipeClient);
                StreamReader reader = new StreamReader(pipeClient);

                try {
                    startTime = GetCurrentStartTime(writer, reader);
                    Log($"initial start time {startTime}");
                } catch {
                    Log("Could not obtain current time. The service is expected to be down. Using 'now' as current time.");
                }
            }
        } catch (Exception ex) {
            Log($"Error: {ex.Message}");
        }

        while (true) {
            try {
                using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", "ziti-edge-tunnel.sock", PipeDirection.InOut)) {
                    pipeClient.Connect();
                    StreamWriter writer = new StreamWriter(pipeClient);
                    StreamReader reader = new StreamReader(pipeClient);
                    DateTime nextStartTime = GetCurrentStartTime(writer, reader);
                    if (startTime == nextStartTime) {
                        Log($"{startTime} is equal to {nextStartTime}");
                    } else {
                        Log($"{startTime} has changed to {nextStartTime}");
                        return;
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(500)); // wait for the serice to start and return a result
                }
            } catch (Exception ex) {
                Log($"Error: {ex.Message}");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500)); // try again...
        }
    }

    public static async Task RunWithTimeout(Task task, TimeSpan timeout) {
        using (var cancellationTokenSource = new CancellationTokenSource()) {
            if (await Task.WhenAny(task, Task.Delay(timeout)) == task) {
                cancellationTokenSource.Cancel();
                await task;
            } else {
                throw new TimeoutException("The operation has timed out.");
            }
        }
    }
    public static void Log(string message) {
        Console.WriteLine(message);
        File.AppendAllText(logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}\n");
    }

    public static void StartZitiDesktopEdgeUI() {
        Log($"trying to find the UI to start");
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        List<string> dirs = new List<string>(Directory.EnumerateDirectories(programFilesX86, "NetFoundry*"));

        if (dirs.Count > 1) {
            Log($"cannot start the UI. too many directories to search??? Found: {dirs.Count} {string.Join(",", dirs)}");
        } else if (dirs.Count < 1) {
            Log($"cannot start the UI. No ZitiDesktopEdge.exe found");
        } else {
            var zitiFiles = Directory.GetFiles(dirs[0], "ZitiDesktopEdge.exe", SearchOption.AllDirectories);

            foreach (var file in zitiFiles) {
                Console.WriteLine($"Found ZitiDesktopEdge at: {file}");
                using (Process process = new Process()) {
                    process.StartInfo.FileName = file;
                    process.StartInfo.Arguments = "version";
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                    Log($"Started {file}");
                }
            }
        }
    }
}


[DataContract]
public class Response {
    [DataMember]
    public bool Success { get; set; }

    [DataMember]
    public Data Data { get; set; }

    [DataMember]
    public int Code { get; set; }
}

[DataContract]
public class Data {
    [DataMember]
    public bool Active { get; set; }

    [DataMember]
    public long Duration { get; set; }

    [DataMember]
    public string StartTime { get; set; }

    [DataMember]
    public List<Identity> Identities { get; set; }
}

[DataContract]
public class Identity {
    [DataMember]
    public string Name { get; set; }

    [DataMember]
    public string Identifier { get; set; }
}