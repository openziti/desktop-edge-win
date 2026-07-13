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
using System.Collections.ObjectModel;
using System.Windows;
using NLog;
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.ServiceClient;

namespace ZitiDesktopEdge {
    public class TunnelConfigViewModel : ViewModelBase {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private bool _l2Enabled;
        public bool L2Enabled {
            get { return _l2Enabled; }
            set {
                _l2Enabled = value;
                if (!value) { UsePcap = false; }
                OnPropertyChanged(nameof(L2Enabled));
                OnPropertyChanged(nameof(IsPcapDropdownEnabled));
            }
        }

        // Only relevant when L2Enabled is true. Controls whether a PcapInterface is sent to the tunneler.
        private bool _usePcap;
        public bool UsePcap {
            get { return _usePcap; }
            set {
                _usePcap = value;
                if (!value) { SelectedPcapInterface = null; }
                OnPropertyChanged(nameof(UsePcap));
                OnPropertyChanged(nameof(IsPcapDropdownEnabled));
            }
        }

        public bool IsPcapDropdownEnabled {
            get { return L2Enabled && UsePcap; }
        }

        public ObservableCollection<string> PcapInterfaces { get; } = new ObservableCollection<string>();

        private string _selectedPcapInterface;
        public string SelectedPcapInterface {
            get { return _selectedPcapInterface; }
            set { _selectedPcapInterface = value; OnPropertyChanged(nameof(SelectedPcapInterface)); }
        }

        private string _configIp;
        public string ConfigIp { get { return _configIp; } set { _configIp = value; OnPropertyChanged(nameof(ConfigIp)); } }

        private string _configSubnet;
        public string ConfigSubnet { get { return _configSubnet; } set { _configSubnet = value; OnPropertyChanged(nameof(ConfigSubnet)); } }

        private string _configMtu;
        public string ConfigMtu { get { return _configMtu; } set { _configMtu = value; OnPropertyChanged(nameof(ConfigMtu)); } }

        private string _configDns;
        public string ConfigDns { get { return _configDns; } set { _configDns = value; OnPropertyChanged(nameof(ConfigDns)); } }

        private string _configPageSize;
        public string ConfigPageSize { get { return _configPageSize; } set { _configPageSize = value; OnPropertyChanged(nameof(ConfigPageSize)); } }

        private string _configDnsEnabled;
        public string ConfigDnsEnabled { get { return _configDnsEnabled; } set { _configDnsEnabled = value; OnPropertyChanged(nameof(ConfigDnsEnabled)); } }

        private string _configL2Enabled;
        public string ConfigL2Enabled { get { return _configL2Enabled; } set { _configL2Enabled = value; OnPropertyChanged(nameof(ConfigL2Enabled)); } }

        private string _configUsePcap;
        public string ConfigUsePcap { get { return _configUsePcap; } set { _configUsePcap = value; OnPropertyChanged(nameof(ConfigUsePcap)); } }

        private string _configPcapInterface;
        public string ConfigPcapInterface { get { return _configPcapInterface; } set { _configPcapInterface = value; OnPropertyChanged(nameof(ConfigPcapInterface)); } }

        private string _editIp;
        public string EditIp { get { return _editIp; } set { _editIp = value; OnPropertyChanged(nameof(EditIp)); } }

        private string _editPageSize;
        public string EditPageSize { get { return _editPageSize; } set { _editPageSize = value; OnPropertyChanged(nameof(EditPageSize)); } }

        private string _editPrefix;
        public string EditPrefix { get { return _editPrefix; } set { _editPrefix = value; OnPropertyChanged(nameof(EditPrefix)); } }

        private bool _editAddDns;
        public bool EditAddDns { get { return _editAddDns; } set { _editAddDns = value; OnPropertyChanged(nameof(EditAddDns)); } }

        public event Action<string> BlurbRequested;
        public event Action ConfigSaved;

        public ActionCommand SaveConfigCommand { get; }

        public TunnelConfigViewModel() {
            SaveConfigCommand = new ActionCommand(SaveConfig, () => true);
        }

        public void ClampPageSize() {
            int defaultVal = 250;
            int value;
            if (!int.TryParse(EditPageSize, out value)) value = defaultVal;
            if (value < 10 || value > 500) value = defaultVal;
            EditPageSize = value.ToString();
        }

        private async void SaveConfig() {
            if (L2Enabled && UsePcap && string.IsNullOrEmpty(SelectedPcapInterface)) {
                BlurbRequested?.Invoke("Select a Pcap Interface");
                return;
            }
            Properties.Settings.Default.Save();
            Logger.Info("updating config...");
            DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
            try {
                int prefixLength = int.Parse(EditPrefix);
                bool addDns = EditAddDns;
                ConfigDnsEnabled = addDns.ToString();
                ClampPageSize();
                int pageSize = int.Parse(EditPageSize);
                ConfigPageSize = EditPageSize;
                ConfigL2Enabled = L2Enabled.ToString();
                ConfigUsePcap = (L2Enabled && UsePcap).ToString();
                string pcapInterface = (L2Enabled && UsePcap) ? SelectedPcapInterface ?? "" : "";
                ConfigPcapInterface = pcapInterface;
                SvcResponse response = await client.UpdateInterfaceConfigAsync(EditIp, prefixLength, addDns, pageSize, L2Enabled, pcapInterface);
                if (response.Code != 0) {
                    BlurbRequested?.Invoke("Error: " + response.Error);
                    Logger.Debug("ERROR: {0} : {1}", response.Message, response.Error);
                } else {
                    Application.Current.Properties["L2Enabled"] = L2Enabled;
                    Application.Current.Properties["PcapInterface"] = pcapInterface;
                    BlurbRequested?.Invoke("Config Save, Please Restart Ziti to Update");
                    ConfigSaved?.Invoke();
                }
                Logger.Info("Got response from update interface config task : {0}", response);
            } catch (ServiceException se) {
                BlurbRequested?.Invoke("Error: " + se.Message);
                Logger.Error(se, "service exception in update config: {0}", se.Message);
            } catch (Exception ex) {
                BlurbRequested?.Invoke("Error: " + ex.Message);
                Logger.Error(ex, "unexpected error in update config: {0}", ex.Message);
            }
        }
    }
}
