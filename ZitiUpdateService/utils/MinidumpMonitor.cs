using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;


namespace ZitiUpdateService {
    public class MinidumpMonitor {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [Flags]
        private enum MiniDumpType {
            MiniDumpNormal = 0x00000000,
            MiniDumpWithDataSegs = 0x00000001,
            MiniDumpWithFullMemory = 0x00000002,
            MiniDumpWithHandleData = 0x00000004,
            MiniDumpFilterMemory = 0x00000008,
            MiniDumpScanMemory = 0x00000010,
            MiniDumpWithUnloadedModules = 0x00000020,
            MiniDumpWithIndirectlyReferencedMemory = 0x00000040,
            MiniDumpFilterModulePaths = 0x00000080,
            MiniDumpWithProcessThreadData = 0x00000100,
            MiniDumpWithPrivateReadWriteMemory = 0x00000200,
            MiniDumpWithoutOptionalData = 0x00000400,
            MiniDumpWithFullMemoryInfo = 0x00000800,
            MiniDumpWithThreadInfo = 0x00001000,
            MiniDumpWithCodeSegs = 0x00002000
        }

        [DllImport("DbgHelp.dll")]
        private static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            int processId,
            SafeHandle hFile,
            MiniDumpType dumpType,
            IntPtr exceptionParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);

        private readonly string _processName;

        public MinidumpMonitor(string processName) {
            _processName = processName;
        }

        public void StartMonitoring() {
            var targetProcess = Process.GetProcessesByName(_processName);
            if (targetProcess.Length == 0) {
                Logger.Warn($"Target process '{_processName}' not found.");
                return;
            }

            var process = targetProcess[0];
            try {
                process.EnableRaisingEvents = true;
                process.Exited += (sender, e) => {
                    Logger.Warn($"Process {_processName} has exited. Capturing minidump...");

                    string dumpPath = Path.Combine(Environment.CurrentDirectory, $"{_processName}.dmp");
                    CaptureMinidump(process, dumpPath);
                };

                Logger.Info($"Monitoring process {_processName}...");
            } catch(Exception e) {
                Logger.Error($"Unexpected error when trying to watch process: {e.Message}");
            }
        }

        private void CaptureMinidump(Process process, string dumpPath) {
            using (FileStream fs = new FileStream(dumpPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                bool success = MiniDumpWriteDump(
                    process.Handle,
                    process.Id,
                    fs.SafeFileHandle,
                    MiniDumpType.MiniDumpWithFullMemory,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                Console.WriteLine(success
                    ? $"Minidump written to {dumpPath}"
                    : "Failed to write minidump.");
            }
        }
    }
}