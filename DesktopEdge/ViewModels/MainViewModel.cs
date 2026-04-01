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
using ZitiDesktopEdge.Models;

namespace ZitiDesktopEdge {
    public class MainViewModel : INotifyPropertyChanged {
        private string _connectLabelContent = "Tap to Connect";
        private string _sortOption;
        private string _sortDirection;
        private bool _isConnected;
        private int _identityCount;

        public MainViewModel() {
            _sortOption = Properties.Settings.Default.SortOption;
            _sortDirection = Properties.Settings.Default.SortDirection;
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
