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

﻿/*
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
using System.Text.RegularExpressions;
using System.Threading;

class FileWatcher {
	static SemaphoreSlim semaphore = new SemaphoreSlim(0, 1);
	static string lockFilePath = Path.Combine(Path.GetTempPath(), "ZitiDesktopEdgeUpdateSentinelLock.txt");
	static string pathToWatch = @"C:\Program Files (x86)\NetFoundry Inc\Ziti Desktop Edge\ZitiDesktopEdge.exe";

	static void Main() {
		Console.CancelKeyPress += OnCancelKeyPress;
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
		// Check if the lock file exists
		if (File.Exists(lockFilePath)) {

			try {
				File.Delete(lockFilePath);
				Console.WriteLine("Found stale lock file and removed it.");
			} catch {
				AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
				Console.WriteLine("The program is already running. Exiting.");
				return;
			}
		}

		Console.WriteLine($"{lockFilePath} does not exist");
		var f = File.Create(lockFilePath);
		try {
			// Create or overwrite the log file
			string logFilePath = Path.Combine(Path.GetTempPath(), "ZitiDesktopEdgeUpdateSentinel.log");
			using (StreamWriter logWriter = new StreamWriter(logFilePath, false)) {
				// Redirect console output to the log file
				Console.WriteLine($"Upgrade started. Waiting for the upgrade to complete.");
				Console.WriteLine($"Log file created at: {logFilePath}");
				var outwriter = Console.Out;
				Console.SetOut(logWriter);
				if (File.Exists(pathToWatch)) {
					Console.WriteLine($"File exists: {pathToWatch}");
				}
				WatchForFile(pathToWatch);

				Thread.Sleep(1000);
				// Start the process
				Console.WriteLine($"Starting {pathToWatch}");
				using (Process process = new Process()) {
					process.StartInfo.FileName = pathToWatch;
					process.StartInfo.Arguments = "version";
					process.StartInfo.RedirectStandardOutput = true;
					process.StartInfo.UseShellExecute = false;
					process.StartInfo.CreateNoWindow = true;
					process.Start();
					Console.WriteLine($"Started {pathToWatch}");
				}
				logWriter.Flush();
				Console.SetOut(outwriter);
			}
		} finally {
			// Delete the lock file from the user's temporary folder to allow the program to run next time
			f.Close();
			File.Delete(lockFilePath);
		}
	}


	static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e) {
		Console.WriteLine("Ctrl+C detected. Cleaning up...");
		try {
			// Delete the lock file upon termination
			if (File.Exists(lockFilePath)) {
				File.Delete(lockFilePath);
				Console.WriteLine("Lock file deleted.");
			}
		} catch { }
	}

	static void OnProcessExit(object sender, EventArgs e) {
		Console.WriteLine("Process is exiting. Cleaning up...");
		OnCancelKeyPress(sender, null);
	}

	static void WatchForFile(string filePath) {
		// Extract the directory path from the file path
		string directoryPath = Path.GetDirectoryName(filePath);

		// Create a new FileSystemWatcher instance
		FileSystemWatcher watcher = new FileSystemWatcher();

		// Set the path to watch (the directory containing the file)
		watcher.Path = directoryPath;

		// Watch only the specified file
		watcher.Filter = Path.GetFileName(filePath);

		// Subscribe to events
		watcher.Created += OnFileCreated;
		watcher.Deleted += OnFileDeleted;
		watcher.Renamed += OnFileRenamed;

		// Enable the FileSystemWatcher
		watcher.EnableRaisingEvents = true;

		Console.WriteLine($"Watching for changes to file '{filePath}'...");
		semaphore.Wait();
	}

	static void OnFileCreated(object sender, FileSystemEventArgs e) {
		Console.WriteLine($"File created: {e.FullPath}");
		semaphore.Release();
	}

	static void OnFileDeleted(object sender, FileSystemEventArgs e) {
		Console.WriteLine($"File deleted: {e.FullPath}");

		// Uncomment the line below if you want to stop watching after the file is deleted
		// ((FileSystemWatcher)sender).EnableRaisingEvents = false;
	}

	static void OnFileRenamed(object sender, FileSystemEventArgs e) {
		Console.WriteLine($"File renamed: {e.FullPath}");

		// Uncomment the line below if you want to stop watching after the file is deleted
		// ((FileSystemWatcher)sender).EnableRaisingEvents = false;
	}
}