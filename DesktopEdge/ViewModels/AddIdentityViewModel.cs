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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Threading.Tasks;

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
        // Fixed by the Ziti edge API contract. The deployment-specific path prefix
        // (e.g. "/edge/client/v1") is read from the controller's root response instead.
        private const string EdgeClientApiFamily = "edge-client";
        private const string EdgeClientApiVersion = "v1";
        private const string ExternalJwtSignersSuffix = "/external-jwt-signers";
        private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(5);

        private ExternalJwtSigner _selectedSigner;
        private string _enrollMode;
        private bool _isDiscovering;

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

        public bool IsDiscovering {
            get { return _isDiscovering; }
            private set {
                _isDiscovering = value;
                OnPropertyChanged(nameof(IsDiscovering));
            }
        }

        /// <summary>
        /// Replaces <see cref="Signers"/> with the controller's external JWT signers.
        /// The caller provides the already-fetched preflight body so we don't re-GET the root.
        /// HTTP and parsing errors propagate.
        /// </summary>
        public async Task DiscoverSignersAsync(string controllerUrl, string controllerRootBody) {
            Signers.Clear();
            SelectedSigner = null;
            IsDiscovering = true;
            try {
                Uri baseUri = new Uri(controllerUrl);
                string edgeClientPath = ExtractEdgeClientApiPath(controllerRootBody, baseUri);
                Uri signersUri = new Uri(baseUri, edgeClientPath + ExternalJwtSignersSuffix);

                using (HttpClient client = new HttpClient()) {
                    client.Timeout = DiscoveryTimeout;
                    string signersBody = await client.GetStringAsync(signersUri);
                    ExternalJwtSignerListResponse parsed = JsonConvert.DeserializeObject<ExternalJwtSignerListResponse>(signersBody, DeserializationSettings);
                    if (parsed?.Data != null) {
                        foreach (ExternalJwtSigner signer in parsed.Data) {
                            Signers.Add(signer);
                        }
                    }
                }
            } finally {
                IsDiscovering = false;
            }
        }

        private static string ExtractEdgeClientApiPath(string rootBody, Uri baseUri) {
            ControllerRootResponse root = JsonConvert.DeserializeObject<ControllerRootResponse>(rootBody, DeserializationSettings);

            Dictionary<string, ApiVersionInfo> edgeClient = null;
            root?.Data?.ApiVersions?.TryGetValue(EdgeClientApiFamily, out edgeClient);
            ApiVersionInfo v1 = null;
            edgeClient?.TryGetValue(EdgeClientApiVersion, out v1);

            if (string.IsNullOrEmpty(v1?.Path)) {
                throw new InvalidOperationException(
                    $"Controller at {baseUri} did not advertise an '{EdgeClientApiFamily}.{EdgeClientApiVersion}.path' in its root response.");
            }
            return v1.Path;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Map camelCase wire fields to PascalCase C# properties without per-field attributes.
        private static readonly JsonSerializerSettings DeserializationSettings = new JsonSerializerSettings {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private class ControllerRootResponse {
            public ControllerRootData Data { get; set; }
        }

        private class ControllerRootData {
            public Dictionary<string, Dictionary<string, ApiVersionInfo>> ApiVersions { get; set; }
        }

        private class ApiVersionInfo {
            public string Path { get; set; }
        }

        private class ExternalJwtSignerListResponse {
            public List<ExternalJwtSigner> Data { get; set; }
        }
    }
}
