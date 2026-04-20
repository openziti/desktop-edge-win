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

        public ObservableCollection<ExternalJwtSigner> Signers { get; } = new ObservableCollection<ExternalJwtSigner>();

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
