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
                        sorted = identities.OrderByDescending(i => i.Services.Count);
                    } else {
                        sorted = identities.OrderBy(i => i.Services.Count);
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
            ConnectLabelContent = "Tap to Connect";
        }

        public void Connected() {
            ConnectLabelContent = "Tap to Disconnect";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
