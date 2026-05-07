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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using ZitiDesktopEdge.DataStructures;

namespace ZitiDesktopEdge {
    public class ExternalJwtSigner {
        public string Name { get; set; }
        public bool EnrollToCertEnabled { get; set; }
        public bool EnrollToTokenEnabled { get; set; }
    }

    public class AddIdentityViewModel : INotifyPropertyChanged {
        private ExternalJwtSigner _selectedSigner;
        private string _enrollMode;
        private bool _showSignerPicker;

        public string UrlPlaceholder { get; } = "https://controller.url";
        public string ControllerBaseUrl { get; set; }
        public string IdentityFilename { get; set; }

        public ObservableCollection<ExternalJwtSigner> Signers { get; } = new ObservableCollection<ExternalJwtSigner>();

        public bool ShowSignerPicker {
            get { return _showSignerPicker; }
            private set {
                _showSignerPicker = value;
                OnPropertyChanged(nameof(ShowSignerPicker));
                OnPropertyChanged(nameof(SignerPickerVisibility));
            }
        }

        public Visibility SignerPickerVisibility => _showSignerPicker ? Visibility.Visible : Visibility.Collapsed;

        public ExternalJwtSigner SelectedSigner {
            get { return _selectedSigner; }
            set {
                _selectedSigner = value;
                EnrollMode = (value != null && value.EnrollToCertEnabled) ? "user-session" : null;
                OnPropertyChanged(nameof(SelectedSigner));
                OnPropertyChanged(nameof(EnrollModeRadiosVisibility));
            }
        }

        public string EnrollMode {
            get { return _enrollMode; }
            set {
                _enrollMode = value;
                OnPropertyChanged(nameof(EnrollMode));
                OnPropertyChanged(nameof(IsUserSessionSelected));
                OnPropertyChanged(nameof(IsDeviceCertificateSelected));
            }
        }

        public bool IsUserSessionSelected {
            get { return _enrollMode == "user-session"; }
            set { if (value) EnrollMode = "user-session"; }
        }

        public bool IsDeviceCertificateSelected {
            get { return _enrollMode == "device-certificate"; }
            set { if (value) EnrollMode = "device-certificate"; }
        }

        public Visibility EnrollModeRadiosVisibility {
            get {
                if (_selectedSigner == null) return Visibility.Collapsed;
                if (!_selectedSigner.EnrollToCertEnabled) return Visibility.Collapsed;
                return Visibility.Visible;
            }
        }

        public void LoadSigners(string signersResponseBody) {
            Signers.Clear();
            SelectedSigner = null;

            ExternalJwtSignerListResponse parsed = JsonConvert.DeserializeObject<ExternalJwtSignerListResponse>(signersResponseBody, DeserializationSettings);
            if (parsed?.Data != null) {
                foreach (ExternalJwtSigner signer in parsed.Data) {
                    Signers.Add(signer);
                }
            }

            List<ExternalJwtSigner> capable = Signers.Where(s => s.EnrollToCertEnabled || s.EnrollToTokenEnabled).ToList();
            if (capable.Count == 1) SelectedSigner = capable[0];
            ShowSignerPicker = capable.Count > 1;
        }

        public void Reset() {
            Signers.Clear();
            SelectedSigner = null;
            EnrollMode = null;
            ShowSignerPicker = false;
            ControllerBaseUrl = null;
            IdentityFilename = null;
        }

        public bool CanJoin {
            get {
                if (ShowSignerPicker && SelectedSigner == null) return false;
                if (EnrollModeRadiosVisibility == Visibility.Visible && EnrollMode == null) return false;
                return true;
            }
        }

        public EnrollIdentifierPayload BuildEnrollPayload() {
            return new EnrollIdentifierPayload {
                ControllerURL = ControllerBaseUrl,
                IdentityFilename = IdentityFilename,
                EnrollMode = ResolveWireEnrollMode(),
                Provider = ResolveWireProvider(),
            };
        }

        private string ResolveWireEnrollMode() {
            if (_selectedSigner == null) return null;
            bool cert = _selectedSigner.EnrollToCertEnabled;
            bool token = _selectedSigner.EnrollToTokenEnabled;
            if (!cert && token) return "token";
            if (_enrollMode == "device-certificate") return "cert";
            if (_enrollMode == "user-session" && token) return "token";
            return null;
        }

        private string ResolveWireProvider() {
            return _showSignerPicker ? _selectedSigner?.Name : null;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static readonly JsonSerializerSettings DeserializationSettings = new JsonSerializerSettings {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private class ExternalJwtSignerListResponse {
            public List<ExternalJwtSigner> Data { get; set; }
        }
    }
}
