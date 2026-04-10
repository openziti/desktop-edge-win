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
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

class FileWatcher {
    private static string processName = Process.GetCurrentProcess().ProcessName;
    private static string tempDir = $"{Environment.GetEnvironmentVariable("TEMP")}";
    private static string fileName = $"{processName}_{DateTime.Now:yyyyMMddHHmmss}.log";
    private static string logFilePath = Path.Combine(tempDir, fileName);

    private static Form progressForm;
    private static Label statusLabel;
    private static bool showProgress = false;

    [STAThread]
    public static void Main(string[] args) {
        showProgress = Array.Exists(args, a => a == "--show-progress");
        Log($"{processName} started. showProgress={showProgress}");

        try {
            if (Process.GetProcessesByName(processName).Length > 1) {
                Log("Another instance is already running. Exiting...");
                return;
            }

            if (showProgress) {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                progressForm = CreateProgressForm();

                Task.Run(async () => {
                    try {
                        await RunWithTimeout(task: WaitForStartupChange(), timeout: TimeSpan.FromMinutes(5));
                        UpdateStatus("Launching application...");
                        StartZitiDesktopEdgeUI();
                    } catch (TimeoutException) {
                        Log($"{processName} timed out waiting for service restart");
                        UpdateStatus($"Update timed out. Please restart manually.\nSee log: {logFilePath}");
                        await Task.Delay(5000);
                    } catch (Exception e) {
                        Log($"{processName} completed exceptionally: {e}");
                        UpdateStatus($"Update failed. Please restart manually.\nSee log: {logFilePath}");
                        await Task.Delay(5000);
                    } finally {
                        Log($"{processName} completed");
                        CloseForm();
                    }
                });

                Application.Run(progressForm);
            } else {
                Task.Run(async () => {
                    try {
                        await RunWithTimeout(task: WaitForStartupChange(), timeout: TimeSpan.FromMinutes(5));
                        StartZitiDesktopEdgeUI();
                    } catch (TimeoutException) {
                        Log($"{processName} timed out waiting for service restart");
                    }
                }).Wait();
            }
        } catch (Exception e) {
            Log($"{processName} completed exceptionally: {e}");
        } finally {
            Log($"{processName} completed");
        }
    }

    private static string prefsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetFoundry");
    private static string hideProgressFile = Path.Combine(prefsDirectory, "hide-upgrade-progress");

    private static void SaveHidePreference(bool hide) {
        try {
            if (hide) {
                Directory.CreateDirectory(prefsDirectory);
                File.WriteAllText(hideProgressFile, "");
            } else if (File.Exists(hideProgressFile)) {
                File.Delete(hideProgressFile);
            }
        } catch (Exception ex) {
            Log($"Failed to save preference: {ex.Message}");
        }
    }

    private static Form CreateProgressForm() {
        Form form = new Form {
            Text = "Ziti Desktop Edge - Updating",
            Size = new Size(420, 160),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true,
            ShowInTaskbar = true,
        };

        try {
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            form.Icon = Icon.ExtractAssociatedIcon(exePath);
        } catch (Exception ex) {
            Log($"Failed to extract icon: {ex.Message}");
        }

        statusLabel = new Label {
            Text = "Launching installer...",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 50,
            Font = new Font("Segoe UI", 11f),
        };

        ProgressBar progressBar = new ProgressBar {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Dock = DockStyle.Top,
            Height = 25,
        };

        CheckBox hideCheckbox = new CheckBox {
            Text = "Don't show this again",
            Dock = DockStyle.Bottom,
            Height = 25,
            Font = new Font("Segoe UI", 9f),
            Padding = new Padding(5, 0, 0, 0),
        };
        hideCheckbox.CheckedChanged += (sender, e) => SaveHidePreference(hideCheckbox.Checked);

        Panel padding = new Panel { Dock = DockStyle.Top, Height = 10 };

        form.Controls.Add(hideCheckbox);
        form.Controls.Add(progressBar);
        form.Controls.Add(padding);
        form.Controls.Add(statusLabel);

        return form;
    }

    private static void UpdateStatus(string text) {
        Log($"Status: {text}");
        if (!showProgress || progressForm == null || progressForm.IsDisposed) {
            return;
        }
        if (progressForm.InvokeRequired) {
            progressForm.Invoke(new Action(() => statusLabel.Text = text));
        } else {
            statusLabel.Text = text;
        }
    }

    private static void CloseForm() {
        if (progressForm != null && !progressForm.IsDisposed) {
            if (progressForm.InvokeRequired) {
                progressForm.Invoke(new Action(() => progressForm.Close()));
            } else {
                progressForm.Close();
            }
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
    }

    public static async Task WaitForStartupChange() {
        DateTime startTime = DateTime.Now;
        try {
            using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", "ziti-edge-tunnel.sock", PipeDirection.InOut)) {
                pipeClient.Connect(5000);
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

        UpdateStatus("Waiting for services to stop...");
        while (true) {
            try {
                using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", "ziti-edge-tunnel.sock", PipeDirection.InOut)) {
                    pipeClient.Connect(2000);
                    StreamWriter writer = new StreamWriter(pipeClient);
                    StreamReader reader = new StreamReader(pipeClient);
                    DateTime nextStartTime = GetCurrentStartTime(writer, reader);
                    if (nextStartTime != startTime) {
                        Log($"{startTime} has changed to {nextStartTime}");
                        return;
                    }
                    UpdateStatus("Waiting for services to stop...");
                }
            } catch (Exception ex) {
                UpdateStatus("Waiting for services to start...");
                Log($"Error: {ex.Message}");
            }
            await Task.Delay(500);
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
