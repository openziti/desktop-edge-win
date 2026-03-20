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
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.IO.Pipes;
using System.IO;
using System.Threading.Tasks;

using System.Windows.Interop;

using NLog;
using Ziti.Desktop.Edge.Models;
using System.Reflection;
using ZitiDesktopEdge.Utility;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        private const string NamedPipeName = "ZitiDesktopEdgePipe";

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private static Mutex _mutex = null;

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e) {
            base.OnSessionEnding(e);
        }

        protected override void OnStartup(StartupEventArgs e) {
            UpgradeSentinel.RemoveUpgradeSentinelExe();
            try {
                Current.Properties["ZDEWViewState"] = new ZDEWViewState();

                const string appName = "Ziti Desktop Edge";

                bool createdNew;

                _mutex = new Mutex(true, appName, out createdNew);
#if !DEBUG
                if (!createdNew) {
#if DEBUG
                    logger.Info("Another instance exists but running in debug mode - allowing both to run...");
#else
                    using (var client = new NamedPipeClientStream(NamedPipeName)) {
                        logger.Info("Another instance exists. Attempting to notify it to open");
                        try {
                            client.Connect(1000);
                        } catch {
                            return;
                        }

                        if (!client.IsConnected)
                            return;

                        using (StreamWriter writer = new StreamWriter(client)) {
                            writer.Write("showscreen");
                            writer.Flush();
                        }
                    }
                    Application.Current.Shutdown();
#endif
                } else {
#pragma warning disable 4014 //This async method lacks 'await'
                    StartServer();
#pragma warning restore 4014 //This async method lacks 'await'
                }
#endif
            } catch (Exception ex) {
                logger.Error($"OnStartup FAILED unexpectedly. Exiting", ex);
                Application.Current.Shutdown();
            }
        }

        async public Task StartServer() {
            logger.Debug("Starting IPC server to listen for other instances of the app");
            while (true) {
                string text;
                using (var server = new NamedPipeServerStream(NamedPipeName)) {
                    await server.WaitForConnectionAsync();
                    logger.Debug("Another instance opened and connected.");
                    using (StreamReader reader = new StreamReader(server)) {
                        text = await reader.ReadToEndAsync();
                    }
                }

                logger.Debug("received: {0}. Calling OnReceivedString", text);
                OnReceivedString(text);
            }
        }

        public event Action<string> ReceiveString;
        protected virtual void OnReceivedString(string text) => ReceiveString?.Invoke(text);

    }
}