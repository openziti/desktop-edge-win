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
using NLog;
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge {
    public interface IUiActions {
        void RefreshIdentities();
        void RefreshDetailsIfOpen();
        void QueueMfaNotification(ZitiIdentity identity);
        void ShowError(string title, string message);
        void ShowAuthenticationFailed(ZitiIdentity identity);
        void ServiceConnected();
        void ServiceDisconnected(object error);
        void ApplyTunnelStatus(TunnelStatus status);
        void ApplyLogLevel(string logLevel);
        void HandleIdentityEvent(IdentityEvent e);
        void HandleMfaEvent(MfaEvent mfa);
        void MonitorConnected();
    }

    public class ServiceClientEventHandlers {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly DataClient _serviceClient;
        private readonly MainViewModel _viewModel;
        private readonly IUiActions _ui;

        public ServiceClientEventHandlers(DataClient serviceClient, MainViewModel viewModel, IUiActions ui) {
            _serviceClient = serviceClient;
            _viewModel = viewModel;
            _ui = ui;
            serviceClient.OnServiceEvent += OnServiceEvent;
            serviceClient.OnBulkServiceEvent += OnBulkServiceEvent;
            serviceClient.OnControllerEvent += OnControllerEvent;
            serviceClient.OnCommunicationError += OnCommunicationError;
            serviceClient.OnAuthenticationEvent += OnAuthenticationEvent;
            serviceClient.OnNotificationEvent += OnNotificationEvent;
            serviceClient.OnClientConnected += OnClientConnected;
            serviceClient.OnClientDisconnected += OnClientDisconnected;
            serviceClient.OnTunnelStatusEvent += OnTunnelStatusEvent;
            serviceClient.OnLogLevelEvent += OnLogLevelEvent;
            serviceClient.OnIdentityEvent += OnIdentityEvent;
            serviceClient.OnMfaEvent += OnMfaEvent;
        }

        private void OnIdentityEvent(object sender, IdentityEvent e) {
            _ui.HandleIdentityEvent(e);
        }

        private void OnMfaEvent(object sender, MfaEvent mfa) {
            _ui.HandleMfaEvent(mfa);
        }

        private void OnClientConnected(object sender, object e) {
            _ui.ServiceConnected();
        }

        private void OnClientDisconnected(object sender, object e) {
            _ui.ServiceDisconnected(e);
        }

        private void OnTunnelStatusEvent(object sender, TunnelStatusEvent e) {
            if (e == null) return;
            _ui.ApplyTunnelStatus(e.Status);
        }

        private void OnLogLevelEvent(object sender, LogLevelEvent e) {
            if (e.LogLevel != null) _ui.ApplyLogLevel(e.LogLevel);
        }

        private void OnNotificationEvent(object sender, NotificationEvent e) {
            bool displayMfa = false;
            foreach (Notification notification in e.Notification) {
                ZitiIdentity found = _viewModel.FindIdentity(notification.Identifier);
                if (found == null) {
                    logger.Warn($"{e.Op} event for {notification.Identifier} but the provided identity identifier was not found!");
                    continue;
                }
                found.TimeoutMessage = notification.Message;
                found.MaxTimeout = notification.MfaMaximumTimeout;
                found.MinTimeout = notification.MfaMinimumTimeout;
                displayMfa = true;
            }
            if (displayMfa) _ui.RefreshDetailsIfOpen();
            _ui.RefreshIdentities();
        }

        private void OnAuthenticationEvent(object sender, AuthenticationEvent e) {
            if (e.Action != "error") return;
            ZitiIdentity found = _viewModel.FindIdentity(e.Identifier);
            if (found == null) return;
            found.AuthInProgress = false;
            _ui.ShowAuthenticationFailed(found);
        }

        private void OnControllerEvent(object sender, ControllerEvent e) {
            logger.Debug($"==== ControllerEvent    : action:{e.Action} identifier:{e.Identifier}");
        }

        private void OnCommunicationError(object sender, Exception e) {
            _serviceClient.Reconnect();
            _ui.ShowError("Operation Timed Out", e.Message);
        }

        private void OnServiceEvent(object sender, ServiceEvent e) {
            if (e == null) return;

            logger.Debug($"==== ServiceEvent : action:{e.Action} identifier:{e.Identifier} name:{e.Service.Name} ");
            IdentityViewModel identityViewModel = _viewModel.FindViewModel(e.Identifier);
            if (identityViewModel == null) {
                logger.Debug($"{e.Action} service event for {e.Service.Name} but the provided identity identifier {e.Identifier} is not found!");
                return;
            }

            if (e.Action == "added") {
                if (identityViewModel.ApplyServiceAdded(e.Service)) _ui.QueueMfaNotification(identityViewModel.Identity);
            } else {
                identityViewModel.ApplyServiceRemoved(e.Service);
            }
            _ui.RefreshIdentities();
            _ui.RefreshDetailsIfOpen();
        }

        private void OnBulkServiceEvent(object sender, BulkServiceEvent e) {
            IdentityViewModel identityViewModel = _viewModel.FindViewModel(e.Identifier);
            if (identityViewModel == null) {
                logger.Warn($"{e.Action} service event for {e.Identifier} but the provided identity identifier was not found!");
                return;
            }
            if (e.RemovedServices != null) {
                foreach (Service removed in e.RemovedServices) {
                    identityViewModel.ApplyServiceRemoved(removed);
                }
            }
            bool needsMfaNotification = false;
            if (e.AddedServices != null) {
                foreach (Service added in e.AddedServices) {
                    if (identityViewModel.ApplyServiceAdded(added)) needsMfaNotification = true;
                }
            }
            if (needsMfaNotification) _ui.QueueMfaNotification(identityViewModel.Identity);
            _ui.RefreshIdentities();
            _ui.RefreshDetailsIfOpen();
        }
    }
}
