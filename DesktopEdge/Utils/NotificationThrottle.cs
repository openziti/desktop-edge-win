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
using System.Linq;
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Ziti.Desktop.Edge.Utils {

    /// <summary>
    /// Collects identity authorization notifications (ext-auth, MFA, etc.) over a short time window.
    /// If only one arrives, it sends the specific notification. If multiple arrive, it sends a single summary instead.
    /// </summary>
    public class NotificationThrottle {

        private readonly Action<string, string, ToastButton> _sendNotification;
        private readonly Dictionary<string, Action> _pendingNotifications = new Dictionary<string, Action>();
        private readonly DispatcherTimer _throttleTimer;
        private string _header;
        private string _summaryFormat;

        public NotificationThrottle(Action<string, string, ToastButton> sendNotification) {
            _sendNotification = sendNotification;
            _throttleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _throttleTimer.Tick += (s, e) => SendPendingNotifications();
        }

        /// <summary>
        /// Adds a notification to the pending queue. Skips duplicates for the same identity.
        /// Resets the 5-second window each time a new notification arrives.
        /// </summary>
        public void Queue(string identityIdentifier, string header, string message, ToastButton button, string summaryFormat) {
            if (_pendingNotifications.ContainsKey(identityIdentifier)) return;
            _header = header;
            _summaryFormat = summaryFormat;
            _pendingNotifications[identityIdentifier] = () => _sendNotification(header, message, button);
            _throttleTimer.Stop();
            _throttleTimer.Start();
        }

        /// <summary>
        /// Removes a single identity from the seen set so it can trigger notifications again.
        /// </summary>
        public void Remove(string identityIdentifier) {
            _pendingNotifications.Remove(identityIdentifier);
        }

        /// <summary>
        /// Resets all state. Called on service disconnect so notifications re-fire on reconnect.
        /// </summary>
        public void Clear() {
            _throttleTimer.Stop();
            _pendingNotifications.Clear();
        }

        /// <summary>
        /// Fires when the throttle window expires. Sends the individual notification or a summary if count > 1.
        /// </summary>
        private void SendPendingNotifications() {
            _throttleTimer.Stop();
            int count = _pendingNotifications.Count;
            if (count == 0) return;

            bool sendSummary = count > 1;
            if (sendSummary) {
                _sendNotification(_header, string.Format(_summaryFormat, count), null);
            } else {
                // Send the single notification
                _pendingNotifications.Values.First()();
            }
            _pendingNotifications.Clear();
        }
    }
}
