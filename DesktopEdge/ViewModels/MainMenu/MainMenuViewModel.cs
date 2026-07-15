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

using System.Windows;

namespace ZitiDesktopEdge {
    public class MainMenuViewModel : ViewModelBase {
        public TunnelConfigViewModel TunnelConfig { get; } = new TunnelConfigViewModel();
        public LogLevelViewModel LogLevel { get; } = new LogLevelViewModel();
        public AutomaticUpdatesViewModel AutomaticUpdates { get; } = new AutomaticUpdatesViewModel();

        // Which menu screen is showing. The panels and title derive from this; setting it swaps screens.
        private string _menuState = "Main";
        public string MenuState {
            get { return _menuState; }
            set {
                _menuState = value;
                OnPropertyChanged(nameof(MenuState));
                OnPropertyChanged(nameof(MainItemsVisibility));
                OnPropertyChanged(nameof(AboutVisibility));
                OnPropertyChanged(nameof(AdvancedItemsVisibility));
                OnPropertyChanged(nameof(ConfigItemsVisibility));
                OnPropertyChanged(nameof(LogLevelItemsVisibility));
                OnPropertyChanged(nameof(AutomaticUpgradesItemsVisibility));
                OnPropertyChanged(nameof(IdentitiesVisibility));
                OnPropertyChanged(nameof(BackArrowVisibility));
                OnPropertyChanged(nameof(MenuTitle));
            }
        }

        private bool IsSubPanel =>
            _menuState == "About" || _menuState == "Advanced" || _menuState == "Logs" ||
            _menuState == "UILogs" || _menuState == "LogLevel" ||
            _menuState == "ConfigureAutomaticUpgrades" || _menuState == "Config" ||
            _menuState == "Identities";

        public Visibility MainItemsVisibility => IsSubPanel ? Visibility.Collapsed : Visibility.Visible;
        public Visibility AboutVisibility => _menuState == "About" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AdvancedItemsVisibility => (_menuState == "Advanced" || _menuState == "Logs" || _menuState == "UILogs") ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ConfigItemsVisibility => _menuState == "Config" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility LogLevelItemsVisibility => _menuState == "LogLevel" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AutomaticUpgradesItemsVisibility => _menuState == "ConfigureAutomaticUpgrades" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IdentitiesVisibility => _menuState == "Identities" ? Visibility.Visible : Visibility.Collapsed;
        public Visibility BackArrowVisibility => IsSubPanel ? Visibility.Visible : Visibility.Collapsed;

        public string MenuTitle {
            get {
                switch (_menuState) {
                    case "About": return "About";
                    case "Advanced":
                    case "Logs":
                    case "UILogs": return "Advanced Settings";
                    case "LogLevel": return "Set Log Level";
                    case "ConfigureAutomaticUpgrades": return "Automatic Upgrades";
                    case "Config": return "Tunnel Config";
                    case "Identities": return "Identities";
                    default: return "Main Menu";
                }
            }
        }

        // Sub-screens (Config, Log Level, etc.) go back to Advanced; everything else to the main menu.
        public void NavigateBack() {
            if (_menuState == "Config" || _menuState == "LogLevel" || _menuState == "UILogs" ||
                _menuState == "SetReleaseStream" || _menuState == "ConfigureAutomaticUpgrades") {
                MenuState = "Advanced";
            } else {
                MenuState = "Menu";
            }
        }
    }
}
