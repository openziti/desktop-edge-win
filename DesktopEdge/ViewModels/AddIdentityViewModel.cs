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
using NLog;

using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.Models;

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
        private EnrollmentLink _link;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

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
                OnPropertyChanged(nameof(Header));
            }
        }

        public Visibility SignerPickerVisibility => _showSignerPicker ? Visibility.Visible : Visibility.Collapsed;

        public ExternalJwtSigner SelectedSigner {
            get { return _selectedSigner; }
            set {
                _selectedSigner = value;
                EnrollMode = InitialEnrollMode();
                OnPropertyChanged(nameof(SelectedSigner));
                OnPropertyChanged(nameof(EnrollModeRadiosVisibility));
                OnPropertyChanged(nameof(Header));
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

        private bool SignerAllowsBoth => _selectedSigner != null && _selectedSigner.EnrollToCertEnabled && _selectedSigner.EnrollToTokenEnabled;

        public bool ShowEnrollModeRadios => SignerAllowsBoth && _link?.EnrollTo == null;

        public Visibility EnrollModeRadiosVisibility => ShowEnrollModeRadios ? Visibility.Visible : Visibility.Collapsed;

        public EnrollmentLink Link {
            get { return _link; }
            set {
                _link = value;
                OnPropertyChanged(nameof(Link));
                OnPropertyChanged(nameof(Subtitle));
            }
        }

        public string Subtitle {
            get {
                if (_link == null) return "Choose how this identity connects to the network";
                return "Join the network at " + _link.Controller.Authority + "?";
            }
        }

        public string Header {
            get {
                if (ShowSignerPicker || ShowEnrollModeRadios) return "Configure Enrollment";
                return "Join Network";
            }
        }

        public bool ShowEnrollmentChoices => ShowSignerPicker || ShowEnrollModeRadios || _link != null;

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
            ExternalJwtSigner linkSigner = FindLinkSigner(capable);
            if (capable.Count == 1) {
                SelectedSigner = capable[0];
            } else if (linkSigner != null) {
                SelectedSigner = linkSigner;
            }
            ShowSignerPicker = capable.Count > 1 && linkSigner == null;
        }

        private ExternalJwtSigner FindLinkSigner(List<ExternalJwtSigner> capable) {
            if (_link?.Provider == null) return null;
            ExternalJwtSigner signer = capable.FirstOrDefault(s => s.Name == _link.Provider);
            if (signer == null) {
                logger.Warn("enrollment link provider {provider} is not offered by the network", _link.Provider);
            }
            return signer;
        }

        private string InitialEnrollMode() {
            if (!SignerAllowsBoth) {
                WarnIfLinkTypeNotOffered();
                return null;
            }
            if (_link?.EnrollTo == "cert") return "device-certificate";
            return "user-session";
        }

        private void WarnIfLinkTypeNotOffered() {
            if (_selectedSigner == null || _link?.EnrollTo == null) return;
            if (_link.EnrollTo == PayloadEnrollMode()) return;
            logger.Warn("enrollment link type {type} is not offered by provider {provider}", _link.EnrollTo, _selectedSigner.Name);
        }

        public void Reset() {
            Signers.Clear();
            SelectedSigner = null;
            EnrollMode = null;
            ShowSignerPicker = false;
            Link = null;
            ControllerBaseUrl = null;
            IdentityFilename = null;
        }

        public bool CanJoin {
            get {
                if (ShowSignerPicker && SelectedSigner == null) return false;
                if (ShowEnrollModeRadios && EnrollMode == null) return false;
                return true;
            }
        }

        public EnrollIdentifierPayload BuildEnrollPayload() {
            return new EnrollIdentifierPayload {
                ControllerURL = ControllerBaseUrl,
                JwtContent = _link?.Jwt,
                IdentityFilename = IdentityFilename,
                EnrollMode = PayloadEnrollMode(),
                Provider = PayloadProvider(),
            };
        }

        // The enroll mode ZET receives: "cert" or "token" from the signer's capabilities.
        private string PayloadEnrollMode() {
            if (_selectedSigner == null) return null;
            bool cert = _selectedSigner.EnrollToCertEnabled;
            bool token = _selectedSigner.EnrollToTokenEnabled;
            if (cert && token) return _enrollMode == "device-certificate" ? "cert" : "token";
            if (cert) return "cert";
            if (token) return "token";
            return null;
        }

        private string PayloadProvider() {
            if (Signers.Count(s => s.EnrollToCertEnabled || s.EnrollToTokenEnabled) < 2) return null;
            return _selectedSigner?.Name;
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
