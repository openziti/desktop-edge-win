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
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge {
    public enum ErrorButtonMode { Close, ForceQuit }

    public class MainViewModel : INotifyPropertyChanged {
        private string _connectLabelContent = "Tap to Connect";
        private string _sortOption;
        private string _sortDirection;
        private bool _isConnected;
        private int _identityCount;

        public MainViewModel() {
            _sortOption = Properties.Settings.Default.SortOption;
            _sortDirection = Properties.Settings.Default.SortDirection;
            CloseErrorCommand = new ActionCommand(ExecuteCloseError, () => CloseButtonEnabled);
            DismissErrorCommand = new ActionCommand(CloseServiceError, () => true);
            SortCommand = new ActionCommand(SortBy, parameter => true);
        }

        public event EventHandler SortChanged;
        public ActionCommand SortCommand { get; }

        private void SortBy(object sortOption) {
            SetSort((string)sortOption);
            SortChanged?.Invoke(this, EventArgs.Empty);
        }

        public ActionCommand QuitCommand { get; } = new ActionCommand(() => Application.Current.Shutdown(), () => true);

        public ActionCommand CloseErrorCommand { get; }
        public ActionCommand DismissErrorCommand { get; }

        private Visibility _noServiceVisibility = Visibility.Collapsed;
        private Visibility _errorViewVisibility = Visibility.Collapsed;
        private string _noServiceTitle = "Service Not Started";
        private string _noServiceDetail = "Start the Ziti Tunnel Service to get started";
        private string _errorTitle = "An Error Occurred";
        private string _errorDetails = "An Unknown Error Occurred, you could try restarting the service and the interface to continue";
        private string _closeButtonContent = "Close Error";
        private Visibility _closeButtonVisibility = Visibility.Visible;
        private bool _closeButtonEnabled = true;
        private ErrorButtonMode _errorButtonMode = ErrorButtonMode.Close;

        public Visibility NoServiceVisibility {
            get { return _noServiceVisibility; }
            private set { _noServiceVisibility = value; OnPropertyChanged(nameof(NoServiceVisibility)); }
        }

        public Visibility ErrorViewVisibility {
            get { return _errorViewVisibility; }
            private set { _errorViewVisibility = value; OnPropertyChanged(nameof(ErrorViewVisibility)); }
        }

        public string NoServiceTitle {
            get { return _noServiceTitle; }
            private set { _noServiceTitle = value; OnPropertyChanged(nameof(NoServiceTitle)); }
        }

        public string NoServiceDetail {
            get { return _noServiceDetail; }
            private set { _noServiceDetail = value; OnPropertyChanged(nameof(NoServiceDetail)); }
        }

        public string ErrorTitle {
            get { return _errorTitle; }
            private set { _errorTitle = value; OnPropertyChanged(nameof(ErrorTitle)); }
        }

        public string ErrorDetails {
            get { return _errorDetails; }
            private set { _errorDetails = value; OnPropertyChanged(nameof(ErrorDetails)); }
        }

        public string CloseButtonContent {
            get { return _closeButtonContent; }
            private set { _closeButtonContent = value; OnPropertyChanged(nameof(CloseButtonContent)); }
        }

        public Visibility CloseButtonVisibility {
            get { return _closeButtonVisibility; }
            private set { _closeButtonVisibility = value; OnPropertyChanged(nameof(CloseButtonVisibility)); }
        }

        public bool CloseButtonEnabled {
            get { return _closeButtonEnabled; }
            set { _closeButtonEnabled = value; OnPropertyChanged(nameof(CloseButtonEnabled)); }
        }

        public void ShowNoService(string title, string detail, bool closeButtonVisible) {
            NoServiceTitle = title;
            NoServiceDetail = detail;
            CloseButtonVisibility = closeButtonVisible ? Visibility.Visible : Visibility.Collapsed;
            CloseButtonEnabled = true;
            NoServiceVisibility = Visibility.Visible;
        }

        public void HideNoService() {
            NoServiceVisibility = Visibility.Collapsed;
        }

        public void ShowServiceError(string title, string message) {
            ErrorTitle = title;
            ErrorDetails = message;
            ErrorViewVisibility = Visibility.Visible;
        }

        public void CloseServiceError() {
            NoServiceVisibility = Visibility.Collapsed;
            ErrorViewVisibility = Visibility.Collapsed;
            CloseButtonEnabled = true;
            SetCloseMode();
        }

        public void SetForceQuitMode() {
            _errorButtonMode = ErrorButtonMode.ForceQuit;
            CloseButtonContent = "Force Quit";
        }

        private void SetCloseMode() {
            _errorButtonMode = ErrorButtonMode.Close;
            CloseButtonContent = "Close Error";
        }

        private async void ExecuteCloseError() {
            if (_errorButtonMode != ErrorButtonMode.ForceQuit) {
                CloseServiceError();
                return;
            }
            MonitorClient monitor = (MonitorClient)Application.Current.Properties["MonitorClient"];
            MonitorServiceStatusEvent status = await monitor.ForceTerminateAsync();
            if (status.IsStopped()) {
                SetCloseMode();
            } else {
                ShowNoService("The Service Is Still Running", "Current status is: " + status.Status, true);
            }
        }

        public string ConnectLabelContent {
            get { return _connectLabelContent; }
            set {
                _connectLabelContent = value;
                OnPropertyChanged(nameof(ConnectLabelContent));
            }
        }

        public string SortOption {
            get { return _sortOption; }
            private set {
                _sortOption = value;
                OnPropertyChanged(nameof(SortOption));
                OnPropertyChanged(nameof(NameArrowVisibility));
                OnPropertyChanged(nameof(StatusArrowVisibility));
                OnPropertyChanged(nameof(ServicesArrowVisibility));
            }
        }

        public string SortDirection {
            get { return _sortDirection; }
            private set {
                _sortDirection = value;
                OnPropertyChanged(nameof(SortDirection));
                OnPropertyChanged(nameof(SortArrowText));
            }
        }

        public int IdentityCount {
            get { return _identityCount; }
            set {
                _identityCount = value;
                OnPropertyChanged(nameof(IdentityCount));
                OnPropertyChanged(nameof(ColumnHeaderVisibility));
            }
        }

        public Visibility ColumnHeaderVisibility => _isConnected && _identityCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility NameArrowVisibility => SortOption == "Name" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility StatusArrowVisibility => SortOption == "Status" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ServicesArrowVisibility => SortOption == "Services" ? Visibility.Visible : Visibility.Collapsed;
        public string SortArrowText => SortDirection == "Descending" ? "▼" : "▲";

        public void SetSort(string option) {
            if (SortOption == option) {
                SortDirection = SortDirection == "Descending" ? "Ascending" : "Descending";
            } else {
                SortOption = option;
                SortDirection = "Descending";
            }
            var settings = Properties.Settings.Default;
            settings.SortOption = SortOption;
            settings.SortDirection = SortDirection;
            settings.Save();
        }

        /// <summary>
        /// Returns a sort rank for an identity's auth state, where lower values
        /// represent statuses that need more urgent user attention.
        /// </summary>
        private int AuthSortOrder(ZitiIdentity identity) {
            if (identity.IsTimedOut) return 0;
            if (identity.IsTimingOut) return 1;
            if (identity.IsMFANeeded) return 2;
            if (identity.NeedsExtAuth) return 3;
            return 4;
        }

        public ZitiIdentity[] GetSortedIdentities(IEnumerable<ZitiIdentity> identities) {
            bool descending = SortDirection == "Descending";
            IEnumerable<ZitiIdentity> sorted;
            switch (SortOption) {
                case "Name":
                    if (descending) {
                        sorted = identities.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase);
                    } else {
                        sorted = identities.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
                    }
                    break;
                case "Services":
                    if (descending) {
                        sorted = identities
                            .OrderByDescending(i => AuthSortOrder(i))
                            .ThenByDescending(i => i.Services.Count);
                    } else {
                        sorted = identities
                            .OrderBy(i => AuthSortOrder(i))
                            .ThenBy(i => i.Services.Count);
                    }
                    break;
                case "Status":
                    if (descending) {
                        sorted = identities.OrderByDescending(i => i.IsEnabled);
                    } else {
                        sorted = identities.OrderBy(i => i.IsEnabled);
                    }
                    break;
                default:
                    sorted = identities.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
                    break;
            }
            return sorted.ToArray();
        }

        public void Disconnected() {
            _isConnected = false;
            ConnectLabelContent = "Tap to Connect";
            OnPropertyChanged(nameof(ColumnHeaderVisibility));
        }

        public void Connected() {
            _isConnected = true;
            ConnectLabelContent = "Tap to Disconnect";
            OnPropertyChanged(nameof(ColumnHeaderVisibility));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
