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

using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using NLog;
using Ziti.Desktop.Edge.Models;
using Ziti.Desktop.Edge.Utils;
using ZitiDesktopEdge.Models;

namespace ZitiDesktopEdge {
    public interface INotificationService {
        bool Suppress { get; set; }
        bool IsToastEnabled { get; }
        void QueueMfaNotification(ZitiIdentity identity);
        void QueueExtAuthNotification(ZitiIdentity identity);
        void ShowToast(string header, string message, ToastButton button);
        void ShowToast(string message);
        void Remove(string identifier);
        void Clear();
    }

    // App-facing notification layer over the generic NotificationThrottle: builds the identity toasts,
    // makes the OS toast call, and gates on update policy.
    public class NotificationService : INotificationService {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly NotificationThrottle _throttle;
        private int _shownCount = 0;

        public NotificationService() {
            _throttle = new NotificationThrottle(ShowToast, "Authorization Required", "{0} identities require authorization.");
        }

        public bool Suppress {
            get { return _throttle.Suppress; }
            set { _throttle.Suppress = value; }
        }

        // Only show notifications once when automatic updates are disabled.
        public bool IsToastEnabled {
            get {
                if (_shownCount == 0) return true;
                ZDEWViewState state = Application.Current.Properties["ZDEWViewState"] as ZDEWViewState;
                return state == null || !state.AutomaticUpdatesDisabled;
            }
        }

        public void QueueMfaNotification(ZitiIdentity identity) {
            string displayName = string.IsNullOrEmpty(identity.Name) ? identity.Identifier : identity.Name;
            ToastButton button = new ToastButton()
                .SetContent("Authenticate")
                .AddArgument("action", "mfa-auth")
                .AddArgument("identifier", identity.Identifier);
            _throttle.Queue(identity.Identifier, $"{displayName} requires MFA authentication.", button);
        }

        public void QueueExtAuthNotification(ZitiIdentity identity) {
            string displayName = string.IsNullOrEmpty(identity.Name) ? identity.Identifier : identity.Name;
            ToastButton button = new ToastButton()
                .SetContent("Authenticate")
                .AddArgument("action", "ext-auth")
                .AddArgument("identifier", identity.Identifier);
            _throttle.Queue(identity.Identifier, $"{displayName} requires external authentication to access services.", button);
        }

        public void ShowToast(string header, string message, ToastButton button) {
            try {
                logger.Debug("showing toast: {} {}", header, message);
                ToastContentBuilder builder = new ToastContentBuilder()
                    .AddArgument("notbutton", "click")
                    .AddText(header)
                    .AddText(message);
                if (button != null) {
                    builder.AddButton(button);
                }
                builder.Show();
                _shownCount++;
            } catch {
                logger.Warn("couldn't show toast: {} {}", header, message);
            }
        }

        public void ShowToast(string message) {
            ShowToast("Important Notice", message, null);
        }

        public void Remove(string identifier) {
            _throttle.Remove(identifier);
        }

        public void Clear() {
            _throttle.Clear();
        }
    }
}
