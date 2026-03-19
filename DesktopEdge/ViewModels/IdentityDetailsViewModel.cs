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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using ZitiDesktopEdge.Models;

namespace ZitiDesktopEdge {
    public class IdentityDetailsViewModel : INotifyPropertyChanged {
        private const int ServicesPerPage = 50;

        private ZitiIdentity _identity;
        private ZitiService[] _sortedServices;
        private string _sortBy = "Name";
        private string _sortWay = "Asc";
        private string _filter = "";
        private bool _isLoaded;
        private int _totalServices;
        private string _serviceCountLabel = "0 Services";

        public ObservableCollection<ZitiService> Services { get; } = new ObservableCollection<ZitiService>();

        public ZitiIdentity Identity {
            get { return _identity; }
            set {
                _identity = value;
                OnPropertyChanged(nameof(Identity));
            }
        }

        public string ServiceCountLabel {
            get { return _serviceCountLabel; }
            private set {
                _serviceCountLabel = value;
                OnPropertyChanged(nameof(ServiceCountLabel));
            }
        }

        public int TotalServices {
            get { return _totalServices; }
            private set {
                _totalServices = value;
                OnPropertyChanged(nameof(TotalServices));
            }
        }

        public bool IsLoaded {
            get { return _isLoaded; }
            set {
                _isLoaded = value;
                OnPropertyChanged(nameof(IsLoaded));
            }
        }

        public bool HasSortedServices {
            get { return _sortedServices != null; }
        }

        /// <summary>
        /// Resets sort/filter state and loads the first page of services for the current identity.
        /// </summary>
        public void LoadServices() {
            _sortBy = "Name";
            _sortWay = "Asc";
            _filter = "";
            IsLoaded = false;
            TotalServices = _identity.Services.Count;
            ServiceCountLabel = TotalServices + " service" + (TotalServices != 1 ? "s" : "");
            RebuildServiceList();
            IsLoaded = true;
        }

        /// <summary>
        /// Applies a filter/sort change and reloads the first page.
        /// </summary>
        public void ApplyFilter(string filter, string sortBy, string sortWay) {
            _filter = filter;
            _sortBy = sortBy;
            _sortWay = sortWay;
            IsLoaded = false;
            RebuildServiceList();
            IsLoaded = true;
        }

        /// <summary>
        /// Appends the next page of filtered services. Returns the number of items added.
        /// </summary>
        public int LoadNextPage() {
            if (_sortedServices == null) {
                return 0;
            }

            IsLoaded = false;
            string lowerFilter = _filter.ToLower();
            int added = 0;
            int skip = Services.Count;
            int seen = 0;

            foreach (ZitiService zitiSvc in _sortedServices) {
                if (MatchesFilter(zitiSvc, lowerFilter)) {
                    if (seen >= skip) {
                        if (added >= ServicesPerPage) {
                            break;
                        }
                        zitiSvc.TimeUpdated = _identity.LastUpdatedTime;
                        zitiSvc.IsMfaReady = _identity.IsMFAEnabled;
                        Services.Add(zitiSvc);
                        added++;
                    }
                    seen++;
                }
            }

            IsLoaded = true;
            return added;
        }

        public void Clear() {
            Services.Clear();
            _sortedServices = null;
        }

        private void RebuildServiceList() {
            Services.Clear();
            SortServices();

            string lowerFilter = _filter.ToLower();
            int count = 0;
            foreach (ZitiService zitiSvc in _sortedServices) {
                if (count >= ServicesPerPage) {
                    break;
                }
                if (MatchesFilter(zitiSvc, lowerFilter)) {
                    zitiSvc.TimeUpdated = _identity.LastUpdatedTime;
                    zitiSvc.IsMfaReady = _identity.IsMFAEnabled;
                    Services.Add(zitiSvc);
                    count++;
                }
            }
        }

        private void SortServices() {
            switch (_sortBy) {
                case "Name":
                    _sortedServices = _identity.Services.OrderBy(s => s.Name.ToLower()).ToArray();
                    break;
                case "Address":
                    _sortedServices = _identity.Services.OrderBy(s => s.Addresses.ToString()).ToArray();
                    break;
                case "Protocol":
                    _sortedServices = _identity.Services.OrderBy(s => s.Protocols.ToString()).ToArray();
                    break;
                case "Port":
                    _sortedServices = _identity.Services.OrderBy(s => s.Ports.ToString()).ToArray();
                    break;
                default:
                    _sortedServices = _identity.Services.ToArray();
                    break;
            }
            if (_sortWay == "Desc") {
                _sortedServices = _sortedServices.Reverse().ToArray();
            }
        }

        private static bool MatchesFilter(ZitiService service, string lowerFilter) {
            if (string.IsNullOrEmpty(lowerFilter)) {
                return true;
            }
            return service.Name.ToLower().Contains(lowerFilter) || service.ToString().ToLower().Contains(lowerFilter);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
