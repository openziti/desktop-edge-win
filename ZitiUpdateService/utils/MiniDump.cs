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

        public const uint MiniDumpNormal = 0x00000000;
        public const uint MiniDumpWithFullMemory = 0x00000002;
        public const uint MiniDumpWithThreadInfo = 0x00000010;

        public static void CreateMemoryDump(Process procToDump, string outputFile) {
            try {
                uint processId = (uint)procToDump.Id;

                using (FileStream fs = new FileStream(outputFile, FileMode.Create)) {
                    IntPtr hFile = fs.SafeFileHandle.DangerousGetHandle();

                    // Create the dump using MiniDumpWriteDump
                    bool result = MiniDumpWriteDump(
                        procToDump.Handle,
                        processId,
                        hFile,
                        MiniDumpWithThreadInfo,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero);

                    if (result) {
                        Logger.Info("Memory dump created successfully at {}", outputFile);
                    } else {
                        Logger.Error("Failed to create memory dump?");
                    }
                }
            } catch (Exception ex) {
                Logger.Error("Unexpected error while creating memory dump: {}", ex.Message);
            }
        }
    }
}
