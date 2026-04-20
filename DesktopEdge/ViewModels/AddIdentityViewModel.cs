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

namespace ZitiDesktopEdge {
    /// <summary>Subset of the controller's external-jwt-signer response used by the add-identity UI.</summary>
    public class ExternalJwtSigner {
        public string Name { get; set; }
        public bool EnrollToCertEnabled { get; set; }
        public bool EnrollToTokenEnabled { get; set; }
        public bool SupportsAnyEnrollMode => EnrollToCertEnabled || EnrollToTokenEnabled;
    }

    public class AddIdentityViewModel : INotifyPropertyChanged {
        private ExternalJwtSigner _selectedSigner;
        private string _enrollMode;
        private bool _showSignerPicker;

        /// <summary>Initial placeholder shown in the controller URL field, also used to detect whether the user has entered a real URL.</summary>
        public string UrlPlaceholder { get; } = "https://controller.url";

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
                OnPropertyChanged(nameof(SelectedSigner));
            }
        }

        public string EnrollMode {
            get { return _enrollMode; }
            set {
                _enrollMode = value;
                OnPropertyChanged(nameof(EnrollMode));
            }
        }

        /// <summary>
        /// Parses the controller's external-jwt-signers response and replaces <see cref="Signers"/>.
        /// Parsing errors propagate so the caller can surface a specific error.
        /// </summary>
        public void LoadSigners(string signersResponseBody) {
            Signers.Clear();
            SelectedSigner = null;

            ExternalJwtSignerListResponse parsed = JsonConvert.DeserializeObject<ExternalJwtSignerListResponse>(signersResponseBody, DeserializationSettings);
            if (parsed?.Data != null) {
                foreach (ExternalJwtSigner signer in parsed.Data) {
                    Signers.Add(signer);
                }
            }

            // Picker rules:
            //   0 capable signers — no picker, fall through to the current add-by-URL flow.
            //   1 capable signer  — auto-select it; dropdown stays hidden (no choice to make).
            //   2+ capable        — show the dropdown, force the user to pick one.
            List<ExternalJwtSigner> capable = Signers.Where(s => s.SupportsAnyEnrollMode).ToList();
            if (capable.Count == 1) {
                SelectedSigner = capable[0];
            }
            ShowSignerPicker = capable.Count > 1;
        }

        /// <summary>Clears any previously-loaded signer state — call when the controller URL changes.</summary>
        public void Reset() {
            Signers.Clear();
            SelectedSigner = null;
            EnrollMode = null;
            ShowSignerPicker = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Map camelCase wire fields to PascalCase C# properties without per-field attributes.
        private static readonly JsonSerializerSettings DeserializationSettings = new JsonSerializerSettings {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private class ExternalJwtSignerListResponse {
            public List<ExternalJwtSigner> Data { get; set; }
        }
    }
}
