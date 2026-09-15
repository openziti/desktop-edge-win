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

namespace ZitiUpdateService {
    using NLog;
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;

    public class MiniDump {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [DllImport("dbghelp.dll", SetLastError = true)]
        public static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            uint processId,
            IntPtr hFile,
            uint dumpType,
            IntPtr exceptionParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);

        // values from dbghelp.h
        public const uint MiniDumpNormal = 0x00000000;
        public const uint MiniDumpWithFullMemory = 0x00000002;
        public const uint MiniDumpScanMemory = 0x00000010;
        public const uint MiniDumpWithThreadInfo = 0x00001000;
        public const uint MiniDumpWithModuleHeaders = 0x00080000;

        // module headers are required to unwind the dump without the byte-identical binary
        private const uint DumpType = MiniDumpWithThreadInfo | MiniDumpWithModuleHeaders;

        private static string SafeProcessName(Process p) {
            try {
                return p.ProcessName;
            } catch (InvalidOperationException) {
                return "(exited process)";
            }
        }

        /// <summary>
        /// Writes a minidump of the given process. Returns true only when a dump was actually produced.
        /// </summary>
        public static bool CreateMemoryDump(Process procToDump, string outputFile) {
            // read once, up front: ProcessName throws if the process has already exited, and the
            // callers here are killing processes, so that is a normal race and not an error
            string procName = SafeProcessName(procToDump);

            // written to a temp file first: FileMode.Create truncates, so a failed write on the real
            // path would destroy the previous dump and leave 0 bytes
            string tempFile = outputFile + ".tmp";
            try {
                uint processId = (uint)procToDump.Id;
                bool result;
                int lastError = 0;

                using (FileStream fs = new FileStream(tempFile, FileMode.Create)) {
                    IntPtr hFile = fs.SafeFileHandle.DangerousGetHandle();

                    result = MiniDumpWriteDump(
                        procToDump.Handle,
                        processId,
                        hFile,
                        DumpType,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero);

                    // must be read immediately after the P/Invoke, before anything else can clobber it
                    if (!result) {
                        lastError = Marshal.GetLastWin32Error();
                    }
                }

                if (!result) {
                    Logger.Error("Failed to create memory dump of {0} at {1}: {2} ({3})",
                        procName, outputFile,
                        new Win32Exception(lastError).Message, lastError);
                    File.Delete(tempFile);
                    return false;
                }

                if (File.Exists(outputFile)) {
                    File.Delete(outputFile);
                }
                File.Move(tempFile, outputFile);
                Logger.Info("Memory dump of {0} created successfully at {1}", procName, outputFile);
                return true;
            } catch (Exception ex) {
                Logger.Error(ex, "Unexpected error while creating memory dump of {0}", procName);
                try {
                    if (File.Exists(tempFile)) {
                        File.Delete(tempFile);
                    }
                } catch (Exception cleanupEx) {
                    Logger.Warn("Could not remove the partial dump at {0}: {1}", tempFile, cleanupEx.Message);
                }
                return false;
            }
        }
    }
}
