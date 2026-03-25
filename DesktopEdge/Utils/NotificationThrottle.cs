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
using System.Windows.Threading;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Ziti.Desktop.Edge.Utils {

    /// <summary>
    /// Collects identity authorization notifications (ext-auth, MFA, etc.) over a short time window.
    /// If only one arrives, it sends the specific notification. If multiple arrive, it sends a single summary instead.
    /// </summary>
    public class NotificationThrottle {

        private readonly Action<string, string, ToastButton> _sendNotification;
        private readonly HashSet<string> _seenIdentifiers = new HashSet<string>();
        private readonly List<Action> _pendingNotifications = new List<Action>();
        private readonly DispatcherTimer _throttleTimer;
        private int _queuedCount;
        private string _header;
        private string _summaryFormat;

        public NotificationThrottle(Action<string, string, ToastButton> sendNotification) {
            _sendNotification = sendNotification;
            _throttleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _throttleTimer.Tick += (s, e) => Flush();
        }

        /// <summary>
        /// Adds a notification to the pending queue. Skips duplicates for the same identity.
        /// Resets the 5-second window each time a new notification arrives.
        /// </summary>
        public void Queue(string identityIdentifier, string header, string message, ToastButton button, string summaryFormat) {
            if (_seenIdentifiers.Contains(identityIdentifier)) return;
            _seenIdentifiers.Add(identityIdentifier);
            _header = header;
            _summaryFormat = summaryFormat;
            _queuedCount++;
            _pendingNotifications.Add(() => _sendNotification(header, message, button));
            _throttleTimer.Stop();
            _throttleTimer.Start();
        }

        /// <summary>
        /// Removes a single identity from the seen set so it can trigger notifications again.
        /// </summary>
        public void Remove(string identityIdentifier) {
            _seenIdentifiers.Remove(identityIdentifier);
        }

        /// <summary>
        /// Resets all state. Called on service disconnect so notifications re-fire on reconnect.
        /// </summary>
        public void Clear() {
            _throttleTimer.Stop();
            _seenIdentifiers.Clear();
            _pendingNotifications.Clear();
            _queuedCount = 0;
        }

        /// <summary>
        /// Fires when the throttle window expires. Sends the individual notification or a summary depending on count.
        /// </summary>
        private void Flush() {
            _throttleTimer.Stop();
            if (_queuedCount == 0) return;
            if (_queuedCount == 1) {
                _pendingNotifications[0]();
            } else {
                _sendNotification(_header, string.Format(_summaryFormat, _queuedCount), null);
            }
            _pendingNotifications.Clear();
            _queuedCount = 0;
        }
    }
}
