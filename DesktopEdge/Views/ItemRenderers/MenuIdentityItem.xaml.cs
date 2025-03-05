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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ZitiDesktopEdge.Models;
using ZitiDesktopEdge.DataStructures;
using ZitiDesktopEdge.ServiceClient;
using Ziti.Desktop.Edge.Utils;

namespace ZitiDesktopEdge {
    /// <summary>
    /// Interaction logic for MenuItem.xaml
    /// </summary>
    public partial class MenuIdentityItem : UserControl {

        private string _label = "";
        private ZitiIdentity _identity;

        public string Label {
            get {
                return _label;
            }
            set {
                this._label = value;
                MainLabel.Text = this._label;
            }
        }

        public ZitiIdentity Identity {
            get {
                return _identity;
            }
            set {
                _identity = value;
            }
        }

        public MenuIdentityItem() {
            InitializeComponent();
            ToggleSwitch.OnToggled += ToggleIdentity;
        }

        async private void ToggleIdentity(bool on) {
            try {
                DataClient client = (DataClient)Application.Current.Properties["ServiceClient"];
                DataStructures.Identity id = await client.IdentityOnOffAsync(_identity.Identifier, on);
                this.Identity.IsEnabled = on;
            } catch (DataStructures.ServiceException se) {
                MessageBox.Show(se.AdditionalInfo, se.Message);
            } catch (Exception ex) {
                MessageBox.Show("Error", ex.Message);
            }
        }

        private void ShowIdentity(object sender, MouseButtonEventArgs e) {
            if (!UIUtils.IsLeftClick(e)) return;
            if (!UIUtils.MouseUpForMouseDown(e)) return;
            IdentityDetails deets = ((MainWindow)Application.Current.MainWindow).IdentityMenu;
            deets.SelectedIdentityMenu = this;
            deets.Identity = this.Identity;
        }

        private void MainUI_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
            UIUtils.ClickedControl = e.Source as UIElement;
        }
    }
}
