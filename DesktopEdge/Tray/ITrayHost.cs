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

using System.Threading.Tasks;
using ZitiDesktopEdge.Models;

namespace ZitiDesktopEdge.Tray {
    /// <summary>
    /// Surface that <see cref="TrayMenuController"/> calls back into for actions that touch the
    /// rest of the app (window state, dialogs, IPC). Keeps the controller focused on menu
    /// construction and avoids a hard reference to MainWindow. MainWindow implements this.
    /// </summary>
    internal interface ITrayHost {
        /// <summary>Bring the main window to front and activate it.</summary>
        void BringWindowForward();

        /// <summary>Open the Add-Identity-by-JWT file picker flow.</summary>
        void ShowAddIdentityByJwt();

        /// <summary>Open the Add-Identity-by-URL dialog (clipboard pre-fill etc.).</summary>
        void ShowAddIdentityByUrl();

        /// <summary>Show the welcome screen, expanding the window to fit.</summary>
        void ShowWelcomeScreen();

        /// <summary>Open the identity details panel for the given identity.</summary>
        void OpenIdentityDetails(ZitiIdentity identity);

        /// <summary>Apply a new log level across data + monitor clients + local UI.</summary>
        Task<bool> SetLogLevelAsync(string level);

        /// <summary>Switch the active ziti-edge-tunnel instance (Ctrl+Shift+T equivalent).</summary>
        Task SwitchTunnelerAsync(string discriminator);

        /// <summary>Start a feedback log capture (zitiDump + CaptureLogs).</summary>
        void StartFeedbackCapture();
    }
}
